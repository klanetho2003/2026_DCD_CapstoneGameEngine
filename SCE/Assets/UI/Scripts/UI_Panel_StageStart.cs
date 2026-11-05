using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using static Define;

/// <summary>
/// 라운드 시작 VS 화면 (Popup). 2초 뒤 스스로 닫히고 콜백을 부른다.
///   Managers.UI.ShowPopupUI&lt;UI_Panel_StageStart&gt;().SetInfo(round, onFinished);
/// </summary>
public class UI_Panel_StageStart : UI_Popup
{
    private enum Images
    {
        Img_RoundNum,
    }

    [SerializeField] private float _showSeconds = 2f;
    [Tooltip("text_round_num_1, 2, 3 순서 (ERound A, B, C)")]
    [SerializeField] private Sprite[] _roundNumSprites;

    private Action _onFinished;

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        BindImages(typeof(Images));
        return true;
    }

    public void SetInfo(ERound round, Action onFinished)
    {
        Init();
        _onFinished = onFinished;

        int index = (int)round;
        Image image = GetImage(Images.Img_RoundNum);
        if (image != null && _roundNumSprites != null && index >= 0 && index < _roundNumSprites.Length && _roundNumSprites[index] != null)
        {
            image.sprite = _roundNumSprites[index];
            image.SetNativeSize();
        }

        StopAllCoroutines();
        StartCoroutine(CoClose());
    }

    private IEnumerator CoClose()
    {
        yield return new WaitForSecondsRealtime(_showSeconds);

        Action callback = _onFinished;
        _onFinished = null;
        Managers.UI.ClosePopupUI(this);
        callback?.Invoke();
    }
}
