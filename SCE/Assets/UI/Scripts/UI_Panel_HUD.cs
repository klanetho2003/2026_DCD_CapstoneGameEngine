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
    }

    [Header("시계 아이콘")]
    [SerializeField] private Sprite _clockNormal;   // icon_clock
    [SerializeField] private Sprite _clockWarning;  // icon_clock_warning
    [SerializeField] private int _warningSeconds = 10;

    [Header("시간 숫자 시트 (digits_s3_white / digits_s3_red 12칸 순서대로)")]
    [SerializeField] private Sprite[] _timeWhiteGlyphs = new Sprite[12];
    [SerializeField] private Sprite[] _timeRedGlyphs = new Sprite[12];
    [SerializeField] private int _timeAdvance = 18; // s3 = 6*3

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
        go = UIPanelInput.FindDeep(gameObject, "Txt_TilesTotal"); if (go != null) _tilesTotal = go.GetComponent<PixelDigits>();
        return true;
    }

    private void Update()
    {
        if (Managers.Stage == null || Managers.Stage.Phase != EStagePhase.Playing)
            return;

        SetTime(Mathf.CeilToInt(Managers.Stage.RoundRemainingSeconds));

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

    /// <summary>뒤집은 타일 수 / 목표</summary>
    public void SetTiles(int current, int total)
    {
        Init();
        if (_tiles != null) _tiles.Text = current.ToString();
        if (_tilesTotal != null) _tilesTotal.Text = "/" + total;
    }
}
