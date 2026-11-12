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

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        // UI Bind
        BindButtons(typeof(Buttons));

        GetButton(Buttons.Btn_Play).BindEvent((evt) => { OnClickStartButton(); }, UIEvent.Click);

        Button settings = GetButton(Buttons.Btn_Settings);
        if (settings != null)
            settings.BindEvent((evt) => { Managers.UI.ShowPopupUI<UI_Panel_Settings>(); }, UIEvent.Click);

        Button quit = GetButton(Buttons.Btn_Quit);
        if (quit != null)
            quit.BindEvent((evt) => { Application.Quit(); }, UIEvent.Click);

        // Loading 시작
        StartLoadAssets();

        return true;
    }

    void StartLoadAssets()
    {
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
        if (_isCompeleteLoad == false)
            return;

        Managers.Scene.LoadAsyncScene(EScene.GameScene, () => CoroutineFade.FadeIn(), true);
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
