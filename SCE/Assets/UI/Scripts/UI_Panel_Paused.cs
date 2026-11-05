using System;
using UnityEngine;
using static Define;

/// <summary>
/// ESC 일시정지 (Popup) → 계속하기 / 설정 / 스테이지 선택.
/// 열려 있는 동안 Time.timeScale = 0.
/// "스테이지 선택"에서 할 일은 StageSelectRequested 에 연결 (게임 쪽).
/// </summary>
public class UI_Panel_Paused : UI_Popup
{
    private enum GameObjects
    {
        Btn_Resume,
        Btn_Settings,
        Btn_StageSelect,
    }

    /// <summary>"스테이지 선택"을 눌렀을 때 (Popup 은 이미 닫힌 뒤 불림)</summary>
    public static Action StageSelectRequested;

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        BindObjects(typeof(GameObjects));

        GameObject resume = GetObject(GameObjects.Btn_Resume);
        if (resume != null) BindEvent(resume, (evt) => Resume());

        GameObject settings = GetObject(GameObjects.Btn_Settings);
        if (settings != null) BindEvent(settings, (evt) => Managers.UI.ShowPopupUI<UI_Panel_Settings>());

        GameObject stageSelect = GetObject(GameObjects.Btn_StageSelect);
        if (stageSelect != null) BindEvent(stageSelect, (evt) => OnClickStageSelect());

        return true;
    }

    private void OnEnable()
    {
        Time.timeScale = 0f;
    }

    private void OnDisable()
    {
        Time.timeScale = 1f;
    }

    private void Update()
    {
        // 설정창이 위에 떠 있으면 ESC 는 설정창이 처리
        if (UI_Panel_Settings.IsOpen)
            return;

        if (UIPanelInput.ConsumeEsc())
            Resume();
    }

    private void Resume()
    {
        Managers.UI.ClosePopupUI(this);
    }

    private void OnClickStageSelect()
    {
        if (Managers.UI.ClosePopupUI(this))
            StageSelectRequested?.Invoke();
    }
}
