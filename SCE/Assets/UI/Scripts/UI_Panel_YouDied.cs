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

        return true;
    }

    public void SetInfo(Action onRetry, Action onStageSelect)
    {
        Init();
        _onRetry = onRetry;
        _onStageSelect = onStageSelect;
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
