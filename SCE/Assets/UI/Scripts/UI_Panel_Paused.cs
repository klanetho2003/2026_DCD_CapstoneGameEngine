using System;
using UnityEngine;
using static Define;

/// <summary>
/// ESC 일시정지 (Popup) → 돌아가기 / 스테이지 재시작 / 스테이지 선택으로 돌아가기 / 설정.
/// 열려 있는 동안 Time.timeScale = 0.
/// 마우스를 올린 메뉴만 흰 글씨 + 화살표 (타이틀과 같은 UIMenuSelection).
/// "재시작", "스테이지 선택"에서 할 일은 RestartRequested / StageSelectRequested 에 연결 (게임 쪽).
/// </summary>
public class UI_Panel_Paused : UI_Popup
{
    private enum GameObjects
    {
        Btn_Resume,
        Btn_Restart,
        Btn_StageSelect,
        Btn_Settings,
    }

    /// <summary>"스테이지 재시작"을 눌렀을 때 (Popup 은 이미 닫힌 뒤 불림)</summary>
    public static Action RestartRequested;

    /// <summary>"스테이지 선택으로 돌아가기"를 눌렀을 때 (Popup 은 이미 닫힌 뒤 불림)</summary>
    public static Action StageSelectRequested;

    private UIMenuSelection _menu;

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        BindObjects(typeof(GameObjects));

        GameObject resume = GetObject(GameObjects.Btn_Resume);
        if (resume != null) BindEvent(resume, (evt) => Resume());

        GameObject restart = GetObject(GameObjects.Btn_Restart);
        if (restart != null) BindEvent(restart, (evt) => { UISound.Play(Sfx.UiSelect); CloseAndInvoke(RestartRequested); });

        GameObject stageSelect = GetObject(GameObjects.Btn_StageSelect);
        if (stageSelect != null) BindEvent(stageSelect, (evt) => { UISound.Play(Sfx.UiSelect); CloseAndInvoke(StageSelectRequested); });

        GameObject settings = GetObject(GameObjects.Btn_Settings);
        if (settings != null) BindEvent(settings, (evt) => { UISound.Play(Sfx.UiSelect); Managers.UI.ShowPopupUI<UI_Panel_Settings>(); });

        // 화면에 보이는 순서대로
        _menu = new UIMenuSelection(resume, restart, stageSelect, settings);
        _menu.BindHover();

        return true;
    }

    private void OnEnable()
    {
        Time.timeScale = 0f;
        _menu?.Select(0); // 열 때마다 "돌아가기"부터
        UISound.Play(Sfx.PauseOpen);
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
        UISound.Play(Sfx.UiBack);
        Managers.UI.ClosePopupUI(this);
    }

    private void CloseAndInvoke(Action action)
    {
        if (Managers.UI.ClosePopupUI(this))
            action?.Invoke();
    }
}
