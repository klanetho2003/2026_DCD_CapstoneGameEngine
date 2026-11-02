using System;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// 순찰 한 칸 — 정면 → 오른쪽 → 뒤 → 왼쪽 가운데 처음 들어갈 수 있는 칸으로 한 칸 간다 (TilePatrolRule · MonsterTileStepper — 기존 이동 제어기와 같은 규칙 · 속도).
/// 선점까지 마친 도착이면 성공, 네 방향이 다 막혔거나 출발하지 못했거나 도착 없이 끝나면 (양보 · 물리 정체 · 라운드 정지) 실패.
/// trackLoops면 도착을 탈출 추적기에 기록하고, 순찰 단계에서 같은 순환을 세 번 완주하거나 네 방향이 막히면 탈출 예고를 시작한다 (TileEscapeDue · TileEscapeStep과 함께 쓴다).
/// 도착을 바로 알도록 매 프레임 갱신한다. 끊기면 진행 중인 한 칸이 끝날 때까지 미룬다 (칸 사이에서 멈추지 않는다)
/// </summary>
[Serializable]
[BehaviorNode("TilePatrolStep", "순찰 한 칸", "정면 → 오른쪽 → 뒤 → 왼쪽 중 처음 들어갈 수 있는 칸으로 한 칸 — 도착하면 성공", Category = "Tile")]
public sealed class BehaviorTilePatrolStepTask : BehaviorTask
{
    /// <summary>도착을 탈출 추적기에 기록하고 세 바퀴 순환 · 완전 봉쇄면 탈출 예고를 시작한다</summary>
    [JsonProperty("trackLoops")] public bool TrackLoops;

    public override bool UpdateEveryFrame => true;

    public override EBehaviorStatus OnStart(BehaviorTreeRunner runner)
    {
        var agent = runner.Agent as IBehaviorTileAgent;
        if (agent == null)
            return EBehaviorStatus.Failure;

        MonsterTileStepper stepper = agent.Stepper;
        MonsterEscapeTracker tracker = TrackLoops ? agent.EscapeTracker : null;
        Vector2Int cell = stepper.CurrentCell;
        if (TilePatrolRule.TryChooseDirection(cell, stepper.Facing, stepper.CanEnter, out Vector2Int direction) == false)
        {
            tracker?.NotePatrolBlocked(cell);
            return EBehaviorStatus.Failure; // 네 방향이 다 막힘 — 트리가 다음 평가에 다시
        }
        tracker?.NotePatrolDirection(cell, direction);
        if (stepper.TryStartStep(direction, tracker?.RecordArrivalCallback) == false)
            return EBehaviorStatus.Failure;
        runner.MarkMoveIssued();
        return EBehaviorStatus.Running;
    }

    public override EBehaviorStatus OnUpdate(BehaviorTreeRunner runner)
    {
        var agent = runner.Agent as IBehaviorTileAgent;
        if (agent == null)
            return EBehaviorStatus.Failure;

        MonsterTileStepper stepper = agent.Stepper;
        if (stepper.IsAwaitingArrival)
            return stepper.TryConsumeCancelled() ? EBehaviorStatus.Failure : EBehaviorStatus.Running;
        return stepper.LastStepArrived ? EBehaviorStatus.Success : EBehaviorStatus.Failure;
    }

    public override EBehaviorAbortResult OnAbort(BehaviorTreeRunner runner)
    {
        return BehaviorTileStepAbort.Update(runner);
    }

    public override EBehaviorAbortResult OnAbortUpdate(BehaviorTreeRunner runner)
    {
        return BehaviorTileStepAbort.Update(runner);
    }
}

/// <summary>
/// 탈출 한 칸 — 예고가 끝났으면 BFS로 기록 밖 가장 가까운 칸까지의 경로를 찾아 (MonsterEscapeTracker — 기존 이동 제어기와 같은 규칙) 그 경로로 한 칸 간다.
/// 경로가 없으면 다른 캐릭터의 선점이 바뀔 때까지 제자리에서 기다리고 (워프 없음), 다음 칸이 막혔거나 도착 없이 끝나면 경로를 다시 찾는다.
/// 선점까지 마친 도착이면 성공 (기록 밖에 닿았으면 추적기는 순찰 단계로), 탈출할 차례가 아니면 (순찰 · 예고 중) 실패.
/// 매 프레임 갱신한다. 끊기면 진행 중인 한 칸이 끝날 때까지 미룬다. 칸 0: Int0 = 이번 실행에서 한 칸을 출발했으면 1
/// </summary>
[Serializable]
[BehaviorNode("TileEscapeStep", "탈출 한 칸", "BFS로 기록 밖 가장 가까운 칸을 향해 한 칸 — 도착하면 성공, 길이 없으면 기다린다", Category = "Tile")]
public sealed class BehaviorTileEscapeStepTask : BehaviorTask
{
    public override int MemorySlotCount => 1;

    public override bool UpdateEveryFrame => true;

    public override EBehaviorStatus OnStart(BehaviorTreeRunner runner)
    {
        runner.Memory(this).Int0 = 0;
        return Advance(runner);
    }

    public override EBehaviorStatus OnUpdate(BehaviorTreeRunner runner)
    {
        return Advance(runner);
    }

    public override EBehaviorAbortResult OnAbort(BehaviorTreeRunner runner)
    {
        return BehaviorTileStepAbort.Update(runner);
    }

    public override EBehaviorAbortResult OnAbortUpdate(BehaviorTreeRunner runner)
    {
        return BehaviorTileStepAbort.Update(runner);
    }

    /// <summary>출발한 한 칸의 결과를 보고, 없으면 탈출 단계에 맞춰 경로를 찾거나 기다리거나 다음 칸으로 출발한다</summary>
    private EBehaviorStatus Advance(BehaviorTreeRunner runner)
    {
        var agent = runner.Agent as IBehaviorTileAgent;
        if (agent == null)
            return EBehaviorStatus.Failure;

        MonsterTileStepper stepper = agent.Stepper;
        MonsterEscapeTracker tracker = agent.EscapeTracker;
        ref BehaviorNodeMemory memory = ref runner.Memory(this);
        if (memory.Int0 == 1)
        {
            if (stepper.IsAwaitingArrival)
            {
                if (stepper.TryConsumeCancelled() == false)
                    return EBehaviorStatus.Running;
                memory.Int0 = 0;
                if (tracker.State == MonsterMovementState.Escaping)
                    tracker.BeginEscape(stepper.CurrentCell); // 양보 · 물리 정체로 도착 없이 끝남 — 경로를 다시 찾는다
                return EBehaviorStatus.Running;
            }
            if (stepper.LastStepArrived)
                return EBehaviorStatus.Success;
            memory.Int0 = 0; // 라운드 정지 · 배치 변경이 한 칸을 되돌렸다 — 다시 정한다
        }

        Vector2Int cell = stepper.CurrentCell;
        if (tracker.IsPreparationOver || tracker.IsTrapReleased)
            tracker.BeginEscape(cell);
        if (tracker.State == MonsterMovementState.Trapped)
            return EBehaviorStatus.Running; // 길이 없음 — 다른 캐릭터의 선점이 바뀔 때까지 제자리
        if (tracker.State != MonsterMovementState.Escaping)
            return EBehaviorStatus.Failure; // 탈출할 차례가 아니다 (순찰 · 예고 중)
        if (tracker.TryGetEscapeDirection(cell, out Vector2Int direction) == false)
            return EBehaviorStatus.Running; // 경로를 다시 찾았다 — 다음 프레임에 출발
        if (stepper.TryStartStep(direction, tracker.RecordArrivalCallback) == false)
            return EBehaviorStatus.Running;
        memory.Int0 = 1;
        runner.MarkMoveIssued();
        return EBehaviorStatus.Running;
    }
}

/// <summary>칸 이동 Task의 끊기 — 진행 중인 한 칸이 도착하거나 도착 없이 끝날 때까지 미룬다 (칸 사이에서 멈추거나 칸 중앙으로 되돌아가지 않는다)</summary>
internal static class BehaviorTileStepAbort
{
    public static EBehaviorAbortResult Update(BehaviorTreeRunner runner)
    {
        var agent = runner.Agent as IBehaviorTileAgent;
        if (agent == null || agent.Stepper.IsAwaitingArrival == false)
            return EBehaviorAbortResult.Done;
        return agent.Stepper.TryConsumeCancelled() ? EBehaviorAbortResult.Done : EBehaviorAbortResult.Pending;
    }
}
