using System;
using UnityEngine;
using UnityEngine.InputSystem;
using static LogPrinter;

/// <summary>
/// New Input System의 키 리바인딩.
/// 키 설정은 지금 저장하지 않는다 — 설정 파일에 등록된 구역이 없어 SaveBindings · LoadBindings는 아무것도 쓰고 읽지 않으며,
/// 바꾼 키는 이번 실행 동안만 유지된다. 설정 파일에 키 설정 구역이 생기면 두 함수가 그대로 쓰고 읽는다.
/// 
/// 사용 흐름:
/// 1. 옵션 메뉴 >> StartRebinding(Action, callback) >> 사용자 키 입력 대기
/// 2. 새 키 누름 >> 자동 SaveBindings (지금은 저장하지 않음)
/// </summary>
public class InputRebindingManager
{
    private InputActionAsset _actionAsset;
    private InputActionRebindingExtensions.RebindingOperation _currentOperation;

    public bool IsRebinding => _currentOperation != null;

    public void Init()
    {
        _actionAsset = Managers.Input.ActionAsset.asset;
        if (_actionAsset == null)
        {
            LogError("[InputRebindingManager] InputActionAsset이 null입니다.");
            return;
        }
    }

    /// <summary>
    /// 키 리바인딩 시작. 사용자가 키를 누르면 onComplete(true), 취소되면 onComplete(false).
    /// </summary>
    public void StartRebinding(InputAction action, int bindingIndex, Action<bool> onComplete)
    {
        if (action == null)
        {
            onComplete?.Invoke(false);
            return;
        }

        // 진행 중 rebinding 취소
        if (_currentOperation != null)
        {
            _currentOperation.Cancel();
            _currentOperation = null;
        }

        // 리바인딩 중에는 해당 action 임시 비활성
        action.Disable();

        _currentOperation = action.PerformInteractiveRebinding(bindingIndex)
            .WithControlsExcluding("Mouse")   // 마우스는 제외
            .OnCancel(op =>
            {
                action.Enable();
                op.Dispose();
                _currentOperation = null;
                onComplete?.Invoke(false);
            })
            .OnComplete(op =>
            {
                action.Enable();
                op.Dispose();
                _currentOperation = null;
                SaveBindings();
                onComplete?.Invoke(true);
            })
            .Start();
    }

    public void CancelCurrentRebinding()
    {
        _currentOperation?.Cancel();
    }

    public void SaveBindings()
    {
        if (_actionAsset == null)
            return;

        Managers.Save.Save(ESaveFile.Settings);
    }

    public void LoadBindings()
    {
        if (_actionAsset == null)
            return;

        Managers.Save.Load(ESaveFile.Settings);
    }

    /// <summary>
    /// 모든 binding을 .inputactions 파일의 기본값으로 리셋.
    /// </summary>
    public void ResetBindings()
    {
        if (_actionAsset == null)
            return;

        _actionAsset.RemoveAllBindingOverrides();

        // 기본값으로 되돌린 것을 설정 파일에 남긴다 (설정 구역이 없으면 아무것도 쓰지 않는다)
        Managers.Save.Save(ESaveFile.Settings);
    }
}