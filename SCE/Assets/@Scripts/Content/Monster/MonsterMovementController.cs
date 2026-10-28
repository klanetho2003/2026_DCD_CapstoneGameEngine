using System;
using System.Collections.Generic;
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

/// <summary>몬스터 한 개의 이동 판단과 성공한 도착 기록, 세 바퀴 순환 뒤의 탈출을 관리한다.</summary>
public sealed class MonsterMovementController
{
    /// <summary>같은 순환을 완주해야 하는 횟수다.</summary>
    public const int REQUIRED_REPETITIONS = 3;

    /// <summary>탈출 탐색을 시작하기 전에 기본 이동을 유지할 예고 시간이다.</summary>
    public const float PREPARATION_SECONDS = 4f;

    /// <summary>이 행동 상태를 소유한 몬스터다.</summary>
    private readonly MonsterBase _owner;

    /// <summary>통행 가능 여부와 선점, 환경 변경 버전을 제공하는 맵이다.</summary>
    private readonly MapManager _map;

    /// <summary>타일별 출발 방향과 순환 횟수를 용량 재할당 없이 관리하는 기록이다.</summary>
    private readonly MonsterPatrolHistory _history = new();

    /// <summary>탈출 목적지와 경로를 찾는 재사용 BFS 탐색기다.</summary>
    private readonly MonsterEscapePathfinder _pathfinder = new();

    /// <summary>탈출 이동에서 차례로 도착해야 할 타일 목록이다.</summary>
    private readonly List<Vector2Int> _path = new();

    /// <summary>한 칸 도착 때마다 새 델리게이트를 만들지 않기 위한 완료 콜백이다.</summary>
    private readonly Action _onArrived;

    /// <summary>BFS에 전달할 공통 통행 조회 콜백이다.</summary>
    private readonly Func<Vector2Int, bool> _canEnter;

    /// <summary>도착 후 Dictionary의 키로 기록할 실제 출발 타일이다.</summary>
    private Vector2Int _pendingOrigin;

    /// <summary>도착 후 Dictionary의 값으로 기록할 실제 이동 방향이다.</summary>
    private Vector2Int _pendingDirection;

    /// <summary>시작한 이동의 성공 콜백을 아직 기다리는지 나타낸다.</summary>
    private bool _awaitingArrival;

    /// <summary>탈출 경로에서 다음에 이동할 목록 위치다.</summary>
    private int _pathIndex;

    /// <summary>경로와 순환 기록이 유효했던 마지막 정적 배치 버전이다.</summary>
    private int _traversalVersion;

    /// <summary>봉쇄 대기 중 불필요한 재탐색을 막기 위한 마지막 선점 버전이다.</summary>
    private int _occupancyVersion;

    /// <summary>현재 몬스터의 행동 단계다.</summary>
    public MonsterMovementState State { get; private set; }

    /// <summary>정지 중에도 유지되는 논리적인 시선 및 직진 방향이다.</summary>
    public Vector2Int Facing { get; private set; } = Vector2Int.right;

    /// <summary>현재 탈출 예고가 끝날 때까지 남은 초다.</summary>
    public float PreparationRemaining { get; private set; }

    /// <summary>현재 확인한 동일 순환의 완주 횟수다.</summary>
    public int LoopRepetitions => _history.Repetitions;

    /// <summary>이동을 소유하는 몬스터와 맵, 재사용 콜백을 연결한다.</summary>
    /// <param name="owner">한 칸 이동을 실행하고 도착을 알려줄 몬스터다.</param>
    public MonsterMovementController(MonsterBase owner)
    {
        _owner = owner;
        _map = Managers.Map;
        _onArrived = OnArrived;
        _canEnter = CanEnter;
        Reset();
    }

    /// <summary>새 생애나 배치 변경, 탈출 완료 시 기록을 비우고 순찰로 돌아간다.</summary>
    /// <param name="resetFacing">참이면 초기 방향인 오른쪽으로 되돌린다.</param>
    public void Reset(bool resetFacing = true)
    {
        _history.Clear();
        _path.Clear();
        _pathIndex = 0;
        _awaitingArrival = false;
        PreparationRemaining = 0f;
        State = MonsterMovementState.Patrol;
        _traversalVersion = _map.TraversalVersion;
        _occupancyVersion = _map.OccupancyVersion;
        if (resetFacing)
            Facing = Vector2Int.right;
    }

    /// <summary>플레이 중에만 타이머를 진행하고 칸 중앙에서 다음 행동을 한 번 판단한다.</summary>
    /// <param name="deltaTime">현재 게임 시간 배율이 반영된 프레임 경과 시간이다.</param>
    public void Tick(float deltaTime)
    {
        if (_owner == null || _owner.Movement == null || !_owner.Movement.HasCell || !_map.IsLoaded)
            return;
        if (_owner.IsDead || Managers.Stage.Phase != EStagePhase.Playing)
        {
            _owner.Movement.CancelToCurrentCell();
            _awaitingArrival = false;
            return;
        }

        if (_traversalVersion != _map.TraversalVersion)
        {
            _owner.Movement.CancelToCurrentCell();
            Reset(false);
        }

        if (_awaitingArrival && !_owner.Movement.IsMoving)
        {
            // 양보나 물리 장애물로 취소된 이동은 성공한 출발 기록에 남기지 않는다.
            _awaitingArrival = false;
            if (State == MonsterMovementState.Escaping)
            {
                BeginEscape();
                return;
            }
        }

        if (State == MonsterMovementState.PreparingEscape)
            PreparationRemaining = Mathf.Max(0f, PreparationRemaining - deltaTime);

        if (_owner.Movement.IsMoving)
            return;

        if (State == MonsterMovementState.PreparingEscape && PreparationRemaining <= 0f)
        {
            BeginEscape();
            return;
        }

        if (State == MonsterMovementState.Trapped)
        {
            if (_occupancyVersion != _map.OccupancyVersion)
                BeginEscape();
            return;
        }

        if (State == MonsterMovementState.Escaping)
        {
            if (_pathIndex >= _path.Count || !CanEnter(_path[_pathIndex]))
                BeginEscape();
            else
                TryStartStep(_path[_pathIndex] - _owner.Movement.CurrentCell);
            return;
        }

        // 정상 이동에서 우선순위를 만족한 실제 다음 방향이다.
        if (!TryChoosePatrolDirection(out Vector2Int direction))
        {
            if (State == MonsterMovementState.Patrol)
            {
                _history.MarkBlocked(_owner.Movement.CurrentCell);
                BeginPreparation();
            }
            return;
        }

        if (State == MonsterMovementState.Patrol &&
            _history.DetectCycle(_owner.Movement.CurrentCell, direction) &&
            _history.Repetitions >= REQUIRED_REPETITIONS)
            BeginPreparation();

        TryStartStep(direction);
    }

    /// <summary>정면, 오른쪽, 뒤쪽, 왼쪽 순서로 실제 진입 가능한 방향을 선택한다.</summary>
    /// <param name="selected">통행 가능한 다음 한 칸 방향이며 실패 시 0이다.</param>
    private bool TryChoosePatrolDirection(out Vector2Int selected)
    {
        // 각 후보를 오른쪽으로 90도씩 돌리며 확인할 방향이다.
        Vector2Int direction = Facing;
        // attempt는 정면부터 오른쪽으로 회전하며 검사한 방향 수다.
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (CanEnter(_owner.Movement.CurrentCell + direction))
            {
                selected = direction;
                return true;
            }
            direction = new Vector2Int(direction.y, -direction.x);
        }
        selected = Vector2Int.zero;
        return false;
    }

    /// <summary>한 칸 이동의 기록 후보를 보관하고 플레이어의 실제 속도에 맞춰 이동을 요청한다.</summary>
    /// <param name="direction">현재 칸에서 도착하려는 인접 칸의 방향이다.</param>
    private bool TryStartStep(Vector2Int direction)
    {
        if (_owner.MoveSpeed <= 0f)
            return false;
        _pendingOrigin = _owner.Movement.CurrentCell;
        _pendingDirection = direction;

        // 플레이어의 현재 최종 이동 속도를 비교할 빙의 대상이다.
        Villager player = Managers.Object.PossessedTarget;
        // 플레이어가 없으면 기본 속도, 있으면 동일한 실제 속도를 적용할 배율이다.
        float speedWeight = player != null && player.Movement != null
            ? player.Movement.EffectiveMoveSpeed / _owner.MoveSpeed : 1f;
        _awaitingArrival = _owner.Movement.TryMoveToCell(
            _pendingOrigin + direction, speedWeight, _onArrived);
        if (_awaitingArrival)
            Facing = direction;
        return _awaitingArrival;
    }

    /// <summary>선점까지 완료한 이동만 기록하고 예고 취소 또는 탈출 완료를 판정한다.</summary>
    private void OnArrived()
    {
        _awaitingArrival = false;
        _history.RecordArrival(_pendingOrigin, _pendingDirection);
        if (State == MonsterMovementState.Escaping)
        {
            _pathIndex++;
            if (!_history.ExcludedCells.Contains(_owner.Movement.CurrentCell))
                Reset(false);
        }
        else if (State == MonsterMovementState.PreparingEscape &&
            !_history.ExcludedCells.Contains(_owner.Movement.CurrentCell))
        {
            Reset(false);
        }

        // 플레이어처럼 도착한 물리 주기 안에서 다음 이동을 준비하여 칸마다 한 프레임 쉬지 않는다.
        Tick(0f);
    }

    /// <summary>세 바퀴 순환 또는 완전 봉쇄 뒤 4초 예고를 한 번 시작한다.</summary>
    private void BeginPreparation()
    {
        State = MonsterMovementState.PreparingEscape;
        PreparationRemaining = PREPARATION_SECONDS;
    }

    /// <summary>기록 타일을 보존하여 BFS를 실행하고 경로가 없으면 워프 없는 봉쇄 대기로 전환한다.</summary>
    private void BeginEscape()
    {
        _history.MarkBlocked(_owner.Movement.CurrentCell);
        _history.PrepareSearch();
        _pathIndex = 0;
        PreparationRemaining = 0f;
        _occupancyVersion = _map.OccupancyVersion;
        State = _pathfinder.TryFindPath(_owner.Movement.CurrentCell,
            _history.ExcludedCells, _canEnter, _path)
            ? MonsterMovementState.Escaping : MonsterMovementState.Trapped;
    }

    /// <summary>순찰과 BFS가 같은 벽, 영역, 다른 캐릭터 선점 기준을 사용하도록 조회한다.</summary>
    /// <param name="cell">다음 이동 또는 탐색 후보인 타일이다.</param>
    private bool CanEnter(Vector2Int cell)
    {
        return _map.CanEnterCell(cell, _owner.Movement);
    }
}
