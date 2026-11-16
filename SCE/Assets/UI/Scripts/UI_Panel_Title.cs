using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static Define;

public class UI_Panel_Title : UI_Scene
{
    #region Bind UI
    private enum Buttons
    {
        Btn_Play,
        Btn_Settings,
        Btn_Quit,
    }
    #endregion

    private bool _isCompeleteLoad = false;

    private string _titleText { get { return _isCompeleteLoad ? "Title Scene" : "Title Scene (Unloaded)"; } }

    // 마우스를 올린 메뉴만 흰 글씨 + 화살표
    private UIMenuSelection _menu;

    // Title → 게임 전환 페이드 시간 (CoroutineFade 기본값 1.5초보다 짧게)
    private const float StartFadeDuration = 0.8f;
    private bool _isStarting = false;

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        // UI Bind
        BindButtons(typeof(Buttons));

        GetButton(Buttons.Btn_Play).BindEvent((evt) => { OnClickStartButton(); }, UIEvent.Click);

        Button settings = GetButton(Buttons.Btn_Settings);
        if (settings != null)
            settings.BindEvent((evt) => { OnClickSettingsButton(); }, UIEvent.Click);

        Button quit = GetButton(Buttons.Btn_Quit);
        if (quit != null)
            quit.BindEvent((evt) => { UISound.Play(Sfx.UiSelect); Application.Quit(); }, UIEvent.Click);

        _menu = new UIMenuSelection(
            GetButton(Buttons.Btn_Play)?.gameObject,
            settings?.gameObject,
            quit?.gameObject);
        _menu.BindHover();

        // Loading 시작
        StartLoadAssets();

        return true;
    }

    void StartLoadAssets()
    {
        // 게임에서 Back 으로 돌아온 경우: 이미 다 불러왔다.
        // LoadGameData 를 다시 부르면 IsDataLoaded 가 true 라 완료 콜백이 오지 않아 Play 가 막힌다
        if (Managers.Data.IsDataLoaded)
        {
            OnCompeleteLoad();
            return;
        }

        // 리소스 → 시트 데이터 → 진행 파일 순서는 Managers.LoadGameData가 맡는다
        Managers.LoadGameData(OnCompeleteLoad);
    }

    private void OnCompeleteLoad()
    {
        LogPrinter.Log("<color=Cyan> Load Compeleted </color>");

        _isCompeleteLoad = true;

        var ui = Managers.UI.ShowBaseUI<UI_Dialogue>();
        ui.StartTempTextSequence(new List<string> { "안녕하 나는 김성호야.", "반갑고", "잘 지내보자" });
    }

    private void OnClickStartButton()
    {
        if (_isCompeleteLoad == false || _isStarting)
            return;

        // 페이드 중에 다시 눌러도 처음부터 다시 시작하지 않게
        _isStarting = true;
        UISound.Play(Sfx.UiSelect);

        // LoadAsyncScene(…, true) 와 같은 순서: 어두워짐 → 씬 로드 → 밝아짐. 시간만 짧게
        CoroutineFade.FadeOut(() =>
        {
            Managers.Scene.LoadAsyncScene(EScene.GameScene, () => CoroutineFade.FadeIn(null, StartFadeDuration), false);
        }, StartFadeDuration);
    }

    // ESC = 설정창. 다른 Popup(설정창 포함)이 떠 있으면 그쪽이 ESC 를 쓴다
    private void Update()
    {
        if (_isCompeleteLoad == false || _isStarting)
            return;

        if (Managers.UI.GetPopupCount() == 0 && UIPanelInput.ConsumeEsc())
            OnClickSettingsButton();
    }

    // 설정창 프리팹은 PreLoad 로 불러오므로, 로드가 끝나기 전에는 열 수 없다
    private void OnClickSettingsButton()
    {
        if (_isCompeleteLoad == false || _isStarting)
            return;

        UISound.Play(Sfx.UiSelect);
        Managers.UI.ShowPopupUI<UI_Panel_Settings>();
    }
}

/*public class UI_PanelTitle : UI_Base
{
    #region 예시
    enum GameObjects
    {
        Btn_Play,
    }

    enum Buttons
    {
        Btn_Play,
    }

    enum Texts
    {
        Txt_Label,
    }
    

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        // Prefab에 있는 객체 연결하기
        BindObjects(typeof(GameObjects));
        BindButtons(typeof(Buttons));
        BindTexts(typeof(Texts));


        // 가져온 객체에 있는 값 수정
        GameObject go = GetObject(GameObjects.Btn_Play);
        Button button = GetButton(Buttons.Btn_Play);
        TMP_Text t = GetText(Texts.Txt_Label);



        button.BindEvent(OnClickTestButton, UIEvent.Click);


        return true;
    }

    void OnClickTestButton(PointerEventData evt)
    {
        Managers.Scene.LoadAsyncScene(EScene.GameScene, () => CoroutineFade.FadeIn(), true);
    }
    #endregion
}*/
