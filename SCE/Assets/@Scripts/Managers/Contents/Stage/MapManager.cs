using Data;
using Newtonsoft.Json;
using UnityEngine;
using static Define;

public class MapManager
{
    public Data.MapData CurrentMap { get; private set; }
    public bool IsLoaded { get { return CurrentMap != null; } }

    private readonly MapView _view = new MapView();

    public ref readonly GridSpec Grid { get { return ref _grid; } }
    private GridSpec _grid;
    
    

    private ETileType[,] _playerSpawnPoint;

    /// <summary>
    /// 맵 데이터(JSON)를 읽고, mapPrefabKey가 있으면 Stage Prefab도 스폰한다.
    /// 실패하면 아무것도 남기지 않고 false를 돌려준다 — 이전 맵의 데이터로 진행되는 일을 막는다.
    /// </summary>
    /// <param name="mapPrefabKey">null이면 프리팹을 스폰하지 않는다 (맵이 Scene에 직접 놓여 있는 기존 방식).</param>
    public bool LoadMap(string mapDataKey, string mapPrefabKey = null)
    {
        UnloadMap(); // 이전 맵을 먼저 비운다 — 아래에서 실패해도 이전 데이터가 남지 않는다

        TextAsset json = Managers.Resource.Load<TextAsset>(mapDataKey);
        if (json == null)
        {
            LogPrinter.LogError($"[MapManager] MapData 로드 실패: {mapDataKey}. Addressable 등록 확인.");
            return false;
        }

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
        /*_collision = null;
        _clearanceCaches.Clear();   // 맵 교체 시 캐시 폐기 — 배열 크기가 달라질 수 있음
        _occupancy.Clear();
        _collisionVersion = 0;*/
    }

    /// <summary>지정한 라운드의 루트만 보이게 한다. 라운드를 시작하는 쪽(StageManager)이 호출.</summary>
    public void ShowRound(ERound round)
    {
        _view.ShowRound(round);
    }
}
