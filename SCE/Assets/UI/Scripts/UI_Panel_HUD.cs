using UnityEngine;
using UnityEngine.UI;
using static Define;

/// <summary>
/// 인게임 HUD (Scene UI). 라운드 진행 중이면 남은 시간을 StageManager 에서 직접 읽어 표시.
/// 타일 수는 SetTiles 로 넣어준다. ESC → 일시정지.
/// </summary>
public class UI_Panel_HUD : UI_Scene
{
    private enum Images
    {
        Img_Clock,
        Img_PlanetIcon,
        Img_StageName,
        Img_RoundNum,
    }

    [Header("왼쪽 위 행성 표시 (수성, 금성, 지구, 화성, 목성, 토성 = StageCatalog 순서)")]
    [SerializeField] private Sprite[] _planetIcons;  // icon_planet_*
    [SerializeField] private Sprite[] _stageNames;   // text_MERCURY …
    [Tooltip("text_round_num_small_1, 2, 3 (ERound A, B, C)")]
    [SerializeField] private Sprite[] _roundNumSprites;

    [Header("왼쪽 아래 타일 목표 (표시만 — 성공/실패 판정은 게임 쪽)")]
    [Tooltip("스테이지 전체 목표 타일 수. 게임에 목표 값이 생기면 SetGoalTiles 로 넣는다")]
    [SerializeField] private int _stageGoalTiles = 80;

    [Header("시계 아이콘")]
    [SerializeField] private Sprite _clockNormal;   // icon_clock
    [SerializeField] private Sprite _clockWarning;  // icon_clock_warning
    [SerializeField] private int _warningSeconds = 10;

    [Header("시간 숫자 시트 (digits_s3_white / digits_s3_red 12칸 순서대로)")]
    [SerializeField] private Sprite[] _timeWhiteGlyphs = new Sprite[12];
    [SerializeField] private Sprite[] _timeRedGlyphs = new Sprite[12];
    [SerializeField] private int _timeAdvance = 18; // s3 = 6*3

    [Header("게임 뒤 우주 배경 (Common/bg_space_960x540, bg_stars_960x540)")]
    [SerializeField] private Sprite _bgSpace;
    [SerializeField] private Sprite _bgStars;
    [Tooltip("맵·캐릭터(Order in Layer 최소 -1)보다 뒤")]
    [SerializeField] private int _bgSortingOrder = -100;

    private PixelDigits _time;
    private PixelDigits _tiles;
    private PixelDigits _tilesTotal;
    private bool _isWarning;
    private int _lastSeconds = int.MinValue;

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        BindImages(typeof(Images));

        GameObject go;
        go = UIPanelInput.FindDeep(gameObject, "Txt_Time");       if (go != null) _time = go.GetComponent<PixelDigits>();
        go = UIPanelInput.FindDeep(gameObject, "Txt_Tiles");      if (go != null) _tiles = go.GetComponent<PixelDigits>();
        go = UIPanelInput.FindDeep(gameObject, "Txt_TilesGoal");  if (go != null) _tilesTotal = go.GetComponent<PixelDigits>(); // 프리팹 이름은 Txt_TilesGoal ("/80")

        CreateBackground();
        return true;
    }

    /// <summary>
    /// 게임 화면 뒤 우주 배경. HUD 와 같은 640×360 ×3 기준으로 가운데 Native Size (넓은 화면에서도 가장자리까지 덮는다).
    /// 자체 Canvas(정렬 순서 _bgSortingOrder)로 맵·캐릭터 뒤에 그린다. 카메라가 움직여도 화면에 고정.
    /// </summary>
    private void CreateBackground()
    {
        if (_bgSpace == null)
            return;

        GameObject panel = UIPanelInput.FindDeep(gameObject, "Panel_HUD");
        Transform parent = panel != null ? panel.transform : transform;
        if (parent.Find("Img_BgSpace") != null)
            return; // 이미 있음

        Image space = CreateImage("Img_BgSpace", parent, _bgSpace);
        space.transform.SetAsFirstSibling();

        // 이 배경만 따로 정렬: HUD 글자는 위, 배경은 게임보다 뒤
        Canvas canvas = space.gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = _bgSortingOrder;

        if (_bgStars != null)
            CreateImage("Img_BgStars", space.transform, _bgStars);
    }

    private static Image CreateImage(string name, Transform parent, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = parent.gameObject.layer;

        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;

        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        image.SetNativeSize();
        return image;
    }

    private void Update()
    {
        if (Managers.Stage == null || Managers.Stage.Phase != EStagePhase.Playing)
            return;

        SetTime(Mathf.CeilToInt(Managers.Stage.RoundRemainingSeconds));

        // 왼쪽 아래 발판 수: 이번 라운드에 플레이어가 가진 타일 수 (밟을 때마다 올라가고, 뺏기면 내려간다)
        if (Managers.Map != null)
        {
            int tiles = Managers.Map.PlayerTerritoryCount;
            // 타일을 새로 칠했을 때만 "뽁" (라운드 시작 연출 중 = timeScale 0 에 칠해지는 시작 칸은 조용히)
            bool gained = tiles > _lastTiles && _lastTiles != int.MinValue && Time.timeScale > 0f;
            SetTileCount(tiles);
            if (gained)
                UISound.Play(Sfx.TileCapture, pitchJitter: 0.08f); // 음 높이를 조금씩 바꿔 덜 반복적으로
        }

        // 다른 Popup 이 없을 때만 일시정지
        if (Managers.UI.GetPopupCount() == 0 && UIPanelInput.ConsumeEsc())
            Managers.UI.ShowPopupUI<UI_Panel_Paused>();
    }

    /// <summary>남은 시간(초). 10초 이하면 빨강.</summary>
    public void SetTime(int seconds)
    {
        Init();
        seconds = Mathf.Max(seconds, 0);
        if (seconds == _lastSeconds)
            return;
        _lastSeconds = seconds;

        bool warning = seconds <= _warningSeconds;
        if (warning && seconds > 0 && Time.timeScale > 0f)
            UISound.Play(Sfx.TimeWarning); // 10초 이하: 1초마다

        if (_time != null)
        {
            if (warning != _isWarning)
            {
                Sprite[] sheet = warning ? _timeRedGlyphs : _timeWhiteGlyphs;
                if (sheet != null && sheet.Length >= 12 && sheet[0] != null)
                    _time.SetGlyphs(sheet, _timeAdvance);
            }
            _time.Text = seconds + "s";
        }

        Image clock = GetImage(Images.Img_Clock);
        if (clock != null)
        {
            Sprite sprite = warning ? _clockWarning : _clockNormal;
            if (sprite != null) clock.sprite = sprite;
        }

        _isWarning = warning;
    }

    /// <summary>
    /// 왼쪽 위: 지금 Stage 의 행성 아이콘 + 이름, 라운드 번호.
    /// 행성은 StageCatalog 목록 순서로 정한다 (행성 선택 화면과 같은 규칙: 1번째 Stage = 수성).
    /// </summary>
    public void SetStage(Data.StageInfoData info, ERound round)
    {
        Init();
        SetTileCount(0); // 새 라운드는 0 부터
        RefreshRoundGoal();

        int planet = StagePlanet.IndexOf(info);
        bool icon = StagePlanet.Apply(GetImage(Images.Img_PlanetIcon), _planetIcons, planet, "HUD 행성 아이콘");
        bool name = StagePlanet.Apply(GetImage(Images.Img_StageName), _stageNames, planet, "HUD 행성 이름");
        StagePlanet.Apply(GetImage(Images.Img_RoundNum), _roundNumSprites, (int)round - (int)ERound.A, "HUD 라운드 번호");

        Image nameImage = GetImage(Images.Img_StageName);
        LogPrinter.Log($"[UI_Panel_HUD] SetStage Stage={info?.StageId} 행성={planet} 아이콘={icon} 이름={name} → {(nameImage != null && nameImage.sprite != null ? nameImage.sprite.name : "null")}");
    }

    /// <summary>스테이지 전체 목표 타일 수 (게임 쪽에서 스테이지 데이터로 넣을 때). 바로 "/남은 목표" 를 다시 그린다.</summary>
    public void SetGoalTiles(int goal)
    {
        _stageGoalTiles = Mathf.Max(0, goal);
        RefreshRoundGoal();
    }

    /// <summary>
    /// "/남은 목표" = 스테이지 목표 − 지난 라운드까지 누적 (0 아래로는 안 내려감).
    /// 예: 목표 80, 1라운드 20개 → 2라운드는 "0 / 60". 라운드 시작 때 한 번 정한다.
    /// </summary>
    private void RefreshRoundGoal()
    {
        Init();
        int earned = Managers.Stage != null ? Managers.Stage.TotalPlayerTiles : 0;
        int remaining = Mathf.Max(0, _stageGoalTiles - earned);
        if (_tilesTotal != null) _tilesTotal.Text = "/" + remaining;
    }

    private int _lastTiles = int.MinValue;

    /// <summary>뒤집은 타일 수만 (목표 "/80" 은 그대로). 바뀐 때만 다시 그린다.</summary>
    public void SetTileCount(int current)
    {
        Init();
        if (current == _lastTiles)
            return;
        _lastTiles = current;
        if (_tiles != null) _tiles.Text = current.ToString();
    }

    /// <summary>뒤집은 타일 수 / 목표</summary>
    public void SetTiles(int current, int total)
    {
        Init();
        if (_tiles != null) _tiles.Text = current.ToString();
        if (_tilesTotal != null) _tilesTotal.Text = "/" + total;
    }
}
