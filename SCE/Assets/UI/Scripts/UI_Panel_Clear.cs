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

    private PixelDigits _result;
    private PixelDigits _resultTotal;

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        GameObject go;
        go = UIPanelInput.FindDeep(gameObject, "Txt_Result");      if (go != null) _result = go.GetComponent<PixelDigits>();
        go = UIPanelInput.FindDeep(gameObject, "Txt_ResultTotal"); if (go != null) _resultTotal = go.GetComponent<PixelDigits>();
        return true;
    }

    public void SetInfo(int tiles, int target, Action onConfirmed)
    {
        Init();
        _onConfirmed = onConfirmed;
        _openedFrame = Time.frameCount;

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
