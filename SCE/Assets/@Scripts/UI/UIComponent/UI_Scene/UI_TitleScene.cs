using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using static Define;

public class UI_TitleScene : UI_Scene
{
    #region Bind UI
    private enum Texts
    {
        TitleText,
    }

    private enum Images
    {
        BackGround,
    }
    #endregion

    private bool _isCompeleteLoad = false;

    private string _titleText { get { return _isCompeleteLoad ? "Title Scene" : "Title Scene (Unloaded)"; } }

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        // UI Bind
        BindTexts(typeof(Texts));
        BindImages(typeof(Images));

        GetImage(Images.BackGround).gameObject.BindEvent((evt) => { OnClickBackGround(); }, UIEvent.Click);

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

        GetText(Texts.TitleText).text = _titleText;

        var ui = Managers.UI.ShowBaseUI<UI_Dialogue>();
        ui.StartTempTextSequence(new List<string> { "안녕하 나는 김성호야.", "반갑고", "잘 지내보자" });

    }

    private void OnClickBackGround()
    {
        if (_isCompeleteLoad == false)
            return;

        Managers.Scene.LoadAsyncScene(EScene.GameScene, () => CoroutineFade.FadeIn(), true);
    }
}
