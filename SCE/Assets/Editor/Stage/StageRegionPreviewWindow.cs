using System;
using System.Collections.Generic;
using System.IO;
using Data;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;
using static Define;

/// <summary>
/// 스테이지 영역을 Scene 뷰에 덧칠해 보여준다. Tools > Stage Region Preview. 도킹 가능.
/// 원본: Scene(칠하는 중 자동 갱신) / Export 파일(런타임이 보게 될 영역).
/// 그리기 형태는 갱신 시점에 1회 계산하고, Repaint에서는 그리기만 한다.
/// </summary>
public sealed class StageRegionPreviewWindow : EditorWindow
{
    private enum ESource
    {
        Scene,        // 현재 칠해진 Terrain
        ExportedJson, // 저장된 JSON (런타임 기준)
    }

    [SerializeField] private GameObject _mapRoot;
    [SerializeField] private ESource _source = ESource.Scene;
    [SerializeField] private bool _visible = true;
    [SerializeField] private bool _autoRefresh = true;
    [SerializeField] private float _fillAlpha = 0.25f;

    private readonly StageRegionPreviewData _data = new();

    // 그리기 형태 캐시 — Refresh에서만 갱신
    private readonly List<Vector3> _runVerts = new();              // 행 단위 연속 구간, 사각형당 4개
    private readonly List<int> _runKeys = new();
    private readonly Dictionary<int, Vector3[]> _outlineByKey = new();  // 경계선 선분 쌍
    private readonly List<(int Key, Vector3 Center, string Text)> _labels = new();
    private Vector3[] _overlapVerts = Array.Empty<Vector3>();
    private readonly Vector3[] _quad = new Vector3[4];              // 그리기용 재사용 버퍼

    private bool _refreshQueued;
    private GUIStyle _labelStyle;

    [MenuItem("Tools/Stage Region Preview")]
    public static void Open()
    {
        GetWindow<StageRegionPreviewWindow>("Stage Region");
    }

    #region 생명주기
    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        Undo.undoRedoPerformed += OnSceneChanged;
        EditorApplication.hierarchyChanged += OnSceneChanged;
        Tilemap.tilemapTileChanged += OnTilemapTileChanged;
        QueueRefresh();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        Undo.undoRedoPerformed -= OnSceneChanged;
        EditorApplication.hierarchyChanged -= OnSceneChanged;
        Tilemap.tilemapTileChanged -= OnTilemapTileChanged;
        SceneView.RepaintAll(); // 덧칠 제거
    }
    #endregion

    #region 패널
    private void OnGUI()
    {
        bool refresh = false;

        EditorGUI.BeginChangeCheck();
        _mapRoot = (GameObject)EditorGUILayout.ObjectField("맵 루트", _mapRoot, typeof(GameObject), true);
        _source = (ESource)EditorGUILayout.EnumPopup(new GUIContent("원본", "Scene: 지금 칠한 상태 / ExportedJson: 저장된 파일 = 런타임 기준"), _source);
        refresh |= EditorGUI.EndChangeCheck();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("선택 오브젝트를 맵 루트로"))
            {
                _mapRoot = Selection.activeGameObject;
                refresh = true;
            }
            if (GUILayout.Button("새로고침"))
                refresh = true;
        }

        EditorGUI.BeginChangeCheck();
        _visible = EditorGUILayout.Toggle("Scene에 표시", _visible);
        _fillAlpha = EditorGUILayout.Slider("채우기 투명도", _fillAlpha, 0.05f, 0.8f);
        if (EditorGUI.EndChangeCheck())
            SceneView.RepaintAll();

        using (new EditorGUI.DisabledScope(_source != ESource.Scene))
            _autoRefresh = EditorGUILayout.Toggle(new GUIContent("자동 갱신", "Scene 원본일 때 타일을 칠하면 즉시 갱신"), _autoRefresh);

        if (refresh)
            Refresh();

        EditorGUILayout.Space();
        MessageType statusType = _data.ErrorCount > 0 ? MessageType.Error
            : _data.WarningCount > 0 ? MessageType.Warning : MessageType.Info;
        EditorGUILayout.HelpBox(string.IsNullOrEmpty(_data.Status) ? "맵 루트를 지정하세요" : _data.Status, statusType);

        if (_data.OverlapCells.Count > 0)
            EditorGUILayout.HelpBox($"겹친 셀 {_data.OverlapCells.Count}개 — Scene에 빨간색으로 표시됩니다", MessageType.Error);

        // 범례
        for (int i = 0; i < _labels.Count; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect swatch = GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14));
                EditorGUI.DrawRect(swatch, KeyColor(_labels[i].Key, 1f));
                EditorGUILayout.LabelField(_labels[i].Text);
            }
        }
    }
    #endregion

    #region 갱신
    private void OnTilemapTileChanged(Tilemap tilemap, Tilemap.SyncTile[] tiles)
    {
        if (_autoRefresh == false || _source != ESource.Scene || _mapRoot == null)
            return;
        if (tilemap.transform.IsChildOf(_mapRoot.transform))
            QueueRefresh();
    }

    private void OnSceneChanged()
    {
        if (_autoRefresh && _source == ESource.Scene)
            QueueRefresh();
    }

    /// <summary>한 프레임에 여러 번 요청돼도 1회만 갱신 (칠하기 중 연속 이벤트 대응)</summary>
    private void QueueRefresh()
    {
        if (_refreshQueued)
            return;
        _refreshQueued = true;
        EditorApplication.delayCall += Refresh;
    }

    private void Refresh()
    {
        _refreshQueued = false;
        if (this == null) // 창이 닫힌 뒤 지연 호출이 도착한 경우
            return;

        if (_mapRoot == null)
        {
            _data.Clear();
            _data.Status = "맵 루트를 지정하세요";
        }
        else if (_source == ESource.Scene)
        {
            MapExporter.TryBuildLivePreview(_mapRoot, _data);
        }
        else
        {
            TryLoadExported(_mapRoot, _data);
        }

        RebuildGeometry();
        Repaint();
        SceneView.RepaintAll();
    }

    /// <summary>런타임과 같은 디코드 경로로 Export 파일을 읽는다.</summary>
    private static bool TryLoadExported(GameObject mapRoot, StageRegionPreviewData into)
    {
        into.Clear();
        string path = MapExporter.GetExportPath(mapRoot.name);
        if (File.Exists(path) == false)
        {
            into.Status = $"Export 파일 없음: {path}";
            into.ErrorCount = 1;
            return false;
        }

        MapData map;
        try
        {
            map = JsonConvert.DeserializeObject<MapData>(File.ReadAllText(path));
        }
        catch (JsonException e)
        {
            into.Status = $"JSON 파싱 실패: {e.Message}";
            into.ErrorCount = 1;
            return false;
        }

        GridSpec spec = map.Grid;
        var cells = new int[spec.Width * spec.Height];
        if (StageRegionCodec.TryDecode(map.StageRegionRows, map.StageRegionLegend, spec.Width, spec.Height, cells, out string error) == false)
        {
            into.Status = $"영역 디코드 실패: {error}";
            into.ErrorCount = 1;
            return false;
        }

        into.Spec = spec;
        into.Cells = cells;
        into.PlaneZ = mapRoot.transform.position.z;
        into.Status = $"Export 파일 기준 (런타임과 동일) — {File.GetLastWriteTime(path):yyyy-MM-dd HH:mm:ss}";
        return true;
    }
    #endregion

    #region 형태 계산 (Refresh에서 1회)
    private void RebuildGeometry()
    {
        _runVerts.Clear();
        _runKeys.Clear();
        _outlineByKey.Clear();
        _labels.Clear();
        _overlapVerts = Array.Empty<Vector3>();

        if (_data.IsValid == false)
            return;

        GridSpec spec = _data.Spec;
        int width = spec.Width;
        int height = spec.Height;
        int[] cells = _data.Cells;

        var outlines = new Dictionary<int, List<Vector3>>();
        var sums = new Dictionary<int, (Vector3 Sum, int Count)>();

        for (int y = 0; y < height; y++)
        {
            int row = y * width;

            // 1. 같은 행의 연속 구간을 사각형 하나로
            int x = 0;
            while (x < width)
            {
                int key = cells[row + x];
                int start = x;
                while (x < width && cells[row + x] == key)
                    x++;

                if (key == STAGE_REGION_NONE)
                    continue;

                _runVerts.Add(Corner(spec, start, y));
                _runVerts.Add(Corner(spec, x, y));
                _runVerts.Add(Corner(spec, x, y + 1));
                _runVerts.Add(Corner(spec, start, y + 1));
                _runKeys.Add(key);

                // 라벨 위치 = 영역 셀 중심의 평균 (구간 중심 × 셀 수로 가중)
                int count = x - start;
                Vector3 runCenter = (Corner(spec, start, y) + Corner(spec, x, y + 1)) * 0.5f;
                sums.TryGetValue(key, out var acc);
                sums[key] = (acc.Sum + runCenter * count, acc.Count + count);
            }

            // 2. 이웃과 Key가 다른 변 = 경계선
            for (int cx = 0; cx < width; cx++)
            {
                int key = cells[row + cx];
                if (key == STAGE_REGION_NONE)
                    continue;

                if (outlines.TryGetValue(key, out List<Vector3> lines) == false)
                {
                    lines = new List<Vector3>();
                    outlines.Add(key, lines);
                }

                if (KeyAt(cells, width, height, cx - 1, y) != key) AddSegment(lines, Corner(spec, cx, y), Corner(spec, cx, y + 1));
                if (KeyAt(cells, width, height, cx + 1, y) != key) AddSegment(lines, Corner(spec, cx + 1, y), Corner(spec, cx + 1, y + 1));
                if (KeyAt(cells, width, height, cx, y - 1) != key) AddSegment(lines, Corner(spec, cx, y), Corner(spec, cx + 1, y));
                if (KeyAt(cells, width, height, cx, y + 1) != key) AddSegment(lines, Corner(spec, cx, y + 1), Corner(spec, cx + 1, y + 1));
            }
        }

        foreach (var entry in outlines)
            _outlineByKey.Add(entry.Key, entry.Value.ToArray());

        foreach (var entry in sums)
            _labels.Add((entry.Key, entry.Value.Sum / entry.Value.Count, $"Stage {entry.Key}"));
        _labels.Sort((a, b) => a.Key.CompareTo(b.Key));

        // 3. 겹친 셀
        List<Vector2Int> overlaps = _data.OverlapCells;
        _overlapVerts = new Vector3[overlaps.Count * 4];
        for (int i = 0; i < overlaps.Count; i++)
        {
            Vector2Int c = overlaps[i];
            _overlapVerts[i * 4 + 0] = Corner(spec, c.x, c.y);
            _overlapVerts[i * 4 + 1] = Corner(spec, c.x + 1, c.y);
            _overlapVerts[i * 4 + 2] = Corner(spec, c.x + 1, c.y + 1);
            _overlapVerts[i * 4 + 3] = Corner(spec, c.x, c.y + 1);
        }
    }

    /// <summary>0-base 셀 모서리의 월드 좌표. 런타임 CellCenterToWorld와 같은 GridSpec 규약.</summary>
    private Vector3 Corner(GridSpec spec, int x, int y)
    {
        return new Vector3(spec.OriginX + x * spec.CellSize, spec.OriginY + y * spec.CellSize, _data.PlaneZ);
    }

    private static int KeyAt(int[] cells, int width, int height, int x, int y)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
            return STAGE_REGION_NONE;
        return cells[y * width + x];
    }

    private static void AddSegment(List<Vector3> lines, Vector3 a, Vector3 b)
    {
        lines.Add(a);
        lines.Add(b);
    }
    #endregion

    #region Scene 그리기 (Repaint마다 — 계산 없음)
    private void OnSceneGUI(SceneView view)
    {
        if (_visible == false || Event.current.type != EventType.Repaint)
            return;
        if (_runKeys.Count == 0 && _overlapVerts.Length == 0)
            return;

        CompareFunction previousZTest = Handles.zTest;
        Handles.zTest = CompareFunction.Always;

        // 채우기
        for (int i = 0; i < _runKeys.Count; i++)
        {
            int v = i * 4;
            _quad[0] = _runVerts[v];
            _quad[1] = _runVerts[v + 1];
            _quad[2] = _runVerts[v + 2];
            _quad[3] = _runVerts[v + 3];
            Handles.DrawSolidRectangleWithOutline(_quad, KeyColor(_runKeys[i], _fillAlpha), Color.clear);
        }

        // 경계선 — 스테이지당 1회 호출
        foreach (var entry in _outlineByKey)
        {
            Handles.color = KeyColor(entry.Key, 1f);
            Handles.DrawLines(entry.Value);
        }

        // 겹친 셀
        var overlapFill = new Color(1f, 0.1f, 0.1f, 0.6f);
        for (int v = 0; v < _overlapVerts.Length; v += 4)
        {
            _quad[0] = _overlapVerts[v];
            _quad[1] = _overlapVerts[v + 1];
            _quad[2] = _overlapVerts[v + 2];
            _quad[3] = _overlapVerts[v + 3];
            Handles.DrawSolidRectangleWithOutline(_quad, overlapFill, Color.red);
        }

        // 라벨
        _labelStyle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white },
        };
        for (int i = 0; i < _labels.Count; i++)
            Handles.Label(_labels[i].Center, _labels[i].Text, _labelStyle);

        Handles.zTest = previousZTest;
    }

    /// <summary>Key마다 고정된 색 (황금비 색상 분산 — 인접 Key끼리도 색이 잘 갈린다)</summary>
    private static Color KeyColor(int key, float alpha)
    {
        float hue = Mathf.Repeat(key * 0.61803398875f, 1f);
        Color color = Color.HSVToRGB(hue, 0.65f, 1f);
        color.a = alpha;
        return color;
    }
    #endregion
}