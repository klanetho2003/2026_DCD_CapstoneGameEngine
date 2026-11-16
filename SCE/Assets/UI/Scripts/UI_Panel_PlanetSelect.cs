using System;
using System.Collections;
using System.Collections.Generic;
using Data;
using UnityEngine;
using UnityEngine.UI;
using static Define;

/// <summary>
/// 행성 선택 = Stage 선택 화면. UI_StageSelect 와 같은 사용법:
///   var ui = Managers.UI.ShowBaseUI&lt;UI_Panel_PlanetSelect&gt;(sortOrder: UI_Panel_PlanetSelect.SORT_ORDER);
///   ui.SetInfo(onStageSelected, onBack);  ui.Show() / ui.Hide()
/// 행성 순서(수성, 금성, 지구, 화성, 목성, 토성) = StageCatalog 목록 순서.
/// 열린 행성만 컬러, 잠긴 행성은 흑백(*_locked) — 해금 규칙은 IsUnlocked, 클리어 기록은 Managers.Stage.Records.
/// 잠긴 행성을 누르면 좌우로 흔들린다.
/// 스스로 닫히지 않는다 — 열고 닫기는 StageFlowPresenter 가 한다.
/// </summary>
public class UI_Panel_PlanetSelect : UI_Scene
{
    /// <summary>다른 Scene UI(0)보다 위, Popup(10 이상)보다 아래.</summary>
    public const int SORT_ORDER = 5;

    private enum GameObjects
    {
        Btn_Back,
        Btn_Land,
        Btn_Planet_Mercury,
        Btn_Planet_Venus,
        Btn_Planet_Earth,
        Btn_Planet_Mars,
        Btn_Planet_Jupiter,
        Btn_Planet_Saturn,
    }

    private static readonly GameObjects[] Planets =
    {
        GameObjects.Btn_Planet_Mercury,
        GameObjects.Btn_Planet_Venus,
        GameObjects.Btn_Planet_Earth,
        GameObjects.Btn_Planet_Mars,
        GameObjects.Btn_Planet_Jupiter,
        GameObjects.Btn_Planet_Saturn,
    };

    [Header("행성 이미지 (수성, 금성, 지구, 화성, 목성, 토성 순서)")]
    [SerializeField] private Sprite[] _planetSprites;
    [SerializeField] private Sprite[] _lockedSprites;
    [Tooltip("잠긴 행성 가운데에 올리는 자물쇠 (실행 중에 Img_Lock 으로 만든다)")]
    [SerializeField] private Sprite _lockSprite;

    [Header("스테이지에서 돌아올 때 발사 연출")]
    [SerializeField] private Sprite _shipSprite;          // ship
    [SerializeField] private Sprite[] _shipFlameSprites;  // ship_flame_0, 1
    [Tooltip("출발하는 행성: planet_*_big (수성, 금성, 지구, 화성, 목성, 토성)")]
    [SerializeField] private Sprite[] _bigPlanetSprites;

    private const float SHAKE_DURATION = 0.25f;
    private const float SHAKE_DISTANCE = 2f; // Panel 안 픽셀 (화면에서는 3배)

    private const float HOVER_SCALE = 1.1f;
    private const float HOVER_DURATION = 0.08f;

    // 열린 행성 둥둥: 위아래 1px (화면 3px), 한 번 오르내리는 데 2초. 정수 픽셀로 한 칸씩
    private const float BOB_PERIOD = 2f;
    private const float BOB_DISTANCE = 1f;
    private const float BOB_PHASE_STEP = 0.37f; // 행성마다 시작을 엇갈리게 (주기 비율)

    private readonly StageCatalog _catalog = new StageCatalog();
    // 위치 = 기준 위치 + 흔들림(x) + 둥둥(y). LateUpdate 한 곳에서만 정한다
    private readonly Vector2[] _basePos = new Vector2[Planets.Length];
    private readonly float[] _shakeTime = new float[Planets.Length]; // 0 이상이면 흔들리는 중
    private readonly bool[] _unlocked = new bool[Planets.Length];
    private readonly Dictionary<RectTransform, Coroutine> _hovers = new Dictionary<RectTransform, Coroutine>();
    private readonly GameObject[] _locks = new GameObject[Planets.Length];
    private Action<StageInfoData> _onSelected;
    private Action _onBack;
    private int _selected = -1;

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        BindObjects(typeof(GameObjects));

        GameObject land = GetObject(GameObjects.Btn_Land);
        if (land != null) BindEvent(land, (evt) => OnClickLand());

        GameObject back = GetObject(GameObjects.Btn_Back);
        if (back != null) BindEvent(back, (evt) => { UISound.Play(Sfx.UiBack); _onBack?.Invoke(); });

        for (int i = 0; i < Planets.Length; i++)
        {
            int index = i;
            _shakeTime[i] = -1f;
            GameObject planet = GetObject(Planets[i]);
            if (planet == null)
                continue;

            _basePos[i] = ((RectTransform)planet.transform).anchoredPosition;

            BindEvent(planet, (evt) => OnClickPlanet(index));
            BindEvent(planet, (evt) => SetHover(index, true), UIEvent.PointerEnter);
            BindEvent(planet, (evt) => SetHover(index, false), UIEvent.PointerExit);
            _locks[i] = CreateLock(planet);
            _frontSibling = Mathf.Max(_frontSibling, planet.transform.GetSiblingIndex());
        }

        return true;
    }

    /// <summary>
    /// 행성끼리 겹칠 때 이 행성(링·이름 포함)이 맨 위에 그려지게. 형제 순서 = 그리는 순서.
    /// 행성들 중 마지막 자리로만 옮긴다 — 상단 바·착륙 버튼보다 위로 가지 않는다.
    /// </summary>
    private int _frontSibling = -1;

    private void BringToFront(int index)
    {
        if (index < 0 || index >= Planets.Length || _frontSibling < 0)
            return;

        GameObject planet = GetObject(Planets[index]);
        if (planet != null)
            planet.transform.SetSiblingIndex(_frontSibling);
    }

    /// <summary>행성 가운데에 자물쇠 이미지. 도트가 어긋나지 않게 행성 왼쪽 아래 기준 정수 위치에 둔다.</summary>
    private GameObject CreateLock(GameObject planet)
    {
        if (_lockSprite == null)
            return null;

        var go = new GameObject("Img_Lock", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = planet.layer;

        var rt = (RectTransform)go.transform;
        rt.SetParent(planet.transform, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;

        var image = go.GetComponent<Image>();
        image.sprite = _lockSprite;
        image.raycastTarget = false; // 클릭은 행성이 받는다
        image.SetNativeSize();

        Vector2 planetSize = ((RectTransform)planet.transform).rect.size;
        Vector2 lockSize = rt.sizeDelta;
        rt.anchoredPosition = new Vector2(
            Mathf.Floor((planetSize.x - lockSize.x) / 2f),
            Mathf.Floor((planetSize.y - lockSize.y) / 2f));

        go.SetActive(false);
        return go;
    }

    /// <param name="onSelected">착륙을 눌렀을 때. 고른 Stage 정보를 넘긴다.</param>
    /// <param name="onBack">Back 을 눌렀을 때 (없으면 아무 일 없음).</param>
    public void SetInfo(Action<StageInfoData> onSelected, Action onBack = null)
    {
        Init();
        _onSelected = onSelected;
        _onBack = onBack;

        if (_catalog.Load() == false)
            LogPrinter.LogError("[UI_Panel_PlanetSelect] Stage 목록을 불러오지 못함");

        RefreshPlanets();
        Select(DEFAULT_PLANET);
    }

    public void Show()
    {
        gameObject.SetActive(true);
        RefreshPlanets(); // 클리어하고 돌아오면 다음 행성이 열려 있어야 한다
        Select(DEFAULT_PLANET);
    }

    /// <summary>처음 열 때 골라 둔 행성 = 수성 (항상 열려 있음)</summary>
    private const int DEFAULT_PLANET = 0;

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private int StageCount
    {
        get { return _catalog.Stages == null ? 0 : _catalog.Stages.Count; }
    }

    /// <summary>
    /// 첫 Stage는 처음부터 열림, 그 다음은 바로 앞 Stage를 클리어하면 열림. 목록에 없는 행성은 잠김.
    /// 클리어 여부는 Managers.Stage.Records — 진행 파일에 저장되고 타이틀 로드(Managers.LoadGameData) 때 불러온 기록이다.
    /// </summary>
    private bool IsUnlocked(int index)
    {
        if (index < 0 || index >= StageCount)
            return false;
        if (index == 0)
            return true;
        return Managers.Stage.Records.TryGet(_catalog.Stages[index - 1].StageId, out StageRecord record) && record.Cleared;
    }

    /// <summary>열린 행성은 컬러, 잠긴 행성은 흑백 이미지로</summary>
    private void RefreshPlanets()
    {
        for (int i = 0; i < Planets.Length; i++)
        {
            bool unlocked = IsUnlocked(i);
            _unlocked[i] = unlocked;

            if (_locks[i] != null)
                _locks[i].SetActive(unlocked == false);

            GameObject planet = GetObject(Planets[i]);
            if (planet == null || planet.TryGetComponent(out Image image) == false)
                continue;

            Sprite[] sprites = unlocked ? _planetSprites : _lockedSprites;
            if (sprites != null && i < sprites.Length && sprites[i] != null)
                image.sprite = sprites[i];
        }
    }

    #region 열린 행성 호버 — 살짝 커진다
    private void SetHover(int index, bool hover)
    {
        GameObject planet = GetObject(Planets[index]);
        if (planet == null)
            return;
        if (hover && IsUnlocked(index) == false)
            return; // 잠긴 행성은 커지지 않는다 (나갈 때는 항상 원래 크기로)

        if (hover)
            UISound.Play(Sfx.UiHover);

        // 커진 행성이 옆 행성에 가려지지 않게. 나가면 선택된 행성을 다시 맨 위로
        BringToFront(hover ? index : _selected);

        var rt = (RectTransform)planet.transform;
        if (_hovers.TryGetValue(rt, out Coroutine running) && running != null)
            StopCoroutine(running);

        float target = hover ? HOVER_SCALE : 1f;
        if (isActiveAndEnabled == false)
        {
            rt.localScale = new Vector3(target, target, 1f);
            _hovers.Remove(rt);
            return;
        }
        _hovers[rt] = StartCoroutine(CoScale(rt, target));
    }

    private IEnumerator CoScale(RectTransform rt, float target)
    {
        float start = rt.localScale.x;
        float time = 0f;

        while (time < HOVER_DURATION)
        {
            time += Time.unscaledDeltaTime;
            float s = Mathf.Lerp(start, target, Mathf.Clamp01(time / HOVER_DURATION));
            rt.localScale = new Vector3(s, s, 1f);
            yield return null;
        }

        rt.localScale = new Vector3(target, target, 1f);
        _hovers.Remove(rt);
    }

    /// <summary>화면을 다시 열 때 커진 채로 남지 않게</summary>
    private void ResetHover()
    {
        for (int i = 0; i < Planets.Length; i++)
        {
            GameObject planet = GetObject(Planets[i]);
            if (planet != null)
                planet.transform.localScale = Vector3.one;
        }
    }
    #endregion

    private void OnClickPlanet(int index)
    {
        if (IsUnlocked(index) == false)
        {
            if (_shakeTime[index] < 0f)
            {
                _shakeTime[index] = 0f; // 흔들리는 중에 또 누르면 무시
                UISound.Play(Sfx.Locked);
            }
            return;
        }

        UISound.Play(Sfx.UiSelect);
        Select(index);
    }

    private void Select(int index)
    {
        if (index >= 0 && IsUnlocked(index) == false)
            return;

        _selected = index;
        BringToFront(index); // 링과 이름이 옆 행성에 가려지지 않게

        // 선택된 행성의 링(Img_Ring)만 켜기
        for (int i = 0; i < Planets.Length; i++)
        {
            GameObject ring = UIPanelInput.FindDeep(GetObject(Planets[i]), "Img_Ring");
            if (ring != null)
                ring.SetActive(i == _selected);
        }

        // 행성을 안 골랐으면 착륙 버튼 비활성
        GameObject land = GetObject(GameObjects.Btn_Land);
        if (land != null && land.TryGetComponent(out Button button))
            button.interactable = _selected >= 0;
    }

    private void OnClickLand()
    {
        if (_selected < 0 || _selected >= StageCount)
            return;

        UISound.Play(Sfx.Land); // 우주선이 행성으로 날아가는 소리
        _onSelected?.Invoke(_catalog.Stages[_selected]);
    }

    // ESC = 설정창. 다른 Popup(설정창 포함)이 떠 있으면 그쪽이 ESC 를 쓴다
    private void Update()
    {
        if (_init == false || _launchRoot != null) // 발사 연출 중에는 받지 않는다
            return;

        if (Managers.UI.GetPopupCount() == 0 && UIPanelInput.ConsumeEsc())
        {
            UISound.Play(Sfx.UiSelect);
            Managers.UI.ShowPopupUI<UI_Panel_Settings>();
        }
    }

    #region 행성 움직임 — 열린 행성 둥둥, 잠긴 행성 누르면 흔들기
    private void LateUpdate()
    {
        if (_init == false)
            return;

        float now = Time.unscaledTime; // 일시정지(timeScale 0)에도 움직인다
        float dt = Time.unscaledDeltaTime;

        for (int i = 0; i < Planets.Length; i++)
        {
            GameObject planet = GetObject(Planets[i]);
            if (planet == null)
                continue;

            float x = 0f;
            if (_shakeTime[i] >= 0f)
            {
                _shakeTime[i] += dt;
                float t = _shakeTime[i] / SHAKE_DURATION;
                if (t >= 1f)
                    _shakeTime[i] = -1f;
                else // 좌우로 3번 왕복하며 점점 줄어든다
                    x = Mathf.Round(Mathf.Sin(t * Mathf.PI * 6f) * SHAKE_DISTANCE * (1f - t));
            }

            float y = 0f;
            if (_unlocked[i])
            {
                float phase = (now / BOB_PERIOD + i * BOB_PHASE_STEP) * Mathf.PI * 2f;
                y = Mathf.Round(Mathf.Sin(phase) * BOB_DISTANCE); // -1, 0, 1 칸
            }

            ((RectTransform)planet.transform).anchoredPosition = _basePos[i] + new Vector2(x, y);
        }
    }

    /// <summary>화면이 꺼질 때 모든 행성을 기준 위치로</summary>
    private void ResetPositions()
    {
        for (int i = 0; i < Planets.Length; i++)
        {
            _shakeTime[i] = -1f;
            GameObject planet = GetObject(Planets[i]);
            if (planet != null)
                ((RectTransform)planet.transform).anchoredPosition = _basePos[i];
        }
    }

    private void OnDisable()
    {
        if (_init == false)
            return;

        // 발사 연출 중에 꺼지면 연출 오브젝트 정리 (코루틴은 꺼지면서 멈춘다)
        if (_launchRoot != null)
        {
            Destroy(_launchRoot);
            _launchRoot = null;
        }

        ResetPositions();

        // 커지던 중이면 원래 크기로
        foreach (var pair in _hovers)
        {
            if (pair.Value != null)
                StopCoroutine(pair.Value);
        }
        _hovers.Clear();
        ResetHover();
    }
    #endregion

    #region 발사 연출 — 스테이지에서 돌아올 때 우주선이 행성을 떠나 우주로
    // 좌표는 Panel 640×360 가운데 기준 정수 (화면에서는 3배)
    private const float LAUNCH_IDLE = 0.45f;   // 엔진 점화, 살짝 떨림
    private const float LAUNCH_RISE = 1.35f;   // 떠오르기 (행성은 아래로 사라지고 별이 흐른다)
    private const float LAUNCH_EXIT = 0.6f;    // 우주선이 위로 빠져나가는 동안 배경이 걷히고 행성 선택이 나타난다
    private const float FLAME_FRAME = 0.08f;
    private const int SHIP_SCALE = 2;          // 고양이·슬라임처럼 2배
    private const float PLANET_Y = -314f;      // 시작 화면과 같은 큰 행성 위치 (가운데 (320,494))

    private GameObject _launchRoot;

    /// <summary>planetIndex 행성에서 우주선이 떠나는 연출을 행성 선택 화면 위에 덮어 보여 준다. 끝나면 사라진다.</summary>
    public void PlayLaunch(int planetIndex)
    {
        Init();
        if (_shipSprite == null || isActiveAndEnabled == false)
            return;

        if (_launchRoot != null)
            Destroy(_launchRoot);

        GameObject panel = UIPanelInput.FindDeep(gameObject, "Panel_PlanetSelect");
        Transform parent = panel != null ? panel.transform : transform;

        // 배경: 행성 선택 화면의 우주·별 그림을 그대로 쓴다. 클릭을 막는다
        Image bgSource = FindImage("Img_BgSpace");
        Image starsSource = FindImage("Img_BgStars");

        // 묶음: [배경·별·행성 (서서히 걷힘)] + [우주선 (끝까지 또렷하게, 맨 위)]
        var holder = new GameObject("Launch", typeof(RectTransform));
        holder.layer = parent.gameObject.layer;
        var holderRt = (RectTransform)holder.transform;
        holderRt.SetParent(parent, false);
        holderRt.anchorMin = holderRt.anchorMax = holderRt.pivot = new Vector2(0.5f, 0.5f);
        holderRt.anchoredPosition = Vector2.zero;
        holderRt.SetAsLastSibling();
        _launchRoot = holder;

        Image bg = CreateLaunchImage("Img_LaunchBg", holderRt, bgSource != null ? bgSource.sprite : null);
        bg.raycastTarget = true; // 연출이 끝날 때까지 클릭을 막는다
        if (bg.sprite == null)
        {
            bg.color = new Color32(0x0e, 0x12, 0x2a, 0xff);
            bg.rectTransform.sizeDelta = new Vector2(960f, 540f); // 넓은 화면까지 덮는 크기
        }
        var group = bg.gameObject.AddComponent<CanvasGroup>(); // 배경 묶음만 걷힌다

        RectTransform stars = starsSource != null ? CreateLaunchImage("Img_LaunchStars", bg.transform, starsSource.sprite).rectTransform : null;

        RectTransform planet = null;
        if (_bigPlanetSprites != null && planetIndex >= 0 && planetIndex < _bigPlanetSprites.Length && _bigPlanetSprites[planetIndex] != null)
        {
            planet = CreateLaunchImage("Img_LaunchPlanet", bg.transform, _bigPlanetSprites[planetIndex]).rectTransform;
            planet.anchoredPosition = new Vector2(0f, PLANET_Y);
        }

        // 우주선: 바닥이 행성 꼭대기에 닿게. 배경 묶음 밖이라 걷힐 때도 흐려지지 않는다
        RectTransform ship = CreateLaunchImage("Img_Ship", holderRt, _shipSprite).rectTransform;
        ship.localScale = new Vector3(SHIP_SCALE, SHIP_SCALE, 1f);
        float shipHalf = ship.sizeDelta.y * SHIP_SCALE * 0.5f;
        float planetTop = PLANET_Y + (planet != null ? planet.sizeDelta.y * 0.5f : 0f);
        float shipStartY = Mathf.Round(planetTop + shipHalf);
        ship.anchoredPosition = new Vector2(0f, shipStartY);

        // 불꽃: 우주선 아래 (우주선 안 좌표 = 1배 픽셀)
        Image flame = null;
        if (_shipFlameSprites != null && _shipFlameSprites.Length > 0 && _shipFlameSprites[0] != null)
        {
            flame = CreateLaunchImage("Img_Flame", ship, _shipFlameSprites[0]);
            flame.rectTransform.pivot = new Vector2(0.5f, 1f);
            flame.rectTransform.anchoredPosition = new Vector2(0f, -ship.sizeDelta.y * 0.5f); // 우주선 아래 끝에 딱 붙게
            flame.gameObject.SetActive(false);
        }

        UISound.Play(Sfx.Launch);
        StartCoroutine(CoLaunch(holder, group, stars, planet, ship, flame, shipStartY));
    }

    private IEnumerator CoLaunch(GameObject root, CanvasGroup group, RectTransform stars, RectTransform planet, RectTransform ship, Image flame, float shipStartY)
    {
        float total = LAUNCH_IDLE + LAUNCH_RISE + LAUNCH_EXIT;
        float time = 0f;
        float riseTop = 20f;               // 떠오르기가 끝나는 높이
        float exitTop = 180f + 60f;        // 화면 위 밖

        while (time < total && root != null)
        {
            time += Time.unscaledDeltaTime;

            // 불꽃: 점화 후 두 장 번갈아
            if (flame != null)
            {
                bool lit = time > LAUNCH_IDLE * 0.3f;
                flame.gameObject.SetActive(lit);
                if (lit && _shipFlameSprites.Length > 1)
                    flame.sprite = _shipFlameSprites[(int)(time / FLAME_FRAME) % 2];
            }

            float shipY = shipStartY, shipX = 0f, planetY = PLANET_Y, starsY = 0f;
            if (time < LAUNCH_IDLE)
            {
                shipX = (int)(time / 0.05f) % 2 == 0 ? 0f : 1f; // 시동 떨림
            }
            else if (time < LAUNCH_IDLE + LAUNCH_RISE)
            {
                float u = (time - LAUNCH_IDLE) / LAUNCH_RISE;
                float e = u * u;                                  // 천천히 출발해서 빨라진다
                shipY = Mathf.Lerp(shipStartY, riseTop, e);
                planetY = PLANET_Y - 320f * e;                    // 행성은 아래로 멀어진다
                starsY = -70f * e;                                // 별이 아래로 흐른다
            }
            else
            {
                float u = Mathf.Clamp01((time - LAUNCH_IDLE - LAUNCH_RISE) / LAUNCH_EXIT);
                shipY = Mathf.Lerp(riseTop, exitTop, u * u);      // 우주선은 또렷한 채로 화면 위로 빠져나간다
                planetY = PLANET_Y - 320f;
                starsY = -70f - 20f * u;
                // 우주선이 위로 사라지는 동안 배경이 걷히며 행성 선택 화면이 나타난다
                group.alpha = 1f - Mathf.Clamp01((u - 0.2f) / 0.8f);
            }

            // 도트가 흐려지지 않게 정수 픽셀로
            ship.anchoredPosition = new Vector2(shipX, Mathf.Round(shipY));
            if (planet != null) planet.anchoredPosition = new Vector2(0f, Mathf.Round(planetY));
            if (stars != null) stars.anchoredPosition = new Vector2(0f, Mathf.Round(starsY));
            yield return null;
        }

        if (root != null)
            Destroy(root);
        if (_launchRoot == root)
            _launchRoot = null;
    }

    private Image FindImage(string name)
    {
        GameObject go = UIPanelInput.FindDeep(gameObject, name);
        return go != null ? go.GetComponent<Image>() : null;
    }

    private static Image CreateLaunchImage(string name, Transform parent, Sprite sprite)
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
        if (sprite != null)
            image.SetNativeSize();
        return image;
    }
    #endregion
}
