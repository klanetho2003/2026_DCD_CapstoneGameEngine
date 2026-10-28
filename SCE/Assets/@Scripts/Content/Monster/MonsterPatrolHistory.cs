using System.Collections.Generic;
using UnityEngine;

/// <summary>성공한 출발 칸과 방향을 기록하고 같은 순환의 완주 횟수를 세는 재사용 기록이다.</summary>
public sealed class MonsterPatrolHistory
{
    /// <summary>최근 기록 초기화 이후 각 출발 칸에서 마지막으로 성공한 이동 방향이다.</summary>
    private readonly Dictionary<Vector2Int, Vector2Int> _moves = new(64);

    /// <summary>직전에 확인한 순환의 출발 칸과 방향으로 같은 경로인지 비교한다.</summary>
    private readonly Dictionary<Vector2Int, Vector2Int> _cycle = new(64);

    /// <summary>새로 발견한 순환 구간을 임시 구성하며 할당 없이 재사용한다.</summary>
    private readonly Dictionary<Vector2Int, Vector2Int> _candidate = new(64);

    /// <summary>BFS에서 통과는 가능하지만 목적지로 선택할 수 없는 이동 기록의 칸들이다.</summary>
    private readonly HashSet<Vector2Int> _excluded = new();

    /// <summary>같은 순환을 실제로 완주한 횟수다.</summary>
    public int Repetitions { get; private set; }

    /// <summary>현재 이동 내역의 항목 수로 초기화와 완료 기록을 확인할 수 있다.</summary>
    public int Count => _moves.Count;

    /// <summary>탈출 준비 취소와 BFS 목적지 제외에 사용하는 기록 타일 집합이다.</summary>
    public ISet<Vector2Int> ExcludedCells => _excluded;

    /// <summary>도착을 성공한 이동만 출발 칸을 키, 이동 방향을 값으로 저장한다.</summary>
    /// <param name="origin">방금 이동하기 전에 선점하고 있던 타일이다.</param>
    /// <param name="direction">출발 칸에서 도착 칸으로 향하는 한 칸의 정수 방향이다.</param>
    public void RecordArrival(Vector2Int origin, Vector2Int direction)
    {
        _moves[origin] = direction;
    }

    /// <summary>현재 칸에서 같은 방향으로 다시 출발하려는 경우 순환을 세고 용량을 유지한 채 기록을 비운다.</summary>
    /// <param name="current">다음 이동을 판단하는 현재 도착 타일이다.</param>
    /// <param name="direction">통행 가능 확인을 마친 다음 이동 방향이다.</param>
    public bool DetectCycle(Vector2Int current, Vector2Int direction)
    {
        // 현재 칸에서 과거에 성공한 방향이며 이번 방향과 같아야 순환 후보가 된다.
        if (!_moves.TryGetValue(current, out Vector2Int recorded) || recorded != direction)
            return false;

        _candidate.Clear();
        // 출발 방향 기록을 연결하여 순환 경로에 속한 실제 칸들만 수집한다.
        Vector2Int cursor = current;
        // next는 현재 기록 칸에서 다음 기록 칸으로 향하는 성공 이동 방향이다.
        while (_moves.TryGetValue(cursor, out Vector2Int next) && !_candidate.ContainsKey(cursor))
        {
            _candidate.Add(cursor, next);
            cursor += next;
        }
        // 같은 칸과 방향들로 이루어진 경로인지 확인하여 다른 순환은 새로 센다.
        bool same = _cycle.Count == _candidate.Count;
        // entry는 새 순환 후보의 출발 칸과 방향이며 previous는 기존 순환의 방향이다.
        foreach (KeyValuePair<Vector2Int, Vector2Int> entry in _candidate)
        {
            if (!_cycle.TryGetValue(entry.Key, out Vector2Int previous) || previous != entry.Value)
                same = false;
        }

        if (!same)
        {
            _cycle.Clear();
            _excluded.Clear();
            // entry는 이후 순환과 비교하기 위해 보존할 출발 칸과 방향이다.
            foreach (KeyValuePair<Vector2Int, Vector2Int> entry in _candidate)
                _cycle.Add(entry.Key, entry.Value);
            Repetitions = 0;
        }
        Repetitions++;

        // cell은 기록을 지운 뒤에도 BFS 목적지에서 제외하기 위해 보존할 출발 칸이다.
        foreach (Vector2Int cell in _moves.Keys)
            _excluded.Add(cell);
        _moves.Clear();
        return true;
    }

    /// <summary>완전 봉쇄 시 현재 칸을 탈출 목적지에서 제외한다.</summary>
    /// <param name="current">완전 봉쇄가 발생한 타일이다.</param>
    public void MarkBlocked(Vector2Int current)
    {
        _excluded.Add(current);
    }

    /// <summary>4초 예고 중 추가된 기록도 목적지에서 제외한 뒤 이동 내역을 비운다.</summary>
    public void PrepareSearch()
    {
        // cell은 탈출 예고 도중 추가 기록되어 목적지에서 제외해야 하는 칸이다.
        foreach (Vector2Int cell in _moves.Keys)
            _excluded.Add(cell);
        _moves.Clear();
    }

    /// <summary>배치 변경과 탈출 완료 시 모든 항목을 비우되 컬렉션 인스턴스와 용량을 유지한다.</summary>
    public void Clear()
    {
        _moves.Clear();
        _cycle.Clear();
        _candidate.Clear();
        _excluded.Clear();
        Repetitions = 0;
    }
}
