using System;
using UnityEngine;
using UnityEngine.InputSystem;
using static LogPrinter;

/// <summary>
/// New Input System의 키 리바인딩 + 사용자 설정 영구 저장.
/// 
/// 사용 흐름:
/// 1. 게임 시작 >> Managers.Init 끝에서 SaveManager가 설정 파일을 읽어 복원 (InputBindingSaveSection)
/// 2. 옵션 메뉴 >> StartRebinding(Action, callback) >> 사용자 키 입력 대기
/// 3. 새 키 누름 >> 자동 SaveBindings >> SaveManager가 설정 파일 저장
/// 4. 이후 게임 실행 시 1과 같이 복원
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

        // 기본값으로 되돌린 것을 설정 파일에 남긴다 (다른 설정은 그대로)
        Managers.Save.Save(ESaveFile.Settings);
    }
}