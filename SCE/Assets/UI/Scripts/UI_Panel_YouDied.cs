using System;
using UnityEngine;
using static Define;

/// <summary>
/// YOU DIED (Popup) → 다시 도전하기 / 스테이지 선택. 누르면 닫히고 해당 콜백.
///   Managers.UI.ShowPopupUI&lt;UI_Panel_YouDied&gt;().SetInfo(onRetry, onStageSelect);
/// </summary>
public class UI_Panel_YouDied : UI_Popup
{
    private enum GameObjects
    {
        Btn_Retry,
        Btn_StageSelect,
    }

    private Action _onRetry;
    private Action _onStageSelect;

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        BindObjects(typeof(GameObjects));

        GameObject retry = GetObject(GameObjects.Btn_Retry);
        if (retry != null) BindEvent(retry, (evt) => Choose(_onRetry));

        GameObject select = GetObject(GameObjects.Btn_StageSelect);
        if (select != null) BindEvent(select, (evt) => Choose(_onStageSelect));

        // 마우스를 올린 메뉴만 흰 글씨 + 화살표 (타이틀·일시정지와 같은 방식)
        _menu = new UIMenuSelection(retry, select);
        _menu.BindHover();

        return true;
    }

    private UIMenuSelection _menu;

    public void SetInfo(Action onRetry, Action onStageSelect)
    {
        Init();
        _onRetry = onRetry;
        _onStageSelect = onStageSelect;
        _menu?.Select(0); // 열 때마다 "다시 도전하기"부터
    }

    private void Choose(Action callback)
    {
        _onRetry = null;
        _onStageSelect = null;
        if (Managers.UI.ClosePopupUI(this))
            callback?.Invoke();
    }

    /// <summary>버튼 없이 닫아야 할 때. 콜백을 부르지 않는다.</summary>
    public void Dismiss()
    {
        _onRetry = null;
        _onStageSelect = null;
        if (gameObject.activeSelf)
            Managers.UI.ClosePopupUI(this);
    }
}
