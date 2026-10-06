#if UNITY_EDITOR
using Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using static Define;

/// <summary>
/// 선택한 맵 루트(Grid)의 Tilemap을 MapData JSON으로 Export.
///
/// 규약:
/// - Tilemap_Collision의 타일 점유 범위가 전장 크기의 정의
/// - 모든 셀 좌표는 collision bounds 기준 0-base로 재매핑, 행 기록은 y 오름차순
/// - 맵 루트의 직계 자식 중 StageAuthoring이 붙은 오브젝트 = 스테이지 (이름 무관)
/// - 스테이지의 Tilemap_Terrain에 칠한 셀 = 그 스테이지의 영역
/// - 에러가 1건이라도 있으면 JSON을 기록하지 않는다. Export는 씬을 수정하지 않는다
/// </summary>
public static class MapExporter
{
    private const string OUTPUT_DIR = "Assets/@Resources/Data/JsonData/MapData";

    private const string COLLISION_TILEMAP = "Tilemap_Collision";
    private const string OBJECT_STAGE = "Tilemap_Stage_Object";
    private const string OBJECT_MAP = "Tilemap_Map_Object";
    private const string TERRAIN_TILEMAP = "Tilemap_Terrain";
    private const string WAVE_TILEMAP_PREFIX = "Tilemap_Wave_";
    private const string LEGACY_STAGE_PREFIX = "Stage_"; // 마이그레이션 누락 탐지용으로만 사용

    public static string GetExportPath(string mapName)
    {
        return Path.Combine(OUTPUT_DIR, $"{mapName}.json").Replace('\\', '/');
    }

    /// <summary>Export 1회의 에러/경고 집계.</summary>
    private sealed class ExportReport
    {
        private readonly string _mapName;
        private readonly bool _silent; // 미리보기용 — 집계만 하고 콘솔에 쓰지 않는다
        public int ErrorCount { get; private set; }
        public int WarningCount { get; private set; }

        public ExportReport(string mapName, bool silent = false)
        {
            _mapName = mapName;
            _silent = silent;
        }

        public void Error(string message)
        {
            ErrorCount++;
            if (_silent == false) Debug.LogError($"[MapExport] {_mapName}: {message}");
        }

        public void Warning(string message)
        {
            WarningCount++;
            if (_silent == false) Debug.LogWarning($"[MapExport] {_mapName}: {message}");
        }
    }

    [MenuItem("Tools/Export Map %#m")]
    private static void ExportSelectedMaps()
    {
        foreach (GameObject go in Selection.gameObjects)
            ExportMap(go);

        AssetDatabase.Refresh();
    }

    private static void ExportMap(GameObject mapRoot)
    {
        var report = new ExportReport(mapRoot.name);

        var grid = mapRoot.GetComponent<Grid>();
        if (grid == null)
        {
            report.Error("Grid 컴포넌트 없음. 맵 루트를 선택하세요.");
            return;
        }

        if (grid.cellSwizzle != GridLayout.CellSwizzle.XYZ)
            report.Warning($"Cell Swizzle이 XYZ가 아님 ({grid.cellSwizzle}). 좌표 규약 불일치 위험.");
        if (Mathf.Approximately(grid.cellSize.x, grid.cellSize.y) == false)
            report.Warning($"비정방 셀 ({grid.cellSize}). CellSize는 x 기준으로 기록됨.");

        var collisionTm = Util.FindChild<Tilemap>(mapRoot, COLLISION_TILEMAP, true);
        if (collisionTm == null)
        {
            report.Error($"{COLLISION_TILEMAP} 없음.");
            return;
        }

        // 지웠다 그린 타일의 잔여 bounds 제외 — 씬을 수정하지 않는 점유 범위 계산 (미리보기와 같은 경로)
        if (TryGetOccupiedBounds(collisionTm, out BoundsInt bounds) == false)
        {
            report.Error($"{COLLISION_TILEMAP}에 타일이 없음 — 전장 크기 정의 불가");
            return;
        }

        Vector3 originWorld = grid.CellToWorld(new Vector3Int(bounds.xMin, bounds.yMin, 0));
        var spec = new GridSpec
        {
            OriginX = originWorld.x,
            OriginY = originWorld.y,
            CellSize = grid.cellSize.x,
            Width = bounds.size.x,
            Height = bounds.size.y,
        };

        var rows = new List<string>(spec.Height);
        var sb = new StringBuilder(spec.Width);
        for (int y = bounds.yMin; y < bounds.yMax; y++)
        {
            sb.Clear();
            for (int x = bounds.xMin; x < bounds.xMax; x++)
                sb.Append(GetCollisionChar(collisionTm.GetTile(new Vector3Int(x, y, 0)) as CustomTile));
            rows.Add(sb.ToString());
        }

        List<SpawnData> mapObjects = CollectMapObjects(mapRoot, bounds, rows, report);

        SortedList<int, StageAuthoring> stageRoots = CollectStageRoots(mapRoot, report);
        int[] regionCells = BuildStageRegion(stageRoots, grid, bounds, spec, report, overlapCells: null);
        List<StageData> stages = ExportStages(stageRoots, bounds, rows, report);
        ValidateSpawnsInRegion(stages, stageRoots, regionCells, spec.Width, report);
        WarnAdjacentStages(stageRoots, regionCells, spec.Width, spec.Height, report);

        // 범례는 에러 판정 전에 — 기호 초과도 에러로 집계되어야 하므로
        var legend = new Dictionary<string, int>();
        if (StageRegionCodec.TryBuildLegend(stageRoots.Keys, legend, out string legendError) == false)
            report.Error(legendError);

        if (report.ErrorCount > 0)
        {
            Debug.LogError($"[MapExport] {mapRoot.name}: 에러 {report.ErrorCount}건 — JSON을 기록하지 않았습니다 (기존 파일 유지).");
            return;
        }

        var regionRows = new List<string>(spec.Height);
        StageRegionCodec.Encode(regionCells, spec.Width, spec.Height, legend, regionRows);

        var mapData = new MapData
        {
            Name = mapRoot.name,
            Grid = spec,
            CollisionRows = rows,
            Stages = stages,
            MapObjects = mapObjects,
            StageRegionRows = regionRows,
            StageRegionLegend = legend,
        };

        Directory.CreateDirectory(OUTPUT_DIR);
        string path = GetExportPath(mapData.Name);
        string json = JsonConvert.SerializeObject(mapData, Formatting.Indented, new StringEnumConverter());
        File.WriteAllText(path, json);

        LogPrinter.Log($"<color=Cyan>[MapExport] {mapData.Name}: {spec.Width}x{spec.Height}, Stages={stages.Count}, " +
            $"MapObjects={mapObjects.Count}, Warnings={report.WarningCount}, Size={json.Length / 1024f:F1}KB >> {path}</color>");
    }

    /// <summary>CompressBounds와 같은 결과를 Tilemap을 수정하지 않고 계산한다.</summary>
    private static bool TryGetOccupiedBounds(Tilemap tm, out BoundsInt bounds)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

        foreach (Vector3Int pos in tm.cellBounds.allPositionsWithin)
        {
            if (tm.HasTile(pos) == false)
                continue;
            if (pos.x < minX) minX = pos.x;
            if (pos.y < minY) minY = pos.y;
            if (pos.x > maxX) maxX = pos.x;
            if (pos.y > maxY) maxY = pos.y;
        }

        if (minX == int.MaxValue)
        {
            bounds = default;
            return false;
        }

        bounds = new BoundsInt(minX, minY, 0, maxX - minX + 1, maxY - minY + 1, 1);
        return true;
    }

    /// <summary>충돌 문자 규약 — 타일 타입 → 문자 변환은 이 함수로 격리.</summary>
    private static char GetCollisionChar(CustomTile tile)
    {
        if (tile == null)
            return MAP_TOOL_NONE;

        switch (tile.TileType)
        {
            case ETileType.SpawnPoint:
                return MAP_PLAYER_SPAWNPOINT;
            case ETileType.Wall:
                return MAP_TOOL_WALL;
        }

        return MAP_TOOL_NONE;
    }

    /// <summary>맵 루트 Tilemap_Object = 맵 전역 오브젝트. 스테이지와 무관하게 맵 수명 동안 존속.</summary>
    private static List<SpawnData> CollectMapObjects(GameObject mapRoot, BoundsInt bounds, List<string> rows, ExportReport report)
    {
        var result = new List<SpawnData>();

        // 맵 루트의 직계 자식만 — 스테이지 아래의 Tilemap_Object를 잘못 집지 않도록
        var objectTm = Util.FindChild<Tilemap>(mapRoot, OBJECT_MAP, false);
        if (objectTm == null)
            return result;

        foreach (Vector3Int pos in objectTm.cellBounds.allPositionsWithin)
        {
            if (objectTm.GetTile(pos) is CustomTile tile == false)
                continue;

            int cx = pos.x - bounds.xMin;
            int cy = pos.y - bounds.yMin;
            if ((uint)cx >= (uint)bounds.size.x || (uint)cy >= (uint)bounds.size.y)
            {
                report.Warning($"맵 오브젝트 '{tile.DisplayName}'(DataId={tile.DataId})가 전장 밖 ({cx},{cy}) — 제외");
                continue;
            }

            if (tile.TileType == ETileType.SpawnPoint)
            {
                report.Warning($"맵 루트 {OBJECT_MAP}에 SpawnPoint ({cx},{cy}) — SpawnPoint는 스테이지 소속이어야 함, 무시");
                continue;
            }

            if (tile.ObjectType != EObjectType.Monster && tile.ObjectType != EObjectType.NPC && tile.ObjectType != EObjectType.Villager)
                continue;

            if (rows[cy][cx] == MAP_TOOL_WALL)
                report.Warning($"맵 오브젝트 셀({cx},{cy})이 Wall — DataId={tile.DataId}, 런타임 스폰 실패 예정");

            result.Add(new SpawnData { ObjectType = tile.ObjectType, DataId = tile.DataId, CellX = cx, CellY = cy });
        }
        return result;
    }

    #region Stage
    /// <summary>에러·경고 메시지용 스테이지 표기. Key만으로는 기획자가 오브젝트를 찾기 어렵다.</summary>
    private static string StageLabel(SortedList<int, StageAuthoring> stageRoots, int key)
    {
        return stageRoots.TryGetValue(key, out StageAuthoring authoring) ? $"'{authoring.name}'(Key {key})" : $"Key {key}";
    }

    /// <summary>맵 루트의 직계 자식 중 StageAuthoring이 붙은 오브젝트를 수집. Key 중복·음수는 에러.</summary>
    private static SortedList<int, StageAuthoring> CollectStageRoots(GameObject mapRoot, ExportReport report)
    {
        var stageRoots = new SortedList<int, StageAuthoring>();
        foreach (Transform child in mapRoot.transform)
        {
            var authoring = child.GetComponent<StageAuthoring>();
            if (authoring == null)
            {
                // 이름 규약 시절의 오브젝트가 마이그레이션되지 않은 경우
                if (child.name.StartsWith(LEGACY_STAGE_PREFIX))
                    report.Warning($"'{child.name}' — {nameof(StageAuthoring)} 없음, 스테이지에서 제외. 'Tools/Map/Migrate Stage Names To StageAuthoring' 실행 필요");
                continue;
            }

            int key = authoring.StageKey;
            if (key < 0)
            {
                report.Error($"'{child.name}' — Stage Key는 0 이상이어야 함 ({STAGE_REGION_NONE}은 통로 예약값)");
                continue;
            }
            if (stageRoots.TryGetValue(key, out StageAuthoring existing))
            {
                report.Error($"'{child.name}' — Stage Key {key} 중복 ('{existing.name}'와 충돌). 컴포넌트 메뉴의 '다음 빈 Key 할당' 사용");
                continue;
            }
            stageRoots.Add(key, authoring);
        }
        return stageRoots;
    }

    /// <summary>
    /// 각 스테이지의 Tilemap_Terrain에 칠한 셀을 전장 크기 그리드에 Key로 기록.
    /// </summary>
    /// <param name="overlapCells">null이 아니면 겹친 셀을 모은다 — 미리보기 표시용</param>
    private static int[] BuildStageRegion(SortedList<int, StageAuthoring> stageRoots, Grid grid, BoundsInt cb, GridSpec spec,
        ExportReport report, List<Vector2Int> overlapCells)
    {
        int width = spec.Width;
        int height = spec.Height;
        var cells = new int[width * height];
        System.Array.Fill(cells, STAGE_REGION_NONE);

        var overlaps = new Dictionary<(int First, int Second), int>();

        foreach (var pair in stageRoots)
        {
            int key = pair.Key;
            GameObject root = pair.Value.gameObject;

            var terrain = Util.FindChild<Tilemap>(root, TERRAIN_TILEMAP, recursive: true);
            if (terrain == null)
            {
                report.Warning($"'{root.name}': {TERRAIN_TILEMAP} 없음 — 영역 없음 (이동으로 진입 불가)");
                continue;
            }
            if (terrain.layoutGrid != grid)
            {
                report.Error($"'{root.name}': {TERRAIN_TILEMAP}이 맵 루트 Grid 소속이 아님 — 셀 좌표 불일치");
                continue;
            }

            int painted = 0;
            int outside = 0;

            foreach (Vector3Int pos in terrain.cellBounds.allPositionsWithin)
            {
                if (terrain.HasTile(pos) == false)
                    continue;

                int cx = pos.x - cb.xMin;
                int cy = pos.y - cb.yMin;
                if ((uint)cx >= (uint)width || (uint)cy >= (uint)height)
                {
                    outside++;
                    continue;
                }

                int index = cy * width + cx;
                int existing = cells[index];
                if (existing != STAGE_REGION_NONE)
                {
                    var overlapKey = (existing, key);
                    overlaps.TryGetValue(overlapKey, out int overlapCount);
                    overlaps[overlapKey] = overlapCount + 1;
                    overlapCells?.Add(new Vector2Int(cx, cy));
                    continue;
                }

                cells[index] = key;
                painted++;
            }

            if (outside > 0)
                report.Warning($"'{root.name}': Terrain 셀 {outside}개가 전장(Collision bounds) 밖 — 영역에서 제외");
            if (painted == 0)
                report.Warning($"'{root.name}': 영역 셀 0개 — 이동으로 진입 불가");
        }

        foreach (var entry in overlaps)
            report.Error($"영역 겹침 — {StageLabel(stageRoots, entry.Key.First)} ↔ {StageLabel(stageRoots, entry.Key.Second)} ({entry.Value}셀). 한 셀은 한 스테이지에만 칠할 것");

        return cells;
    }

    /// <summary>스테이지 계층을 StageData로 export. 영역 판정은 BuildStageRegion 담당.</summary>
    private static List<StageData> ExportStages(SortedList<int, StageAuthoring> stageRoots, BoundsInt cb, List<string> rows, ExportReport report)
    {
        var stages = new List<StageData>(stageRoots.Count);
        foreach (var pair in stageRoots)
        {
            StageAuthoring authoring = pair.Value;
            Transform root = authoring.transform;

            var stage = new StageData
            {
                Key = pair.Key,
                Settings = authoring.Settings, // 저작 데이터 그대로 — 직렬화 직후 버려지므로 복사하지 않는다
            };

            var objectTm = root.Find(OBJECT_STAGE)?.GetComponent<Tilemap>();
            if (objectTm != null)
                ParseSpawnTilemap(objectTm, cb, rows, stage, waveIndex: -1, root.name, report);
            if (stage.HasPlayerSpawn == false)
                report.Warning($"'{root.name}': SpawnPoint 타일 없음 — 재시작 checkpoint 불가");

            var waveTms = new SortedList<int, Tilemap>();
            foreach (Transform child in root)
            {
                if (child.name.StartsWith(WAVE_TILEMAP_PREFIX) == false) continue;
                if (int.TryParse(child.name.Substring(WAVE_TILEMAP_PREFIX.Length), out int w) == false) continue;
                if (waveTms.ContainsKey(w))
                {
                    report.Error($"'{root.name}': '{child.name}' 웨이브 번호 {w} 중복");
                    continue;
                }
                var tm = child.GetComponent<Tilemap>();
                if (tm != null) waveTms.Add(w, tm);
            }
            foreach (var wavePair in waveTms)
            {
                stage.Waves.Add(new List<SpawnData>());
                ParseSpawnTilemap(wavePair.Value, cb, rows, stage, waveIndex: stage.Waves.Count - 1, root.name, report);
            }

            // 구역 설정과 배치의 불일치 — 동작은 하지만 의도와 다를 가능성이 높다
            if (stage.Settings.ZoneType == EZoneType.Village && stage.Waves.Count > 0)
                report.Warning($"'{root.name}': 마을 구역에 웨이브 {stage.Waves.Count}개 — 의도 확인");

            stages.Add(stage);
        }
        return stages;
    }

    private static void ParseSpawnTilemap(Tilemap tm, BoundsInt cb, List<string> rows,
        StageData stage, int waveIndex, string stageName, ExportReport report)
    {
        foreach (var pos in tm.cellBounds.allPositionsWithin)
        {
            if (tm.GetTile(pos) is CustomTile tile == false) continue;

            int cx = pos.x - cb.xMin; // 0-base 재매핑 — 좌표계 경계의 유일 지점
            int cy = pos.y - cb.yMin;
            if ((uint)cx >= (uint)cb.size.x || (uint)cy >= (uint)cb.size.y)
            {
                report.Warning($"'{stageName}': 타일이 전장 밖 ({cx},{cy}) — 제외");
                continue;
            }

            if (tile.TileType == ETileType.SpawnPoint)
            {
                if (stage.HasPlayerSpawn)
                    report.Warning($"'{stageName}': SpawnPoint 중복 — 마지막 사용");

                stage.HasPlayerSpawn = true;
                stage.PlayerSpawnInfo = new SpawnInfo(
                    type: tile.ObjectType,
                    dataId: tile.DataId,
                    cell: new Vector2Int(cx, cy)
                    );

                continue;
            }

            if (tile.ObjectType == EObjectType.Villager || tile.ObjectType == EObjectType.Monster || tile.ObjectType == EObjectType.NPC)
            {
                if (waveIndex >= 0 && tile.ObjectType != EObjectType.Monster)
                {
                    report.Error($"'{stageName}': 웨이브 타일맵에 {tile.ObjectType}(DataId={tile.DataId}) ({cx},{cy}) — 웨이브에는 Monster만. NPC는 {OBJECT_STAGE}에 배치");
                    continue;
                }

                if (rows[cy][cx] == MAP_TOOL_WALL)
                    report.Warning($"'{stageName}': 스폰 셀({cx},{cy})이 Wall — DataId={tile.DataId}, 런타임 스폰 실패 예정. 배치 확인.");

                var spawn = new SpawnData
                { ObjectType = tile.ObjectType, DataId = tile.DataId, CellX = cx, CellY = cy };

                if (waveIndex >= 0)
                    stage.Waves[waveIndex].Add(spawn);
                else
                    stage.AlwaysSpawn.Add(spawn);
            }
        }
    }

    /// <summary>모든 스폰 셀이 자기 스테이지 영역 안인지 검증.</summary>
    private static void ValidateSpawnsInRegion(List<StageData> stages, SortedList<int, StageAuthoring> stageRoots,
        int[] cells, int width, ExportReport report)
    {
        for (int s = 0; s < stages.Count; s++)
        {
            StageData stage = stages[s];

            

            if (stage.HasPlayerSpawn)
                CheckSpawnOwner(stage, stageRoots, stage.PlayerSpawnInfo.Cell.x, stage.PlayerSpawnInfo.Cell.y, "SpawnPoint", cells, width, report);

            for (int i = 0; i < stage.AlwaysSpawn.Count; i++)
            {
                SpawnData spawn = stage.AlwaysSpawn[i];
                CheckSpawnOwner(stage, stageRoots, spawn.CellX, spawn.CellY, $"AlwaysSpawn(DataId={spawn.DataId})", cells, width, report);
            }

            for (int w = 0; w < stage.Waves.Count; w++)
            {
                List<SpawnData> wave = stage.Waves[w];
                for (int i = 0; i < wave.Count; i++)
                    CheckSpawnOwner(stage, stageRoots, wave[i].CellX, wave[i].CellY, $"Waves[{w}](DataId={wave[i].DataId})", cells, width, report);
            }
        }
    }

    private static void CheckSpawnOwner(StageData stage, SortedList<int, StageAuthoring> stageRoots,
        int x, int y, string label, int[] cells, int width, ExportReport report)
    {
        int owner = cells[y * width + x]; // ParseSpawnTilemap에서 전장 밖 셀은 이미 제외
        if (owner == stage.Key)
            return;

        string where = owner == STAGE_REGION_NONE ? "통로(어느 스테이지도 아님)" : $"{StageLabel(stageRoots, owner)} 영역";
        report.Error($"{StageLabel(stageRoots, stage.Key)}: {label} 셀({x},{y})이 자기 영역 밖 — {where}");
    }

    /// <summary>서로 다른 스테이지 셀이 8방향으로 맞닿으면 경고.</summary>
    private static void WarnAdjacentStages(SortedList<int, StageAuthoring> stageRoots, int[] cells, int width, int height, ExportReport report)
    {
        var pairs = new HashSet<(int Low, int High)>();

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                int a = cells[row + x];
                if (a == STAGE_REGION_NONE)
                    continue;

                if (x + 1 < width)
                    AddAdjacentPair(pairs, a, cells[row + x + 1]);
                if (y + 1 < height)
                {
                    int up = row + width + x;
                    AddAdjacentPair(pairs, a, cells[up]);
                    if (x + 1 < width) AddAdjacentPair(pairs, a, cells[up + 1]);
                    if (x - 1 >= 0) AddAdjacentPair(pairs, a, cells[up - 1]);
                }
            }
        }

        foreach (var pair in pairs)
            report.Warning($"{StageLabel(stageRoots, pair.Low)} ↔ {StageLabel(stageRoots, pair.High)} 영역이 맞닿아 있음 — 경계를 오가면 Unload/Load 반복. 사이에 통로 1칸 이상 권장");
    }

    private static void AddAdjacentPair(HashSet<(int Low, int High)> pairs, int a, int b)
    {
        if (b == STAGE_REGION_NONE || b == a)
            return;
        pairs.Add(a < b ? (a, b) : (b, a));
    }
    #endregion

    /// <summary>
    /// Scene의 현재 상태로 영역을 계산한다. 파일을 쓰지 않고, Tilemap을 수정하지 않고, 로그를 남기지 않는다.
    /// 검사 범위는 영역 수준(Key 중복·Grid 소속·겹침)까지.
    /// </summary>
    public static bool TryBuildLivePreview(GameObject mapRoot, StageRegionPreviewData into)
    {
        into.Clear();
        var report = new ExportReport(mapRoot.name, silent: true);

        var grid = mapRoot.GetComponent<Grid>();
        if (grid == null)
        {
            into.Status = "Grid 컴포넌트 없음 — 맵 루트를 지정하세요";
            return false;
        }

        var collisionTm = Util.FindChild<Tilemap>(mapRoot, COLLISION_TILEMAP, true);
        if (collisionTm == null || TryGetOccupiedBounds(collisionTm, out BoundsInt bounds) == false)
        {
            into.Status = $"{COLLISION_TILEMAP} 없음 또는 타일 없음";
            return false;
        }

        Vector3 originWorld = grid.CellToWorld(new Vector3Int(bounds.xMin, bounds.yMin, 0));
        var spec = new GridSpec
        {
            OriginX = originWorld.x,
            OriginY = originWorld.y,
            CellSize = grid.cellSize.x,
            Width = bounds.size.x,
            Height = bounds.size.y,
        };

        SortedList<int, StageAuthoring> stageRoots = CollectStageRoots(mapRoot, report);
        into.Spec = spec;
        into.PlaneZ = grid.transform.position.z;
        into.Cells = BuildStageRegion(stageRoots, grid, bounds, spec, report, into.OverlapCells);
        into.ErrorCount = report.ErrorCount;
        into.WarningCount = report.WarningCount;
        into.Status = $"Scene 기준 — Stage {stageRoots.Count}개, 영역 에러 {report.ErrorCount}건, 경고 {report.WarningCount}건 (스폰 위치 검증은 Export 시)";
        return true;
    }
}
#endif