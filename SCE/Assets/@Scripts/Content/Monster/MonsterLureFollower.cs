using UnityEngine;

/// <summary>
/// 몬스터 한 개가 활성 유인 객체(GridLure)를 향해 가는 동안 행동 트리를 멈추고 거리 지도를 따라 한 칸씩 이동시킨다. 몬스터마다 하나를 둔다.
/// 유인 객체는 대상만 정하고 (Follow · Release), 행동 트리를 멈추고 다시 시작하는 일은 몬스터 자신의 갱신(Tick)에서만 한다.
/// 진행 중인 한 칸은 끝까지 간다 — 칸 사이에서 멈추지 않는다 (행동 트리의 칸 이동 끊기와 같은 규칙).
/// </summary>
public sealed class MonsterLureFollower
{
    /// <summary>한 칸 이동과 행동 트리를 가진 몬스터다.</summary>
    private readonly MonsterBase _owner;
    /// <summary>행동 트리를 멈추고 유인 이동이 몸을 맡고 있는지 나타낸다.</summary>
    private bool _engaged;

    /// <summary>따라가는 유인 객체이며 없으면 null이다.</summary>
    public GridLure Target { get; private set; }

    /// <summary>유인 이동을 실행할 몬스터를 연결한다.</summary>
    /// <param name="owner">출발기와 행동 트리 실행기, 탈출 추적기를 가진 몬스터다.</param>
    public MonsterLureFollower(MonsterBase owner)
    {
        _owner = owner;
    }

    /// <summary>켜진 유인 객체를 따라가게 한다. 다른 유인을 따라가던 중이면 새 유인으로 바꾼다. 실제 전환은 다음 Tick에서 한다.</summary>
    /// <param name="lure">방금 켜진 유인 객체다.</param>
    public void Follow(GridLure lure)
    {
        if (lure != null)
            Target = lure;
    }

    /// <summary>그 유인 객체를 따라가던 중이면 그만두게 한다. 실제로 행동 트리로 돌아가는 것은 다음 Tick에서 한다.</summary>
    /// <param name="lure">방금 꺼진 유인 객체다.</param>
    public void Release(GridLure lure)
    {
        if (Target == lure)
            Target = null;
    }

    /// <summary>새 생애 · 풀 반환 · 사망 시 대상과 진행 상태를 비운다. 행동 트리는 부르는 쪽이 정리한다.</summary>
    public void Reset()
    {
        Target = null;
        _engaged = false;
    }

    /// <summary>
    /// 매 프레임 (라운드 진행 중 · 살아 있을 때). 유인 이동이 몸을 맡았으면 true — 그 프레임은 행동 트리를 갱신하지 않는다.
    /// 갈 수 있는 유인을 처음 맡을 때 행동 트리를 조용히 멈추고 탈출 기록을 비운다 (갈 수 없거나 이미 그 칸이면 맡지 않는다).
    /// 유인이 꺼졌거나 더는 갈 수 없거나 그 칸에 닿으면, 진행 중인 한 칸을 마친 뒤 행동 트리를 루트부터 바로 다시 평가하게 하고 false.
    /// O(1) (거리 지도를 다시 만드는 프레임만 O(칸 수))
    /// </summary>
    /// <param name="actionLocked">스킬 · 기절 · 강제 이동 중이라 새 한 칸을 출발하지 않는지다.</param>
    public bool Tick(bool actionLocked)
    {
        MonsterTileStepper stepper = _owner.Stepper;
        if (!_engaged)
        {
            if (Target == null)
                return false;
            if (!HasWayToTarget(stepper.CurrentCell))
            {
                Target = null; // 갈 수 없거나 이미 그 칸 — 행동 트리는 그대로
                return false;
            }
            _engaged = true;
            _owner.BehaviorRunner.Stop();
            _owner.EscapeTracker.Reset(); // 유인으로 끌려가면 순찰 순환 · 탈출 예고 기록은 뜻을 잃는다
        }

        if (stepper.IsAwaitingArrival && !stepper.TryConsumeCancelled())
            return true; // 진행 중인 한 칸은 끝까지 간다

        // cell은 다음 한 칸을 고를 현재 선점 칸이다.
        Vector2Int cell = stepper.CurrentCell;
        if (!HasWayToTarget(cell))
        {
            Target = null;
            _engaged = false;
            _owner.BehaviorRunner.Start();
            _owner.BehaviorRunner.RequestEvaluation(); // 흩뜨림 대기 없이 원래 규칙으로 바로
            return false;
        }

        if (!actionLocked && Target.TryGetStep(cell, stepper.CanEnter, out Vector2Int direction))
            stepper.TryStartStep(direction, null);
        return true; // 막혔으면 비킬 때까지 제자리에서 기다린다
    }

    /// <summary>켜진 대상이 있고 그 칸에 아직 닿지 않았으며 갈 길이 있는지 확인한다.</summary>
    /// <param name="cell">몬스터가 선점한 현재 칸이다.</param>
    private bool HasWayToTarget(Vector2Int cell)
    {
        return Target != null && Target.IsActive &&
            Target.TryGetDistance(cell, out int distance) && distance > 0;
    }
}
