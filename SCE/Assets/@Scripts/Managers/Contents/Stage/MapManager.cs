using Data;
using Newtonsoft.Json;
using UnityEngine;
using static Define;

public class MapManager
{
    public Data.MapData CurrentMap { get; private set; }
    public bool IsLoaded { get { return CurrentMap != null; } }

    public ref readonly GridSpec Grid { get { return ref _grid; } }
    private GridSpec _grid;
    
    

    private ETileType[,] _playerSpawnPoint;

    public void LoadMap(string mapName)
    {
        TextAsset json = Managers.Resource.Load<TextAsset>(mapName);
        if (json == null)
        {
            LogPrinter.LogError($"[MapManager] MapData 로드 실패: {mapName}. Addressable 등록 확인.");
            return;
        }

        CurrentMap = JsonConvert.DeserializeObject<Data.MapData>(json.text);
        _grid = CurrentMap.Grid;

        _playerSpawnPoint = new ETileType[_grid.Width, _grid.Height];

        // CollisionRows >> 2D 배열 (로드 시 1회 파싱, 이후 질의는 배열만)
        //_collision = new ECellCollisionType[_grid.Width, _grid.Height];

        for (int y = 0; y < _grid.Height; y++)
        {
            string row = CurrentMap.CollisionRows[y];
            for (int x = 0; x < _grid.Width; x++)
            {
                //_collision[x, z] = CharToCollision(row[x]);
                _playerSpawnPoint[x, y] = row[x] == 'S' ? ETileType.SpawnPoint : ETileType.None;
            }
        }

        //Occupancy.Clear();

        LogPrinter.Log($"<color=Cyan>[MapManager] '{CurrentMap.Name}' 로드 — {_grid.Width}x{_grid.Height}</color>");
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
        CurrentMap = null;
        /*_collision = null;
        _clearanceCaches.Clear();   // 맵 교체 시 캐시 폐기 — 배열 크기가 달라질 수 있음
        _occupancy.Clear();
        _collisionVersion = 0;*/
    }
}
