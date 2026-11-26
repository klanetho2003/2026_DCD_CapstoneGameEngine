using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// DEV 전용 — 기믹 연출 시연 씬(DEV_GimmickFx)을 만든다. 맵이 없어도 홀로그램 박스 · 붕괴 발판 연출을 볼 수 있다.
/// 스프라이트는 슬라이스 이름으로 찾고, 이름이 아직 다르면 시트 안의 위치(32×32 칸)로 찾는다.
/// </summary>
public static class GimmickFxSandboxBuilder
{
    private const string ScenePath = "Assets/@Scenes/DEV_GimmickFx.unity";
    private const string HologramPath = "Assets/@Resources/Sprites/Gimmicks/Hologram_All.png";
    private const string CrackPath = "Assets/@Resources/Sprites/Gimmicks/Crack_All.png";
    private const string GroundPath = "Assets/@Resources/Sprites/Tile/Tile_Earth_All.png";
    private const int Cell = 32;

    [MenuItem("Tools/DEV/Build Gimmick FX Sandbox")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        if (File.Exists(ScenePath) &&
            !EditorUtility.DisplayDialog("Gimmick FX Sandbox", $"{ScenePath}가 이미 있습니다. 새로 만들까요?\n(씬에서 바꾼 값은 사라집니다)", "새로 만들기", "취소"))
            return;

        // hologram은 박스 · 문양 · 버튼 OFF/ON 그림이 든 시트의 조각들이다.
        Sprite[] hologram = LoadSprites(HologramPath);
        Sprite[] crack = LoadSprites(CrackPath);
        Sprite[] ground = LoadSprites(GroundPath);
        if (hologram.Length == 0 || crack.Length == 0 || ground.Length == 0)
        {
            EditorUtility.DisplayDialog("Gimmick FX Sandbox",
                "스프라이트를 찾지 못했습니다. Hologram_All / Crack_All / Tile_Earth_All이 Sprite(Multiple)로 슬라이스되어 있는지 확인하세요.", "확인");
            return;
        }

        Sprite box = Find(hologram, "Hologram_Box", 0, 2);
        Sprite mark = Find(hologram, "Hologram_SpawnMark", 0, 1);
        Sprite buttonOff = Find(hologram, "Hologram_Button_Off", 0, 0);
        Sprite buttonOn = Find(hologram, "Hologram_Button_On", 1, 0);
        Sprite crack1 = Find(crack, "Crack_1", 0, 2);
        Sprite crack2 = Find(crack, "Crack_2", 0, 1);
        Sprite crack3 = Find(crack, "Crack_3", 0, 0);
        // grass는 시트 맨 윗줄의 무늬 없는 풀밭 타일들이다.
        Sprite[] grass = Enumerable.Range(0, 6).Select(x => Find(ground, null, x, 6)).Where(s => s != null).ToArray();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 카메라: 9×5칸이 보이는 정사영
        GameObject cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
        Camera camera = cameraGo.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 3f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.08f, 0.09f, 0.16f);
        cameraGo.transform.position = new Vector3(0.5f, 0f, -10f);

        // 바닥: x -4~5, y -2~2
        Transform floor = new GameObject("Floor").transform;
        Dictionary<Vector2Int, SpriteRenderer> tiles = new Dictionary<Vector2Int, SpriteRenderer>();
        for (int y = -2; y <= 2; y++)
        {
            for (int x = -4; x <= 5; x++)
            {
                SpriteRenderer tile = CreateSprite($"Ground_{x}_{y}", floor, new Vector2(x, y), grass.Length > 0 ? grass[Mathf.Abs(x * 7 + y * 3) % grass.Length] : null, 0);
                tiles[new Vector2Int(x, y)] = tile;
            }
        }

        // 홀로그램: 버튼(-3,0), 문양 · 박스(-1,0)
        Transform hologramRoot = new GameObject("Hologram").transform;
        SpriteRenderer buttonRenderer = CreateSprite("Button", hologramRoot, new Vector2(-3, 0), buttonOff, 1);
        GridButton button = buttonRenderer.gameObject.AddComponent<GridButton>();
        SetObject(button, "_pressedSprite", buttonOn);

        SpriteRenderer markRenderer = CreateSprite("SpawnMark", hologramRoot, new Vector2(-1, 0), mark, 1);
        SpriteFadeFx markFx = markRenderer.gameObject.AddComponent<SpriteFadeFx>();

        SpriteRenderer boxRenderer = CreateSprite("Box", hologramRoot, new Vector2(-1, 0), box, 2);
        BoxAppearFx boxFx = boxRenderer.gameObject.AddComponent<BoxAppearFx>();
        // sparkle은 박스가 원래 크기에 안착할 때 박스 위쪽 절반에 뿌려지는 반짝이 파티클이며 그림이 없으면 연결하지 않는다.
        GameObject sparkle = HologramSparkleBuilder.LoadOrBuild();
        if (sparkle != null)
        {
            SerializedObject boxSo = new SerializedObject(boxFx);
            SerializedProperty slot = boxSo.FindProperty("_landSlot");
            slot.FindPropertyRelative("EffectPrefab").objectReferenceValue = sparkle;
            slot.FindPropertyRelative("EffectLifetime").floatValue = 0f; // 파티클이 끝나면 스스로 사라진다
            boxSo.ApplyModifiedPropertiesWithoutUndo();
        }

        // 붕괴: (2,0), (3,0) 두 칸 — 바닥 그림 위에 금 그림을 얹는다
        Transform crumbleRoot = new GameObject("Crumble").transform;
        List<GridCrumbleTile> crumbles = new List<GridCrumbleTile>();
        foreach (Vector2Int cell in new[] { new Vector2Int(2, 0), new Vector2Int(3, 0) })
        {
            SpriteRenderer overlay = CreateSprite($"CrumbleTile_{cell.x}_{cell.y}", crumbleRoot, cell, null, 1);
            CrackOverlayFx crackFx = overlay.gameObject.AddComponent<CrackOverlayFx>();
            SetObject(crackFx, "_ground", tiles[cell]);
            GridCrumbleTile crumble = overlay.gameObject.AddComponent<GridCrumbleTile>();
            SetSprites(crumble, "_stepSprites", new Sprite[] { null, crack1, crack2, crack3 }); // 0번 = 금 없음
            crumbles.Add(crumble);
        }

        // 시연 버튼
        GimmickFxSandbox sandbox = new GameObject("GimmickFxSandbox").AddComponent<GimmickFxSandbox>();
        SerializedObject so = new SerializedObject(sandbox);
        so.FindProperty("_button").objectReferenceValue = button;
        so.FindProperty("_spawnMark").objectReferenceValue = markFx;
        so.FindProperty("_box").objectReferenceValue = boxFx;
        SerializedProperty list = so.FindProperty("_crumbleTiles");
        list.arraySize = crumbles.Count;
        for (int i = 0; i < crumbles.Count; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = crumbles[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = sandbox.gameObject;
        Debug.Log($"[GimmickFxSandbox] {ScenePath} 생성 — Play 후 왼쪽 위 버튼으로 시연하세요.");
    }

    /// <summary>시트의 모든 조각을 읽는다.</summary>
    private static Sprite[] LoadSprites(string path)
    {
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
    }

    /// <summary>이름으로 찾고, 없으면 시트 안의 칸 위치(왼쪽 아래가 0,0)로 찾는다.</summary>
    /// <param name="name">찾을 슬라이스 이름이며 null이면 위치로만 찾는다.</param>
    /// <param name="column">왼쪽부터 센 칸 번호다.</param>
    /// <param name="row">아래부터 센 칸 번호다.</param>
    private static Sprite Find(Sprite[] sprites, string name, int column, int row)
    {
        if (name != null)
        {
            Sprite byName = sprites.FirstOrDefault(s => s.name == name);
            if (byName != null)
                return byName;
        }
        Sprite byCell = sprites.FirstOrDefault(s =>
            Mathf.RoundToInt(s.rect.x) / Cell == column && Mathf.RoundToInt(s.rect.y) / Cell == row);
        if (byCell == null && name != null)
            Debug.LogWarning($"[GimmickFxSandbox] '{name}' 스프라이트를 찾지 못함 (칸 {column},{row}). 슬라이스를 확인하세요.");
        return byCell;
    }

    /// <summary>칸 중앙에 스프라이트 오브젝트를 만든다 (1칸 = 1유닛, PPU 32 기준).</summary>
    private static SpriteRenderer CreateSprite(string name, Transform parent, Vector2 cell, Sprite sprite, int order)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(cell.x, cell.y, 0f);
        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = order;
        return renderer;
    }

    /// <summary>비공개 직렬화 필드에 오브젝트 참조를 넣는다.</summary>
    private static void SetObject(Object target, string field, Object value)
    {
        SerializedObject so = new SerializedObject(target);
        so.FindProperty(field).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>비공개 직렬화 배열 필드에 스프라이트 목록을 넣는다.</summary>
    private static void SetSprites(Object target, string field, Sprite[] values)
    {
        SerializedObject so = new SerializedObject(target);
        SerializedProperty property = so.FindProperty(field);
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
