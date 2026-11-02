using System;
using UnityEngine;
using static Define;

/// <summary>몬스터가 순찰, 탈출 예고, BFS 이탈, 봉쇄 대기 중 어느 상태인지 구분한다.</summary>
public enum MonsterMovementState
{
    /// <summary>직진과 우회전 우선 규칙으로 이동한다.</summary>
    Patrol,
    /// <summary>기본 이동을 유지하면서 4초 동안 탈출을 예고한다.</summary>
    PreparingEscape,
    /// <summary>BFS가 찾은 기록 밖 목적지를 향해 한 칸씩 이동한다.</summary>
    Escaping,
    /// <summary>탈출 경로가 없어 이동 환경이 바뀔 때까지 워프 없이 기다린다.</summary>
    Trapped
}

/// <summary>몬스터 한 개의 이동 판단을 관리한다. 방향 규칙은 TilePatrolRule, 한 칸 출발은 몬스터의 MonsterTileStepper, 성공한 도착 기록과 세 바퀴 순환 뒤의 탈출은 몬스터의 MonsterEscapeTracker를 쓴다.</summary>
public sealed class MonsterMovementController
{
    /// <summary>같은 순환을 완주해야 하는 횟수다.</summary>
    public const int REQUIRED_REPETITIONS = MonsterEscapeTracker.REQUIRED_REPETITIONS;

    /// <summary>탈출 탐색을 시작하기 전에 기본 이동을 유지할 예고 시간이다.</summary>
    public const float PREPARATION_SECONDS = MonsterEscapeTracker.PREPARATION_SECONDS;

    /// <summary>이 행동 상태를 소유한 몬스터다.</summary>
    private readonly MonsterBase _owner;

    /// <summary>판단 전에 맵을 읽었는지 확인할 맵이다.</summary>
    private readonly MapManager _map;

    /// <summary>한 칸 출발과 도착 결과, 시선, 통행 조회를 관리하는 몬스터의 공용 출발기다.</summary>
    private readonly MonsterTileStepper _stepper;

    /// <summary>성공한 도착 기록과 순환 감지, 예고, BFS 이탈, 봉쇄 대기를 관리하는 몬스터의 공용 탈출 추적기다.</summary>
    private readonly MonsterEscapeTracker _tracker;

    /// <summary>한 칸 도착 때마다 새 델리게이트를 만들지 않기 위한 완료 콜백이다.</summary>
    private readonly Action _onArrived;

    /// <summary>현재 몬스터의 행동 단계이며 몬스터의 탈출 추적기가 보관한다.</summary>
    public MonsterMovementState State => _tracker.State;

    /// <summary>정지 중에도 유지되는 논리적인 시선 및 직진 방향이며 몬스터의 출발기가 보관한다.</summary>
    public Vector2Int Facing => _stepper.Facing;

    /// <summary>현재 탈출 예고가 끝날 때까지 남은 초다.</summary>
    public float PreparationRemaining => _tracker.PreparationRemaining;

    /// <summary>현재 확인한 동일 순환의 완주 횟수다.</summary>
    public int LoopRepetitions => _tracker.LoopRepetitions;

    /// <summary>이동을 소유하는 몬스터와 맵, 재사용 콜백을 연결한다.</summary>
    /// <param name="owner">한 칸 이동을 실행하고 도착을 알려줄 몬스터다.</param>
    public MonsterMovementController(MonsterBase owner)
    {
        _owner = owner;
        _map = Managers.Map;
        _stepper = owner.Stepper;
        _tracker = owner.EscapeTracker;
        _onArrived = OnArrived;
        Reset();
    }

    /// <summary>새 생애나 배치 변경, 탈출 완료 시 기록을 비우고 순찰로 돌아간다.</summary>
    /// <param name="resetFacing">참이면 초기 방향인 오른쪽으로 되돌린다.</param>
    public void Reset(bool resetFacing = true)
    {
        _tracker.Reset();
        _stepper.Reset(resetFacing);
    }

    /// <summary>플레이 중에만 타이머를 진행하고 칸 중앙에서 다음 행동을 한 번 판단한다.</summary>
    /// <param name="deltaTime">현재 게임 시간 배율이 반영된 프레임 경과 시간이다.</param>
    public void Tick(float deltaTime)
    {
        if (_owner == null || _owner.Movement == null || !_owner.Movement.HasCell || !_map.IsLoaded)
            return;
        if (_owner.IsDead || Managers.Stage.Phase != EStagePhase.Playing)
        {
            _stepper.Cancel();
            return;
        }

        if (_tracker.IsTraversalStale)
        {
            _owner.Movement.CancelToCurrentCell();
            Reset(false);
        }

        if (_stepper.TryConsumeCancelled() && _tracker.State == MonsterMovementState.Escaping)
        {
            // 양보나 물리 장애물로 취소된 이동은 성공한 출발 기록에 남기지 않는다.
            _tracker.BeginEscape(_owner.Movement.CurrentCell);
            return;
        }

        _tracker.TickPreparation(deltaTime);

        if (_owner.Movement.IsMoving)
            return;

        // 이번 판단의 기준이 되는 실제 도착 타일이다.
        Vector2Int cell = _owner.Movement.CurrentCell;
        if (_tracker.IsPreparationOver)
        {
            _tracker.BeginEscape(cell);
            return;
        }

        if (_tracker.State == MonsterMovementState.Trapped)
        {
            if (_tracker.IsTrapReleased)
                _tracker.BeginEscape(cell);
            return;
        }

        if (_tracker.State == MonsterMovementState.Escaping)
        {
            // 탈출 경로의 다음 칸으로 향하는 방향이다.
            if (_tracker.TryGetEscapeDirection(cell, out Vector2Int escapeDirection))
                _stepper.TryStartStep(escapeDirection, _onArrived);
            return;
        }

        // 정상 이동에서 우선순위를 만족한 실제 다음 방향이다.
        if (!TilePatrolRule.TryChooseDirection(cell, Facing, _stepper.CanEnter, out Vector2Int direction))
        {
            _tracker.NotePatrolBlocked(cell);
            return;
        }

        _tracker.NotePatrolDirection(cell, direction);
        _stepper.TryStartStep(direction, _onArrived);
    }

    /// <summary>선점까지 완료한 이동만 기록하고 예고 취소 또는 탈출 완료를 판정한다.</summary>
    private void OnArrived()
    {
        if (_tracker.RecordArrival())
            _stepper.Reset(false);

        // 플레이어처럼 도착한 물리 주기 안에서 다음 이동을 준비하여 칸마다 한 프레임 쉬지 않는다.
        Tick(0f);
    }
}
