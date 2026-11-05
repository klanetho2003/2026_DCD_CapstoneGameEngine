using UnityEngine;

/// <summary>
/// UI_Panel_* 공용 입력/검색 도우미.
/// 구 Input / 새 Input System 둘 다에서 컴파일되도록 분기.
/// </summary>
public static class UIPanelInput
{
    private static int _escConsumedFrame = -1;

    /// <summary>이번 프레임에 ESC 가 눌렸고 아직 아무도 안 썼으면 true (한 프레임에 한 화면만 반응)</summary>
    public static bool ConsumeEsc()
    {
        if (_escConsumedFrame == Time.frameCount)
            return false;

#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        bool pressed = kb != null && kb.escapeKey.wasPressedThisFrame;
#else
        bool pressed = Input.GetKeyDown(KeyCode.Escape);
#endif
        if (pressed)
            _escConsumedFrame = Time.frameCount;
        return pressed;
    }

    /// <summary>아무 키 또는 마우스 왼쪽 클릭</summary>
    public static bool AnyKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        var ms = UnityEngine.InputSystem.Mouse.current;
        return (kb != null && kb.anyKey.wasPressedThisFrame) || (ms != null && ms.leftButton.wasPressedThisFrame);
#else
        return Input.anyKeyDown;
#endif
    }

    /// <summary>이름으로 자식 찾기 (꺼진 것 포함, 깊이 상관없이)</summary>
    public static GameObject FindDeep(GameObject root, string name)
    {
        if (root == null) return null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t.gameObject;
        return null;
    }
}
