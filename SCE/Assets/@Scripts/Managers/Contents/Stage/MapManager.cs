using Data;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using static Define;

/// <summary>맵 데이터와 라운드 영역, 도착한 캐릭터의 타일 선점을 관리한다.</summary>
public class MapManager
{
    /// <summary>현재 로드한 타일과 스테이지 데이터다.</summary>
    public Data.MapData CurrentMap { get; private set; }
    /// <summary>현재 맵 데이터가 사용 가능한지 나타낸다.</summary>
    public bool IsLoaded { get { return CurrentMap != null; } }

    /// <summary>현재 맵과 라운드의 화면 표시를 담당한다.</summary>
    private readonly MapView _view = new MapView();

    /// <summary>현재 맵의 좌표 변환과 크기 규약을 제공한다.</summary>
    public ref readonly GridSpec Grid { get { return ref _grid; } }
    /// <summary>맵 데이터에서 복사한 현재 격자 규약이다.</summary>
    private GridSpec _grid;

    private TerrainArea _area; // 현재 라운드의 영역
    /// <summary>현재 라운드의 영역. 맵이 없거나 라운드가 시작되기 전이면 null.</summary>
    public TerrainArea Area { get { return _area; } }

    /// <summary>맵에서 지정한 플레이어 스폰 타일 정보다.</summary>
    private ETileType[,] _playerSpawnPoint;

    /// <summary>도착을 완료하여 각 타일을 선점한 캐릭터의 이동 컴포넌트다.</summary>
    private readonly Dictionary<Vector2Int, CreatureMovement> _occupants = new();

    /// <summary>박스 등 외부 시스템이 벽으로 등록한 추가 차단 타일이다.</summary>
    private readonly HashSet<Vector2Int> _blockedCells = new();

    /// <summary>맵, 라운드 영역, 벽 배치가 변경될 때 증가하는 경로 환경 버전이다.</summary>
    public int TraversalVersion { get; private set; }

    /// <summary>캐릭터의 선점 등록과 해제 시 증가하는 동적 장애물 버전이다.</summary>
    public int OccupancyVersion { get; private set; }

    /// <summary>맵 경계와 벽, 현재 라운드 영역을 기준으로 통과 가능한 타일인지 확인한다.</summary>
    /// <param name="cell">검사할 타일의 정수 좌표다.</param>
    public bool IsWalkableCell(Vector2Int cell)
    {
        return IsLoaded && cell.x >= 0 && cell.x < _grid.Width &&
            cell.y >= 0 && cell.y < _grid.Height &&
            CurrentMap.CollisionRows[cell.y][cell.x] != MAP_TOOL_WALL &&
            !_blockedCells.Contains(cell) && !IsOutsideArea(cell);
    }

    /// <summary>정적 장애물과 다른 캐릭터의 도착 후 선점을 함께 검사한다.</summary>
    /// <param name="cell">진입을 시도할 타일 좌표다.</param>
    /// <param name="requester">자신의 선점은 장애물로 취급하지 않을 이동 주체다.</param>
    public bool CanEnterCell(Vector2Int cell, CreatureMovement requester)
    {
        // occupant는 검사 칸에 실제 도착하여 선점한 이동 주체다.
        return IsWalkableCell(cell) &&
            (!_occupants.TryGetValue(cell, out CreatureMovement occupant) ||
             occupant == null || !occupant.CanOccupyCell || occupant == requester);
    }

    /// <summary>출발 시 예약하지 않고 실제 도착 또는 스폰 시점에 타일 선점을 등록한다.</summary>
    /// <param name="cell">현재 캐릭터가 도착한 타일 좌표다.</param>
    /// <param name="requester">해당 칸을 선점하려는 이동 주체다.</param>
    public bool TryClaimCell(Vector2Int cell, CreatureMovement requester)
    {
        if (requester == null || !CanEnterCell(cell, requester))
            return false;
        // occupant는 같은 칸의 중복 등록 여부를 확인할 기존 소유자다.
        if (_occupants.TryGetValue(cell, out CreatureMovement occupant) && occupant == requester)
            return true;
        _occupants[cell] = requester;
        OccupancyVersion++;
        return true;
    }

    /// <summary>요청한 캐릭터가 실제로 소유한 선점만 해제하여 다른 캐릭터의 기록을 보호한다.</summary>
    /// <param name="cell">해제할 이전 도착 타일이다.</param>
    /// <param name="requester">해당 선점을 해제하려는 이동 주체다.</param>
    public void ReleaseCell(Vector2Int cell, CreatureMovement requester)
    {
        // occupant는 타인의 선점이 해제되지 않도록 비교할 현재 소유자다.
        if (_occupants.TryGetValue(cell, out CreatureMovement occupant) && occupant == requester)
        {
            _occupants.Remove(cell);
            OccupancyVersion++;
        }
    }

    /// <summary>같은 빈 칸으로 동시에 이동할 때 몬스터가 플레이어에게 양보할지 확인한다.</summary>
    /// <param name="requester">현재 목적지 충돌을 검사하는 이동 주체다.</param>
    /// <param name="target">아직 도착 선점하지 않은 이동 목적지다.</param>
    public bool ShouldYieldDestination(CreatureMovement requester, Vector2Int target)
    {
        // entry는 현재 도착 선점을 등록한 칸과 이동 주체의 쌍이다.
        foreach (KeyValuePair<Vector2Int, CreatureMovement> entry in _occupants)
        {
            // 아직 목적지에 도착하지 않은 다른 이동 주체다.
            CreatureMovement other = entry.Value;
            if (other == null || !other.CanOccupyCell || other == requester || !other.IsMoving ||
                other.IsReturning || other.TargetCell != target)
                continue;
            if (other.IsPlayerControlled != requester.IsPlayerControlled)
                return other.IsPlayerControlled;
            if (other.GetInstanceID() < requester.GetInstanceID())
                return true;
        }
        return false;
    }

    /// <summary>박스 등이 차지하는 정적 차단 칸을 갱신하고 몬스터의 이전 경로 판단을 무효화한다.</summary>
    /// <param name="cell">벽으로 추가하거나 해제할 타일 좌표다.</param>
    /// <param name="blocked">참이면 차단 칸으로 등록하고 거짓이면 해제한다.</param>
    public void SetCellBlocked(Vector2Int cell, bool blocked)
    {
        // 같은 등록을 반복할 때는 경로 기록을 불필요하게 초기화하지 않는다.
        bool changed = blocked ? _blockedCells.Add(cell) : _blockedCells.Remove(cell);
        if (changed)
            TraversalVersion++;
    }

    /// <summary>
    /// 맵 데이터(JSON)를 읽고, mapPrefabKey가 있으면 Stage Prefab도 스폰한다.
    /// 실패하면 아무것도 남기지 않고 false를 돌려준다 — 이전 맵의 데이터로 진행되는 일을 막는다.
    /// </summary>
    /// <param name="mapPrefabKey">null이면 프리팹을 스폰하지 않는다 (맵이 Scene에 직접 놓여 있는 기존 방식).</param>
    public bool LoadMap(string mapDataKey, string mapPrefabKey = null)
    {
        UnloadMap(); // 이전 맵을 먼저 비운다 — 아래에서 실패해도 이전 데이터가 남지 않는다

        // 맵 격자와 스폰 정보가 직렬화된 원본 텍스트 에셋이다.
        TextAsset json = Managers.Resource.Load<TextAsset>(mapDataKey);
        if (json == null)
        {
            LogPrinter.LogError($"[MapManager] MapData 로드 실패: {mapDataKey}. Addressable 등록 확인.");
            return false;
        }

        // 원본 텍스트에서 복원하여 형식을 확인할 맵 데이터다.
        Data.MapData map = JsonConvert.DeserializeObject<Data.MapData>(json.text);
        if (IsValidMapData(map, mapDataKey) == false)
            return false;

        // 데이터를 확인한 뒤에 프리팹을 스폰한다 — 데이터가 깨졌는데 맵만 보이는 상태를 만들지 않는다
        if (string.IsNullOrEmpty(mapPrefabKey) == false && _view.Spawn(mapPrefabKey) == false)
            return false;

        CurrentMap = map;
        _grid = map.Grid;

        _playerSpawnPoint = new ETileType[_grid.Width, _grid.Height];

        // CollisionRows >> 2D 배열 (로드 시 1회 파싱, 이후 질의는 배열만)
        //_collision = new ECellCollisionType[_grid.Width, _grid.Height];

        for (int y = 0; y < _grid.Height; y++)
        {
            // 현재 세로 타일 번호에 대응하는 벽과 스폰 표시 문자열이다.
            string row = map.CollisionRows[y];
            for (int x = 0; x < _grid.Width; x++)
            {
                //_collision[x, z] = CharToCollision(row[x]);
                _playerSpawnPoint[x, y] = row[x] == MAP_PLAYER_SPAWNPOINT ? ETileType.SpawnPoint : ETileType.None; // 'S' 리터럴 → Exporter와 같은 상수
            }
        }

        //Occupancy.Clear();

        LogPrinter.Log($"<color=Cyan>[MapManager] '{map.Name}' 로드 — {_grid.Width}x{_grid.Height}</color>");
        return true;
    }

    /// <summary>행 개수와 행 길이가 Grid와 맞는지 확인. 손으로 고친 JSON이나 형식이 다른 파일 방어.</summary>
    private static bool IsValidMapData(Data.MapData map, string mapDataKey)
    {
        if (map == null || map.CollisionRows == null || map.CollisionRows.Count != map.Grid.Height)
        {
            LogPrinter.LogError($"[MapManager] MapData 형식 오류: {mapDataKey} — CollisionRows 개수가 Grid.Height와 다름. 재Export 필요");
            return false;
        }

        for (int y = 0; y < map.CollisionRows.Count; y++)
        {
            // 격자의 가로 타일 수와 길이가 같은지 검증할 행 문자열이다.
            string row = map.CollisionRows[y];
            if (row == null || row.Length != map.Grid.Width)
            {
                LogPrinter.LogError($"[MapManager] MapData 형식 오류: {mapDataKey} — CollisionRows[{y}] 길이가 Grid.Width와 다름. 재Export 필요");
                return false;
            }
        }
        return true;
    }

    #region Coordinate — 월드 to 셀 변환
    /// <summary>월드 좌표 to 셀. Origin/CellSize 반영.</summary>
    public Vector2Int WorldToCell(Vector3 worldPos)
    {
        return GridUtil.WorldToCell(in _grid, worldPos);
    }

    /// <summary>셀 to 셀 중심 월드 좌표. Gizmo/이동 목적지 계산용.</summary>
    public Vector2 CellCenterToWorld(Vector2Int cell)
    {
        return GridUtil.CellCenterToWorld(in _grid, cell);
    }
    #endregion

    public void UnloadMap()
    {
        _view.Despawn();

        CurrentMap = null;
        _grid = default;
        _playerSpawnPoint = null;
        _area = null;
        _occupants.Clear();
        _blockedCells.Clear();
        TraversalVersion++;
        OccupancyVersion++;
        /*_collision = null;
        _clearanceCaches.Clear();   // 맵 교체 시 캐시 폐기 — 배열 크기가 달라질 수 있음
        _occupancy.Clear();
        _collisionVersion = 0;*/
    }

    /// <summary>
    /// 라운드 전환의 유일한 경로 — 보이는 것(라운드 루트)과 영역 데이터를 함께 바꾼다.
    /// 둘을 따로 바꿀 수 있게 열어 두면 한쪽만 바뀐 상태가 생긴다.
    /// </summary>
    public void SetRound(ERound round, TerrainArea area)
    {
        _view.ShowRound(round);
        _area = area;
        TraversalVersion++;
    }

    /// <summary>
    /// 셀이 현재 라운드의 영역 밖인가.
    /// 영역이 정해지지 않았으면(라운드 시작 전, 기존 방식의 맵) 제한이 없으므로 false.
    /// </summary>
    public bool IsOutsideArea(Vector2Int cell)
    {
        return _area != null && _area.Contains(cell) == false;
    }
}
