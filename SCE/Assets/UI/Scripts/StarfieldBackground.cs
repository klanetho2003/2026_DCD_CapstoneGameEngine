using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Canvas 아래의 빈 UI 오브젝트에 붙이면 Play 시 자식으로 별 Image들을 만든다.
/// 뒤의 남색 띠 배경은 SCE와 같이 bg_space PNG(Image)가 담당하고, 이 컴포넌트는 별만 만든다.
/// Awake에서 생성하므로 다른 스크립트의 Start부터 Stars를 쓸 수 있다.
/// </summary>
[DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
public class StarfieldBackground : MonoBehaviour
{
    [Header("화면 기준")]
    [Tooltip("원본 픽셀아트 가로 칸 수")]
    [Min(1)] public int designWidth = 640;

    [Tooltip("원본 픽셀아트 세로 칸 수")]
    [Min(1)] public int designHeight = 360;

    [Tooltip("픽셀아트 1칸 = Canvas 기준 몇 px. 3이면 640×360 → 1920×1080 (SCE Canvas Scaler 기준 해상도)")]
    [Min(1)] public int pixelScale = 3;

    [Header("스프라이트")]
    [Tooltip("별 점 스프라이트 (UI/Sprites/Common/star_dot.png)")]
    public Sprite starSprite;

    [Header("디버그")]
    [Tooltip("켜면 Canvas 설정 점검 결과를 콘솔에 출력한다 ([StarfieldBackground]로 검색)")]
    public bool debugLog = false;

    [Header("별")]
    public StarFieldSettings stars = new StarFieldSettings();

    /// <summary>별 배치와 Image 생성을 맡는 도우미다.</summary>
    private readonly StarField _starField = new StarField();
    /// <summary>생성한 별들을 담는 자식 RectTransform이다.</summary>
    private RectTransform _starsRoot;

    /// <summary>생성된 별 목록이며 StarTwinkler가 사용한다.</summary>
    public IReadOnlyList<StarData> Stars => _starField.Stars;

    /// <summary>별 생성이 끝났는지 나타낸다.</summary>
    public bool IsBuilt { get; private set; }

    /// <summary>생성(또는 다시 생성)이 끝날 때마다 알린다. 별을 쓰는 컴포넌트가 구독한다.</summary>
    public event System.Action<StarfieldBackground> Built;

    /// <summary>현재 설정 기준의 픽셀 격자 정보다.</summary>
    public PixelGrid Grid => new PixelGrid(designWidth, designHeight, pixelScale);

    /// <summary>다른 컴포넌트의 Start보다 먼저 별을 만든다.</summary>
    private void Awake()
    {
        Build();
    }

    /// <summary>디버그 로그가 켜져 있을 때만 Canvas 설정을 점검한다.</summary>
    private void Start()
    {
        if (debugLog)
            CheckCanvas();
    }

    /// <summary>생성한 자식 오브젝트를 정리한다.</summary>
    private void OnDestroy()
    {
        Clear();
    }

    /// <summary>별을 (다시) 만든다.</summary>
    public void Build()
    {
        Clear();

        if (starSprite == null)
            this.LogWarning("Star Sprite가 비어 있습니다. UI/Sprites/Common/star_dot.png를 연결하세요. (비어 있으면 흰 사각형으로 그려집니다)");

        var grid = Grid;
        _starsRoot = CreateRoot("Stars", grid);
        _starField.Build(_starsRoot, stars, grid, starSprite);

        IsBuilt = true;
        Built?.Invoke(this);
    }

    /// <summary>생성한 별 오브젝트를 정리한다.</summary>
    public void Clear()
    {
        IsBuilt = false;
        if (_starsRoot != null)
            Destroy(_starsRoot.gameObject);
        _starsRoot = null;
    }

    /// <summary>인스펙터 값을 바꾼 뒤 Play 중에 다시 생성해 볼 때 사용한다 (컴포넌트 ⋮ 메뉴).</summary>
    [ContextMenu("다시 생성 (Play 중에만)")]
    private void RebuildFromMenu()
    {
        if (!Application.isPlaying)
        {
            this.Log("다시 생성은 Play 중에만 동작합니다. (편집 모드에서 씬을 바꾸지 않기 위해)");
            return;
        }
        Build();
    }

    /// <summary>Canvas 기준 해상도 크기의 중앙 정렬 자식 RectTransform을 만든다.</summary>
    /// <param name="rootName">자식 오브젝트 이름이다.</param>
    /// <param name="grid">크기를 정할 픽셀 격자다.</param>
    private RectTransform CreateRoot(string rootName, PixelGrid grid)
    {
        var go = new GameObject(rootName, typeof(RectTransform));
        go.layer = gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(grid.ScreenWidth, grid.ScreenHeight);
        return rect;
    }

    /// <summary>Canvas와 Canvas Scaler가 SCE UI와 같은 설정인지 확인만 하고 안내한다 (값은 바꾸지 않음).</summary>
    private void CheckCanvas()
    {
        var grid = Grid;
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            this.LogWarning("Canvas 아래에 있지 않아 별이 보이지 않습니다. Canvas의 자식으로 옮겨 주세요.");
            return;
        }

        var scaler = canvas.rootCanvas.GetComponent<CanvasScaler>();
        var reference = new Vector2(grid.ScreenWidth, grid.ScreenHeight);
        if (scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize || scaler.referenceResolution != reference)
        {
            this.LogWarning($"Canvas Scaler를 Scale With Screen Size, 기준 해상도 {grid.ScreenWidth}×{grid.ScreenHeight}로 맞춰야 " +
                            "별이 픽셀 칸에 정확히 맞습니다. (SCE UI와 같은 설정)");
        }
        else
        {
            this.Log($"Canvas OK: Scale With Screen Size, 기준 해상도 {grid.ScreenWidth}×{grid.ScreenHeight}.");
        }

        if (transform.localScale != Vector3.one || transform.localRotation != Quaternion.identity)
            this.LogWarning("이 오브젝트의 Scale은 1, Rotation은 0으로 두세요. 그렇지 않으면 픽셀이 어긋날 수 있습니다.");
    }
}
