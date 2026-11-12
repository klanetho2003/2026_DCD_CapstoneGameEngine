using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using static Define;

/// <summary>
/// 라운드 시작 VS 화면 (Popup). 3초 뒤 스스로 닫히고 콜백을 부른다.
///   Managers.UI.ShowPopupUI&lt;UI_Panel_StageStart&gt;().SetInfo(round, onFinished);
/// </summary>
public class UI_Panel_StageStart : UI_Popup
{
    private enum Images
    {
        Img_RoundNum,
        Img_Planet,
    }

    [SerializeField] private float _showSeconds = 3f;
    [Tooltip("text_round_num_1, 2, 3 순서 (ERound A, B, C)")]
    [SerializeField] private Sprite[] _roundNumSprites;
    [Tooltip("고양이 아래 큰 행성: planet_*_big (수성, 금성, 지구, 화성, 목성, 토성 = StageCatalog 순서)")]
    [SerializeField] private Sprite[] _planetSprites;

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

        // 고양이 아래 행성 = 지금 Stage 의 행성
        StagePlanet.Apply(GetImage(Images.Img_Planet), _planetSprites, StagePlanet.Current(), "시작 화면 행성");

        // 제한 시간 = 인게임 라운드 시간 (라운드가 막 시작된 시점이라 남은 시간 = 전체 시간)
        GameObject timeLimit = UIPanelInput.FindDeep(gameObject, "Txt_TimeLimit");
        if (timeLimit != null && timeLimit.TryGetComponent(out PixelDigits digits))
        {
            float seconds = Managers.Stage != null ? Managers.Stage.RoundRemainingSeconds : 0f;
            if (seconds <= 0f)
                seconds = ROUND_DURATION_SECONDS;
            digits.Text = Mathf.CeilToInt(seconds) + "s";
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
