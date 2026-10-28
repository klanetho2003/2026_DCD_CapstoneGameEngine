using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>기록 타일의 통과를 허용하면서 기록 밖의 가장 가까운 타일까지 BFS 경로를 구한다.</summary>
public sealed class MonsterEscapePathfinder
{
    /// <summary>같은 거리의 경로는 위, 오른쪽, 아래, 왼쪽 순서로 탐색한다.</summary>
    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left
    };

    /// <summary>BFS의 방문 대기 칸이며 경로 탐색마다 비워서 재사용한다.</summary>
    private readonly Queue<Vector2Int> _frontier = new();

    /// <summary>방문 여부와 도착 경로 복원을 위한 이전 칸을 함께 저장한다.</summary>
    private readonly Dictionary<Vector2Int, Vector2Int> _parents = new(64);

    /// <summary>이동 가능한 칸 중 제외 집합 밖의 가장 가까운 목적지를 찾아 경로를 반환한다.</summary>
    /// <param name="start">실제로 도착한 현재 타일이다.</param>
    /// <param name="excluded">통과할 수 있지만 최종 목적지로 삼지 않을 기록 타일이다.</param>
    /// <param name="canEnter">벽, 영역, 다른 캐릭터 선점을 확인할 공통 조회 함수다.</param>
    /// <param name="path">시작 칸을 제외한 이동 순서로 채우며 실패하면 비운다.</param>
    public bool TryFindPath(Vector2Int start, ISet<Vector2Int> excluded,
        Func<Vector2Int, bool> canEnter, List<Vector2Int> path)
    {
        path.Clear();
        _frontier.Clear();
        _parents.Clear();
        _frontier.Enqueue(start);
        _parents.Add(start, start);

        while (_frontier.Count > 0)
        {
            // 시작 칸에서 가까운 순서로 꺼낸 현재 탐색 칸이다.
            Vector2Int cell = _frontier.Dequeue();
            // direction은 거리 우선 탐색에서 사용할 상, 우, 하, 좌 중 한 방향이다.
            foreach (Vector2Int direction in Directions)
            {
                // 이번 칸에서 한 번의 4방향 이동으로 도달할 이웃 칸이다.
                Vector2Int next = cell + direction;
                if (_parents.ContainsKey(next) || !canEnter(next))
                    continue;
                _parents.Add(next, cell);
                if (!excluded.Contains(next))
                {
                    BuildPath(start, next, path);
                    return true;
                }
                _frontier.Enqueue(next);
            }
        }
        return false;
    }

    /// <summary>발견한 목적지에서 부모 칸을 역추적한 뒤 실제 이동 순서로 뒤집는다.</summary>
    /// <param name="start">경로에 포함하지 않을 출발 칸이다.</param>
    /// <param name="goal">BFS가 선택한 기록 밖의 목적지다.</param>
    /// <param name="path">복원한 경로를 저장할 재사용 목록이다.</param>
    private void BuildPath(Vector2Int start, Vector2Int goal, List<Vector2Int> path)
    {
        // 부모를 따라 출발 칸까지 거슬러 올라가는 현재 칸이다.
        Vector2Int cursor = goal;
        while (cursor != start)
        {
            path.Add(cursor);
            cursor = _parents[cursor];
        }
        path.Reverse();
    }
}
