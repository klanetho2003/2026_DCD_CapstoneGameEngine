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
            {
                try
                {
                    // (Sheet) Data Init
                    Managers.Data.InitData();

                    // (DB) Data Init
                    //Managers.Game.InitGame();

                    // Resource가 필요한 Manager Init
                    // _ = Managers.Analytics.InitializeAsync();

                    OnCompeleteLoad();
                }
                catch (System.Exception e)
                {
                    LogPrinter.LogError(this, e.ToString()/*$"[무시된 로딩 에러] 초기화 중 예외가 발생했지만 강제로 통과합니다.\n{e}"*/);
                }
                finally
                {
                    Managers.Data.IsDataLoaded = true;
                }
            }
        });
    }

    private void OnCompeleteLoad()
    {
        LogPrinter.Log("<color=Cyan> Load Compeleted </color>");

        _isCompeleteLoad = true;

        GetText(Texts.TitleText).text = _titleText;
    }

    private void OnClickBackGround()
    {
        if (_isCompeleteLoad == false)
            return;

        Managers.Scene.LoadAsyncScene(EScene.GameScene, () => CoroutineFade.FadeIn(), true);
    }
}
