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
        Managers.Resource.Clear();
        Managers.Data.Clear();

        Managers.Resource.LoadAllAsync<UnityEngine.Object>("PreLoad", (key, count, totalCount) =>
        {
            LogPrinter.Log($"{key} {count}/{totalCount}");

            if (!Managers.Data.IsDataLoaded && count == totalCount)
                OnPreLoadCompleted();
        });
    }

    /// <summary>
    /// PreLoad 리소스를 모두 로드하면 불린다. 시트 데이터를 초기화한 뒤 저장 경로에서 진행 파일을 찾아 불러온다 (없으면 SaveManager가 처음 상태로 새로 만든다).
    /// 진행 데이터는 상태 정의(InitData)를 읽은 뒤에만 입힐 수 있어 이 순서를 지키고, 어느 단계든 실패하면 시작 화면에서 넘어가지 않는다.
    /// </summary>
    private void OnPreLoadCompleted()
    {
        try
        {
            // (Sheet) Data Init
            Managers.Data.InitData();

            // (DB) Data Init
            //Managers.Game.InitGame();
        }
        catch (System.Exception e)
        {
            // 상태 정의가 없으면 진행 데이터를 입힐 수 없어 불러오지 않는다 (저장 파일은 그대로 남는다)
            LogPrinter.LogError(this, $"데이터 초기화 실패 — 진행 데이터를 불러오지 않음\n{e}"/*$"[무시된 로딩 에러] 초기화 중 예외가 발생했지만 강제로 통과합니다.\n{e}"*/);
            return;
        }
        finally
        {
            Managers.Data.IsDataLoaded = true;
        }

        try
        {
            // (Save) 저장 경로에서 진행 파일을 찾아 불러오고 없으면 새로 만든다
            Managers.Save.Load(ESaveFile.Progress);

            // Resource가 필요한 Manager Init
            // _ = Managers.Analytics.InitializeAsync();

            OnCompeleteLoad();
        }
        catch (System.Exception e)
        {
            LogPrinter.LogError(this, e.ToString());
        }
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
