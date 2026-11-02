using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>몬스터 한 개의 성공한 도착 기록과 세 바퀴 순환 뒤의 탈출 단계(예고, BFS 이탈, 봉쇄 대기)를 관리하며 기존 이동 제어기와 행동 트리가 같은 규칙을 쓰도록 몬스터마다 하나를 둔다.</summary>
public sealed class MonsterEscapeTracker
{
    /// <summary>같은 순환을 완주해야 하는 횟수다.</summary>
    public const int REQUIRED_REPETITIONS = 3;

    /// <summary>탈출 탐색을 시작하기 전에 기본 이동을 유지할 예고 시간이다.</summary>
    public const float PREPARATION_SECONDS = 4f;

    /// <summary>현재 칸과 이번 한 칸의 출발 칸 및 방향, 공통 통행 조회를 제공하는 몬스터의 출발기다.</summary>
    private readonly MonsterTileStepper _stepper;

    /// <summary>타일별 출발 방향과 순환 횟수를 용량 재할당 없이 관리하는 기록이다.</summary>
    private readonly MonsterPatrolHistory _history = new();

    /// <summary>탈출 목적지와 경로를 찾는 재사용 BFS 탐색기다.</summary>
    private readonly MonsterEscapePathfinder _pathfinder = new();

    /// <summary>탈출 이동에서 차례로 도착해야 할 타일 목록이다.</summary>
    private readonly List<Vector2Int> _path = new();

    /// <summary>탈출 경로에서 다음에 이동할 목록 위치다.</summary>
    private int _pathIndex;

    /// <summary>경로와 순환 기록이 유효했던 마지막 정적 배치 버전이다.</summary>
    private int _traversalVersion;

    /// <summary>봉쇄 대기 중 불필요한 재탐색을 막기 위한 마지막 선점 버전이다.</summary>
    private int _occupancyVersion;

    /// <summary>현재 탈출 단계다.</summary>
    public MonsterMovementState State { get; private set; }

    /// <summary>현재 탈출 예고가 끝날 때까지 남은 초다.</summary>
    public float PreparationRemaining { get; private set; }

    /// <summary>현재 확인한 동일 순환의 완주 횟수다.</summary>
    public int LoopRepetitions => _history.Repetitions;

    /// <summary>탈출 예고가 끝나 경로를 찾을 차례인지 나타낸다.</summary>
    public bool IsPreparationOver => State == MonsterMovementState.PreparingEscape && PreparationRemaining <= 0f;

    /// <summary>봉쇄 대기 중 다른 캐릭터의 선점이 바뀌어 경로를 다시 찾을 차례인지 나타낸다.</summary>
    public bool IsTrapReleased => State == MonsterMovementState.Trapped && _occupancyVersion != CurrentMap.OccupancyVersion;

    /// <summary>예고가 끝났거나 탈출 또는 봉쇄 대기 중이라 순찰 대신 탈출을 진행할 차례인지 나타낸다.</summary>
    public bool IsEscapeDue => State == MonsterMovementState.Escaping ||
        State == MonsterMovementState.Trapped || IsPreparationOver;

    /// <summary>기록 이후 맵의 정적 배치가 바뀌어 기록과 경로를 비워야 하는지 나타낸다.</summary>
    public bool IsTraversalStale => _traversalVersion != CurrentMap.TraversalVersion;

    /// <summary>출발기에 넘겨 이번 한 칸의 도착을 기록하게 하는 통지이며 생성 시 한 번만 만든다.</summary>
    public Action RecordArrivalCallback { get; }

    /// <summary>환경 변경 버전을 제공하는 지금 맵이다.</summary>
    private static MapManager CurrentMap => Managers.Map;

    /// <summary>출발기를 연결한다. 생애를 시작할 때 부르는 쪽이 Reset으로 지금 맵의 버전을 적는다.</summary>
    /// <param name="stepper">현재 칸과 이번 한 칸의 출발 정보를 제공할 몬스터의 출발기다.</param>
    public MonsterEscapeTracker(MonsterTileStepper stepper)
    {
        _stepper = stepper;
        RecordArrivalCallback = () => RecordArrival();
    }

    /// <summary>새 생애나 배치 변경, 탈출 완료 시 기록과 경로, 예고를 비우고 순찰 단계로 돌아간다.</summary>
    public void Reset()
    {
        _history.Clear();
        _path.Clear();
        _pathIndex = 0;
        PreparationRemaining = 0f;
        State = MonsterMovementState.Patrol;
        _traversalVersion = CurrentMap.TraversalVersion;
        _occupancyVersion = CurrentMap.OccupancyVersion;
    }

    /// <summary>예고 중이면 남은 시간을 줄인다.</summary>
    /// <param name="deltaTime">플레이 중 흐른 게임 시간이다.</param>
    public void TickPreparation(float deltaTime)
    {
        if (State == MonsterMovementState.PreparingEscape)
            PreparationRemaining = Mathf.Max(0f, PreparationRemaining - deltaTime);
    }

    /// <summary>순찰에서 네 방향이 모두 막혔을 때 순찰 단계면 현재 칸을 목적지에서 제외하고 예고를 시작한다.</summary>
    /// <param name="cell">완전 봉쇄가 발생한 현재 타일이다.</param>
    public void NotePatrolBlocked(Vector2Int cell)
    {
        if (State != MonsterMovementState.Patrol)
            return;
        _history.MarkBlocked(cell);
        BeginPreparation();
    }

    /// <summary>순찰에서 고른 방향으로 출발하기 직전, 순찰 단계에서 같은 순환을 세 번 완주했으면 예고를 시작한다.</summary>
    /// <param name="cell">다음 이동을 판단하는 현재 도착 타일이다.</param>
    /// <param name="direction">통행 가능 확인을 마친 다음 이동 방향이다.</param>
    public void NotePatrolDirection(Vector2Int cell, Vector2Int direction)
    {
        if (State == MonsterMovementState.Patrol &&
            _history.DetectCycle(cell, direction) &&
            _history.Repetitions >= REQUIRED_REPETITIONS)
            BeginPreparation();
    }

    /// <summary>탈출 단계의 다음 한 칸 방향을 구하며 경로가 끝났거나 다음 칸이 막혔으면 경로를 다시 찾고 거짓을 반환한다.</summary>
    /// <param name="cell">실제로 도착한 현재 타일이다.</param>
    /// <param name="direction">다음 경로 칸으로 향하는 방향이며 실패 시 0이다.</param>
    public bool TryGetEscapeDirection(Vector2Int cell, out Vector2Int direction)
    {
        if (_pathIndex >= _path.Count || !_stepper.CanEnter(_path[_pathIndex]))
        {
            BeginEscape(cell);
            direction = Vector2Int.zero;
            return false;
        }
        direction = _path[_pathIndex] - cell;
        return true;
    }

    /// <summary>기록 타일을 보존하여 BFS를 실행하고 경로가 없으면 워프 없는 봉쇄 대기로 전환한다.</summary>
    /// <param name="cell">실제로 도착한 현재 타일이다.</param>
    public void BeginEscape(Vector2Int cell)
    {
        _history.MarkBlocked(cell);
        _history.PrepareSearch();
        _pathIndex = 0;
        PreparationRemaining = 0f;
        _occupancyVersion = CurrentMap.OccupancyVersion;
        State = _pathfinder.TryFindPath(cell, _history.ExcludedCells, _stepper.CanEnter, _path)
            ? MonsterMovementState.Escaping : MonsterMovementState.Trapped;
    }

    /// <summary>선점까지 완료한 이번 한 칸의 도착을 기록하고, 탈출이나 예고 중 기록 밖 타일에 닿으면 순찰 단계로 돌아가며 참을 반환한다.</summary>
    public bool RecordArrival()
    {
        _history.RecordArrival(_stepper.StepOrigin, _stepper.StepDirection);
        if (State == MonsterMovementState.Escaping)
            _pathIndex++;
        else if (State != MonsterMovementState.PreparingEscape)
            return false;
        if (_history.ExcludedCells.Contains(_stepper.CurrentCell))
            return false;
        Reset();
        return true;
    }

    /// <summary>세 바퀴 순환 또는 완전 봉쇄 뒤 4초 예고를 한 번 시작한다.</summary>
    private void BeginPreparation()
    {
        State = MonsterMovementState.PreparingEscape;
        PreparationRemaining = PREPARATION_SECONDS;
    }
}
