using System.Collections.Generic;
using UnityEngine;
using static Define;

/// <summary>
/// 포커스 목록의 패드·키보드 조작. UI가 하나씩 소유한다.
/// 상속이 아니라 소유로 쓰는 이유: UI_Scene 계열과 UI_Popup 계열이 같은 로직을 써야 하는데 부모가 다르다.
/// 열려 있는 동안 InputManager의 UI 오버레이로 등록되어 gameplay 입력이 차단된다.
/// Cancel 입력은 소비만 한다 — 이 UI들은 버튼으로만 닫힌다.
/// </summary>
public sealed class UIFocusNavigator : IUIInputReceiver
{
    private const float NAVIGATE_THRESHOLD = 0.5f;

    private readonly List<UI_FocusItem> _items = new();
    private int _index = -1;
    private bool _horizontal;

    public bool IsOpen { get; private set; }

    EActionMap IInputReceiver.RequiredContext { get { return EActionMap.UI; } }

    /// <param name="horizontal">true면 좌우, false면 상하로 이동</param>
    public void Open(IReadOnlyList<UI_FocusItem> items, bool horizontal = false)
    {
        Close(); // 방어 — 이미 열려 있으면 정리 후 다시 연다

        _horizontal = horizontal;
        for (int i = 0; i < items.Count; i++)
            _items.Add(items[i]);

        IsOpen = true;
        SetFocus(_items.Count > 0 ? 0 : -1);
        Managers.Input.PushOverlay(this);
    }

    /// <summary>여러 번 불러도 안전.</summary>
    public void Close()
    {
        if (IsOpen == false)
            return;
        IsOpen = false;

        if (Managers.Input != null)
            Managers.Input.PopOverlay(this); // 이미 빠져 있으면 내부에서 아무 일도 하지 않는다

        SetFocus(-1);
        _items.Clear();
    }

    #region IUIInputReceiver
    void IUIInputReceiver.OnNavigate(Vector2 direction)
    {
        if (_items.Count == 0)
            return;

        // 주 축만 사용. 화면 위쪽(+y) = 목록의 이전 항목
        float raw = _horizontal ? direction.x : direction.y;
        if (Mathf.Abs(raw) < NAVIGATE_THRESHOLD)
            return;

        float axis = _horizontal ? raw : -raw;
        int step = axis > 0f ? 1 : -1;
        SetFocus((_index + step + _items.Count) % _items.Count); // 순환
    }

    void IUIInputReceiver.OnSubmit()
    {
        if (_index < 0 || _index >= _items.Count)
            return;

        _items[_index].Submit();
    }

    bool IUIInputReceiver.OnCancel()
    {
        return true; // 소비 — InputManager가 자동으로 Pop하지 않는다
    }
    #endregion

    private void SetFocus(int index)
    {
        if (_index >= 0 && _index < _items.Count && _items[_index] != null) // 씬 정리 중 파괴된 항목 방어
            _items[_index].SetFocused(false);

        _index = index;

        if (_index >= 0 && _index < _items.Count && _items[_index] != null)
            _items[_index].SetFocused(true);
    }
}