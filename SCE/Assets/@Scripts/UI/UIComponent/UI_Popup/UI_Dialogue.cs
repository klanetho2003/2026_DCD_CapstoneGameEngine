using DG.Tweening;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using static Define;


public class UI_Dialogue : UI_Base
{
    #region UI Bind
    private enum Texts
    {
        DialogueText,
    }
    private enum Images
    {
        BackGround,
    }
    #endregion

    private enum ETextState
    {
        None,           // 대기 상태
        Typing,         // 텍스트 타이핑 애니메이션 중
        WaitingForNext  // 타이핑 완료 후 다음 텍스트 입력을 기다리는 상태
    }

    private ETextState _textState = ETextState.None;
    private Tween _typingTween;

    private float SECOND_PER_CHAR = 0.2f;

    // 임시 일렬 텍스트 처리를 위한 리스트와 인덱스
    private List<string> _tempDialogueSequence;
    private int _tempDialogueIndex = 0;

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        BindTexts(typeof(Texts));
        BindImages(typeof(Images));

        GetImage(Images.BackGround).gameObject.BindEvent(OnHandleInput, UIEvent.Click);

        return true;
    }

    /// <summary>
    /// 외부에서 임시 List<string>을 주입하여 연속 대화를 시작하는 진입점
    /// </summary>
    public void StartTempTextSequence(List<string> dialogueList)
    {
        if (dialogueList == null || dialogueList.Count == 0)
            return;

        _tempDialogueSequence = dialogueList;
        _tempDialogueIndex = 0;

        PlayNextTempDialogue();
    }

    private void PlayNextTempDialogue()
    {
        // 1. 리스트의 끝에 도달했는지 확인
        if (_tempDialogueIndex >= _tempDialogueSequence.Count)
        {
            EndTempTextSequence();
            return;
        }

        string nextText = _tempDialogueSequence[_tempDialogueIndex];

        // 2. 텍스트 세팅 및 초기화
        var dialogueTMP = GetText(Texts.DialogueText);
        dialogueTMP.text = nextText;
        dialogueTMP.maxVisibleCharacters = 0;
        _textState = ETextState.Typing;

        // 3. TMP 강제 업데이트 (Rich Text 및 글자 수 계산을 위해 필수)
        dialogueTMP.ForceMeshUpdate();
        int totalVisibleChars = dialogueTMP.textInfo.characterCount;

        // 4. Tween 시간 계산 (글자 수 * 글자당 속도)
        float duration = totalVisibleChars * SECOND_PER_CHAR;

        // 기존 진행 중인 Tween이 있다면 제거
        _typingTween?.Kill();

        // [SOUND: 대사 타이핑 시작 사운드 재생 호출 위치]

        // 5. DOTween을 이용한 타이핑 애니메이션
        _typingTween = DOTween.To(
            () => dialogueTMP.maxVisibleCharacters,
            x =>
            {
                dialogueTMP.maxVisibleCharacters = x;
                // [SOUND: 필요시 한 글자 타이핑될 때마다 틱(Tick) 사운드 재생 호출 위치]
            },
            totalVisibleChars,
            duration
        )
        .SetEase(Ease.Linear) // 일정한 속도로 타이핑되도록 Linear 설정
        .SetLink(gameObject)  // UI가 파괴될 때 Tween도 자동 메모리 해제 (안전장치)
        .OnComplete(OnTypingCompleted); // 완료 콜백
    }

    private void OnTypingCompleted()
    {
        _textState = ETextState.WaitingForNext;
        GetText(Texts.DialogueText).maxVisibleCharacters = 99999; // 안전하게 전부 노출되도록 세팅

        // [SOUND: 대사 타이핑 종료 사운드 재생 호출 위치]

        // 다음 대사로 넘어갈 수 있다는 화살표 깜빡임 UI 활성화
        //SetActiveNexrDialogueAlert(true);
    }

    private void EndTempTextSequence()
    {
        _textState = ETextState.None;
        _tempDialogueSequence = null;

        // 대화 종료 처리 (ex. 팝업 닫기)
        // DisableDialogueSelf();
    }


    #region Input
    /// <summary>
    /// 기존 OnClickDialogue 또는 터치 이벤트에서 호출되는 핵심 Input 제어부
    /// </summary>
    public void OnHandleInput(PointerEventData evt)
    {
        switch (_textState)
        {
            case ETextState.Typing:
                // 애니메이션 진행 중 클릭 -> 애니메이션 즉시 취소 및 텍스트 전체 출력
                _typingTween?.Kill();
                OnTypingCompleted();
                break;

            case ETextState.WaitingForNext:
                // 애니메이션 완료 상태에서 클릭 -> 다음 텍스트로 이동
                _tempDialogueIndex++;
                //SetActiveNexrDialogueAlert(false);
                PlayNextTempDialogue();
                break;

            case ETextState.None:
                // 시퀀스가 진행 중이 아닐 때의 예외 처리
                break;
        }
    }
    #endregion
}