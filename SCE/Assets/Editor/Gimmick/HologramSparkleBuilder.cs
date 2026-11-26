using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 홀로그램 박스 등장 반짝이(파티클) 프리팹을 만든다. 그림은 Hollogram_Particle.png(5×5 칸, 2열 × 4행).
/// 반짝이 하나가 십자 → 마름모 → X → 점 순서로 줄어들고, 칸마다 왼쪽 · 오른쪽 색을 번갈아 써서 반짝이게 한다.
/// 크기는 원래 픽셀 그대로(5/32 유닛)이며 크기를 늘리지 않아 픽셀이 깨지지 않는다.
/// </summary>
public static class HologramSparkleBuilder
{
    public const string PrefabPath = "Assets/@Resources/Prefabs/Gimmicks/Fx/FX_HologramSparkle.prefab";
    private const string MaterialPath = "Assets/@Resources/Prefabs/Gimmicks/Fx/M_PixelParticle.mat";
    private const string SpritePath = "Assets/@Resources/Sprites/Gimmicks/Hollogram_Particle.png";
    private const int Cell = 5;
    private const float PixelsPerUnit = 32f;

    [MenuItem("Tools/DEV/Rebuild Hologram Sparkle Prefab")]
    private static void RebuildMenu()
    {
        if (File.Exists(PrefabPath) &&
            !EditorUtility.DisplayDialog("Hologram Sparkle", $"{PrefabPath}를 새로 만들까요?\n(프리팹에서 바꾼 값은 사라집니다)", "새로 만들기", "취소"))
            return;
        GameObject prefab = Build();
        if (prefab != null)
        {
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
        }
    }

    /// <summary>프리팹이 있으면 그대로 쓰고, 없으면 만든다.</summary>
    public static GameObject LoadOrBuild()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        return existing != null ? existing : Build();
    }

    /// <summary>머티리얼과 파티클 프리팹을 (다시) 만든다. 그림을 찾지 못하면 null이다.</summary>
    public static GameObject Build()
    {
        Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(SpritePath).OfType<Sprite>().ToArray();
        // frames는 반짝이가 줄어드는 순서(십자 → 마름모 → X → 점)이며 각 단계에서 왼쪽 · 오른쪽 색을 번갈아 쓴다.
        Sprite[] frames =
        {
            FindCell(sprites, 0, 0), FindCell(sprites, 1, 0), // 십자 (맨 아래 행)
            FindCell(sprites, 0, 1), FindCell(sprites, 1, 1), // 마름모
            FindCell(sprites, 0, 2), FindCell(sprites, 1, 2), // X
            FindCell(sprites, 0, 3), FindCell(sprites, 1, 3), // 점 (맨 위 행)
        };
        if (frames.Any(f => f == null))
        {
            EditorUtility.DisplayDialog("Hologram Sparkle",
                $"{SpritePath}에서 5×5 조각 8개를 찾지 못했습니다. Sprite(Multiple)로 5×5 슬라이스되어 있는지 확인하세요.", "확인");
            return null;
        }

        EnsureFolder(Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));
        Material material = LoadOrCreateMaterial();

        GameObject go = new GameObject("FX_HologramSparkle");
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // 한 번 터지고 끝나면 스스로 사라진다 (GimmickFxSlot의 EffectLifetime은 0으로 둔다).
        ParticleSystem.MainModule main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
        main.startSpeed = 0f; // 움직임은 아래 Velocity over Lifetime이 맡는다 (2D 화면 안에서만 움직이도록)
        main.startSize = Cell / PixelsPerUnit;
        main.startRotation = 0f;
        main.startColor = Color.white;
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 32;
        main.stopAction = ParticleSystemStopAction.Destroy;

        // 등장 순간 한 번에 8~12개.
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8, 12) });

        // 박스 위쪽 절반 안의 아무 곳에서 생긴다. 연출은 박스가 안착한 순간 박스 중심 위치에서 이 프리팹을 만든다
        // (중심 기준 가로 -0.45~0.45, 세로 0~0.5 = 한 칸 박스의 위쪽 절반).
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.position = new Vector3(0f, 0.25f, 0f);
        shape.scale = new Vector3(0.9f, 0.5f, 0f);
        shape.rotation = Vector3.zero;

        // 위로 떠오르며 좌우로 살짝 흩어지고, 갈수록 느려진다.
        ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.speedModifier = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.25f));

        // 마지막 30% 동안 흐려진다.
        ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
        color.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        color.color = new ParticleSystem.MinMaxGradient(gradient);

        // 수명 동안 8칸을 한 번 지나며 줄어든다.
        ParticleSystem.TextureSheetAnimationModule sheet = ps.textureSheetAnimation;
        sheet.enabled = true;
        sheet.mode = ParticleSystemAnimationMode.Sprites;
        for (int i = 0; i < frames.Length; i++)
            sheet.AddSprite(frames[i]);
        sheet.timeMode = ParticleSystemAnimationTimeMode.Lifetime;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
        sheet.cycleCount = 1;

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        renderer.sortingOrder = 3; // 박스(2) 위
        renderer.alignment = ParticleSystemRenderSpace.View;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        Object.DestroyImmediate(go);
        Debug.Log($"[HologramSparkle] {PrefabPath} 생성");
        return prefab;
    }

    /// <summary>스프라이트 그림을 그대로 보여 줄 파티클용 머티리얼을 읽거나 만든다.</summary>
    private static Material LoadOrCreateMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material != null)
            return material;
        // shader는 텍스처 시트(Sprites 모드)의 그림을 조명 없이 그대로 그리는 기본 스프라이트 셰이더다.
        Shader shader = Shader.Find("Sprites/Default");
        material = new Material(shader) { name = "M_PixelParticle" };
        AssetDatabase.CreateAsset(material, MaterialPath);
        return material;
    }

    /// <summary>칸 위치(왼쪽 아래가 0,0)로 조각을 찾는다.</summary>
    private static Sprite FindCell(Sprite[] sprites, int column, int row)
    {
        return sprites.FirstOrDefault(s =>
            Mathf.RoundToInt(s.rect.x) / Cell == column && Mathf.RoundToInt(s.rect.y) / Cell == row);
    }

    /// <summary>Assets 아래 폴더를 한 단계씩 만든다.</summary>
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
