using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Define;

public class SceneManagerEx
{
    public BaseScene CurrentScene { get { return GameObject.FindFirstObjectByType<BaseScene>(); } }
    public EScene CurrentSceneType { get; private set; } = EScene.TitleScene;

    public void LoadScene(EScene type)
    {
        // Managers.SetDefault();
        Clear();
        SceneManager.LoadScene(GetSceneName(type));
        CurrentSceneType = type;
    }

    public void LoadAsyncScene(EScene type, Action loadSceneComplete, bool doFadeEffect = false)
    {
        if (doFadeEffect)
        {
            CoroutineFade.FadeOut(() =>
            {
                Managers.Instance.StartCoroutine(LoadingAsyncScene(type, loadSceneComplete));
            });
        }
        else
        {
            Managers.Instance.StartCoroutine(LoadingAsyncScene(type, loadSceneComplete));
        }
    }

    private IEnumerator LoadingAsyncScene(EScene type, Action loadSceneComplete)
    {
        // Managers.SetDefault();
        Clear();

        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(GetSceneName(type));
        while (!loadOperation.isDone)
        {
            // 로딩 진행률 UI 등에 활용 가능 (loadOperation.progress)
            yield return null;
        }

        CurrentSceneType = type;
        loadSceneComplete?.Invoke();
    }

    private string GetSceneName(EScene type)
    {
        string name = System.Enum.GetName(typeof(EScene), type);
        return name;
    }

    public void Clear()
    {
        CurrentScene.Clear();
        Managers.Pool.Clear();
        Managers.UI.Clear();
        Managers.Interaction.Clear();
    }
}
