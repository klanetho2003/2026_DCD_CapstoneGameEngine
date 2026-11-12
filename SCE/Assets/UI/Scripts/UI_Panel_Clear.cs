using System;
using UnityEngine;
using static Define;

/// <summary>
/// CLEAR! (Popup). 아무 키나 클릭 → 닫히고 콜백.
///   Managers.UI.ShowPopupUI&lt;UI_Panel_Clear&gt;().SetInfo(tiles, target, onConfirmed);
/// </summary>
public class UI_Panel_Clear : UI_Popup
{
    private Action _onConfirmed;
    private int _openedFrame;

    private const float INPUT_DELAY = 0.8f;
    private float _acceptInputAt;

    private PixelDigits _result;
    private PixelDigits _resultTotal;

    [Tooltip("깃발 아래 큰 행성: planet_*_big (수성, 금성, 지구, 화성, 목성, 토성 = StageCatalog 순서)")]
    [SerializeField] private Sprite[] _planetSprites;
    private UnityEngine.UI.Image _planet;

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        GameObject planet = UIPanelInput.FindDeep(gameObject, "Img_Planet");
        if (planet != null) _planet = planet.GetComponent<UnityEngine.UI.Image>();

        // 프리팹 이름: Row_Result / Txt_Got (얻은 타일 수), Txt_Goal ("/목표")
        GameObject go;
        go = UIPanelInput.FindDeep(gameObject, "Txt_Got");  if (go != null) _result = go.GetComponent<PixelDigits>();
        go = UIPanelInput.FindDeep(gameObject, "Txt_Goal"); if (go != null) _resultTotal = go.GetComponent<PixelDigits>();
        if (_result == null) LogPrinter.LogWarning("[UI_Panel_Clear] Txt_Got(PixelDigits) 없음");
        return true;
    }

    /// <param name="tiles">3라운드 동안 얻은 타일 수 합계 (StageManager.TotalPlayerTiles)</param>
    /// <param name="target">목표. 0 이하면 "/목표" 를 숨긴다 (지금 게임에 목표 값이 없어 0 으로 넘어온다)</param>
    public void SetInfo(int tiles, int target, Action onConfirmed)
    {
        Init();
        _onConfirmed = onConfirmed;
        _openedFrame = Time.frameCount;
        _acceptInputAt = Time.unscaledTime + INPUT_DELAY;

        // 깃발 꽂힌 행성 = 방금 클리어한 Stage 의 행성
        StagePlanet.Apply(_planet, _planetSprites, StagePlanet.Current(), "CLEAR 행성");

        if (_result != null) _result.Text = tiles.ToString();
        if (_resultTotal != null)
        {
            // 목표가 없으면(0 이하) "/목표" 부분은 숨김
            _resultTotal.gameObject.SetActive(target > 0);
            if (target > 0) _resultTotal.Text = "/" + target;
        }
    }

    private void Update()
    {
        if (_onConfirmed == null || Time.frameCount == _openedFrame)
            return;
        // 라운드가 끝나는 순간 이동 키를 누르고 있었으면 CLEAR 가 바로 넘어가 버리므로 잠깐 입력을 받지 않는다
        if (Time.unscaledTime < _acceptInputAt)
            return;

        if (UIPanelInput.AnyKeyPressed())
        {
            Action callback = _onConfirmed;
            _onConfirmed = null;
            if (Managers.UI.ClosePopupUI(this))
                callback.Invoke();
        }
    }

    /// <summary>버튼 없이 닫아야 할 때. 콜백을 부르지 않는다.</summary>
    public void Dismiss()
    {
        _onConfirmed = null;
        if (gameObject.activeSelf)
            Managers.UI.ClosePopupUI(this);
    }
}
