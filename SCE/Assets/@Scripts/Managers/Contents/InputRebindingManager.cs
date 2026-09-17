using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using static LogPrinter;

/// <summary>
/// New Input System의 키 리바인딩 + 사용자 설정 영구 저장.
/// 
/// 사용 흐름:
/// 1. 게임 시작 >> Init >> LoadBindings (PlayerPrefs에서 복원)
/// 2. 옵션 메뉴 >> StartRebinding(Action, callback) >> 사용자 키 입력 대기
/// 3. 새 키 누름 >> 자동 SaveBindings >> PlayerPrefs 저장
/// 4. 이후 게임 실행 시 LoadBindings로 복원
/// </summary>
public class InputRebindingManager
{
    private string DirectoryPath => Path.Combine(Application.dataPath, "@Resources", "Data", "InputData");
    private string FilePath => Path.Combine(DirectoryPath, "InputBindings.json");

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

        LoadBindings();
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

        string json = _actionAsset.SaveBindingOverridesAsJson();

        // 1. 지정된 경로에 폴더가 없다면 생성
        if (Directory.Exists(DirectoryPath) == false)
            Directory.CreateDirectory(DirectoryPath);

        // 2. JSON 문자열을 파일로 저장
        File.WriteAllText(FilePath, json);
    }

    public void LoadBindings()
    {
        if (_actionAsset == null)
            return;

        // 파일이 존재하지 않으면 로드 패스
        if (File.Exists(FilePath) == false)
            return;

        // JSON 파일을 읽어와서 적용
        string json = File.ReadAllText(FilePath);
        _actionAsset.LoadBindingOverridesFromJson(json);
    }

    /// <summary>
    /// 모든 binding을 .inputactions 파일의 기본값으로 리셋.
    /// </summary>
    public void ResetBindings()
    {
        if (_actionAsset == null)
            return;

        _actionAsset.RemoveAllBindingOverrides();

        // 기본값으로 되돌렸으므로 기존 저장된 JSON 파일 삭제
        if (File.Exists(FilePath))
            File.Delete(FilePath);
    }
}