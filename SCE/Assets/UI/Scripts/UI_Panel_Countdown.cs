using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using static Define;

/// <summary>
/// 3-2-1-GO! (Popup). 끝나면 스스로 닫히고 콜백(= 게임 시작)을 부른다.
///   Managers.UI.ShowPopupUI&lt;UI_Panel_Countdown&gt;().Play(onGo);
/// </summary>
public class UI_Panel_Countdown : UI_Popup
{
    private enum Images
    {
        Img_Count,
    }

    [Tooltip("count_3, count_2, count_1, text_GO 순서")]
    [SerializeField] private Sprite[] _sprites;
    [SerializeField] private float _stepSeconds = 1f;
    [SerializeField] private float _goSeconds = 0.5f;

    private Action _onFinished;

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        BindImages(typeof(Images));
        return true;
    }

    public void Play(Action onFinished)
    {
        Init();
        _onFinished = onFinished;
        StopAllCoroutines();
        StartCoroutine(CoCount());
    }

    private IEnumerator CoCount()
    {
        Image image = GetImage(Images.Img_Count);
        int count = _sprites == null ? 0 : _sprites.Length;

        for (int i = 0; i < count; i++)
        {
            if (image != null && _sprites[i] != null)
            {
                image.sprite = _sprites[i];
                image.SetNativeSize();
            }
            bool isGo = i == count - 1;
            yield return new WaitForSecondsRealtime(isGo ? _goSeconds : _stepSeconds);
        }

        Action callback = _onFinished;
        _onFinished = null;
        Managers.UI.ClosePopupUI(this);
        callback?.Invoke();
    }
}
