using System;
using UnityEngine;

/// <summary>
/// 한 목표 칸까지 상하좌우 걸음 수를 격자 전체에 미리 구해 두는 거리 지도다 (목표에서 거꾸로 BFS).
/// 여러 몬스터가 같은 목표로 갈 때 몬스터마다 길을 찾지 않고 이웃 칸의 거리만 비교하면 된다.
/// 정적 배치(벽 · 박스 · 없앤 칸 · 라운드 영역)만 보고, 다른 캐릭터의 선점은 한 칸을 고를 때 따로 확인한다.
/// </summary>
public sealed class TileDistanceField
{
    /// <summary>목표에 닿을 수 없는 칸의 거리 값이다.</summary>
    private const int UNREACHABLE = -1;

    /// <summary>같은 거리의 이웃은 위, 오른쪽, 아래, 왼쪽 순서로 고른다.</summary>
    private static readonly Vector2Int[] s_directions =
    {
        Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left
    };

    /// <summary>칸 번호(y * 너비 + x)별 목표까지의 걸음 수이며 맵이 커질 때만 다시 할당한다.</summary>
    private int[] _distance = Array.Empty<int>();
    /// <summary>BFS 대기열을 칸 번호 배열로 대신하며 _distance와 같은 크기로 재사용한다.</summary>
    private int[] _queue = Array.Empty<int>();
    /// <summary>지도를 만든 격자의 가로 칸 수다.</summary>
    private int _width;
    /// <summary>지도를 만든 격자의 세로 칸 수다.</summary>
    private int _height;
    /// <summary>지도를 만든 시점의 정적 배치 버전이다.</summary>
    private int _traversalVersion;
    /// <summary>현재 목표로 지도를 만들었는지 나타낸다.</summary>
    private bool _built;

    /// <summary>모든 칸이 향하는 목표 칸이다.</summary>
    public Vector2Int Goal { get; private set; }

    /// <summary>목표를 바꾸고 다음 조회에서 지도를 다시 만들게 한다.</summary>
    /// <param name="goal">걸음 수의 기준이 될 목표 칸이다.</param>
    public void SetGoal(Vector2Int goal)
    {
        Goal = goal;
        _built = false;
    }

    /// <summary>칸에서 목표까지의 걸음 수를 읽는다. 닿을 수 없거나 격자 밖이면 거짓이다. 배치가 바뀌었으면 먼저 다시 만든다 O(너비 × 높이), 아니면 O(1)</summary>
    /// <param name="map">정적 배치와 그 버전을 제공할 현재 맵이다.</param>
    /// <param name="cell">걸음 수를 읽을 칸이다.</param>
    /// <param name="distance">목표까지의 걸음 수이며 실패하면 -1이다.</param>
    public bool TryGetDistance(MapManager map, Vector2Int cell, out int distance)
    {
        distance = UNREACHABLE;
        if (map == null || !map.IsLoaded)
            return false;
        EnsureBuilt(map);
        if (!IsInside(cell))
            return false;
        distance = _distance[ToIndex(cell)];
        return distance != UNREACHABLE;
    }

    /// <summary>목표에 한 걸음 더 가까운 이웃 가운데 지금 들어갈 수 있는 칸의 방향을 고른다. 그런 이웃이 모두 막혔으면 거짓이다. O(1)</summary>
    /// <param name="map">정적 배치와 그 버전을 제공할 현재 맵이다.</param>
    /// <param name="cell">이동을 출발할 현재 칸이다.</param>
    /// <param name="canEnter">다른 캐릭터의 선점까지 확인할 이동 주체의 통행 조회다.</param>
    /// <param name="direction">고른 이웃 칸의 방향이며 실패하면 0이다.</param>
    public bool TryGetStep(MapManager map, Vector2Int cell, Func<Vector2Int, bool> canEnter, out Vector2Int direction)
    {
        direction = Vector2Int.zero;
        if (!TryGetDistance(map, cell, out int distance) || distance == 0)
            return false;
        // direction 후보는 목표 쪽으로 정확히 한 걸음 줄어드는 이웃이다.
        foreach (Vector2Int candidate in s_directions)
        {
            // next는 현재 칸에서 후보 방향으로 한 칸 떨어진 이웃이다.
            Vector2Int next = cell + candidate;
            if (IsInside(next) && _distance[ToIndex(next)] == distance - 1 && canEnter(next))
            {
                direction = candidate;
                return true;
            }
        }
        return false;
    }

    /// <summary>목표를 처음 쓰거나 정적 배치 버전이 바뀌었으면 지도를 다시 만든다.</summary>
    /// <param name="map">정적 배치와 그 버전을 제공할 현재 맵이다.</param>
    private void EnsureBuilt(MapManager map)
    {
        if (_built && _traversalVersion == map.TraversalVersion &&
            _width == map.Grid.Width && _height == map.Grid.Height)
            return;
        Build(map);
    }

    /// <summary>목표에서 거꾸로 BFS를 돌려 모든 칸의 걸음 수를 채운다. 시간 O(너비 × 높이), 할당은 맵이 커질 때만이다.</summary>
    /// <param name="map">통행 가능한 정적 칸을 알려 줄 현재 맵이다.</param>
    private void Build(MapManager map)
    {
        _width = map.Grid.Width;
        _height = map.Grid.Height;
        _traversalVersion = map.TraversalVersion;
        _built = true;
        // size는 이번 격자의 전체 칸 수다.
        int size = _width * _height;
        if (_distance.Length < size)
        {
            _distance = new int[size];
            _queue = new int[size];
        }
        Array.Fill(_distance, UNREACHABLE, 0, size);
        if (!IsInside(Goal) || !map.IsWalkableCell(Goal))
            return;

        // head와 tail은 대기열 배열에서 꺼낼 위치와 넣을 위치다.
        int head = 0;
        int tail = 0;
        _distance[ToIndex(Goal)] = 0;
        _queue[tail++] = ToIndex(Goal);
        while (head < tail)
        {
            // index는 목표에서 가까운 순서로 꺼낸 칸 번호다.
            int index = _queue[head++];
            // cell은 꺼낸 칸 번호의 격자 좌표다.
            Vector2Int cell = new Vector2Int(index % _width, index / _width);
            // nextDistance는 이 칸의 이웃에 매길 걸음 수다.
            int nextDistance = _distance[index] + 1;
            foreach (Vector2Int direction in s_directions)
            {
                // next는 이번에 거리를 매길 이웃 칸이다.
                Vector2Int next = cell + direction;
                if (!IsInside(next))
                    continue;
                // nextIndex는 이웃 칸의 칸 번호다.
                int nextIndex = ToIndex(next);
                if (_distance[nextIndex] != UNREACHABLE || !map.IsWalkableCell(next))
                    continue;
                _distance[nextIndex] = nextDistance;
                _queue[tail++] = nextIndex;
            }
        }
    }

    /// <summary>칸이 지도를 만든 격자 안에 있는지 확인한다.</summary>
    /// <param name="cell">확인할 칸이다.</param>
    private bool IsInside(Vector2Int cell)
    {
        return cell.x >= 0 && cell.x < _width && cell.y >= 0 && cell.y < _height;
    }

    /// <summary>격자 좌표를 칸 번호로 바꾼다.</summary>
    /// <param name="cell">격자 안의 칸이다.</param>
    private int ToIndex(Vector2Int cell)
    {
        return cell.y * _width + cell.x;
    }
}
