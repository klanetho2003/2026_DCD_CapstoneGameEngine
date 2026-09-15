using System;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using static Define;

public class UI_FocusGroup : UI_Popup, IUIInputReceiver
{
    #region Bind UI
    private enum FocusItems
    {
        FocusItems_1,
        FocusItems_2,
        FocusItems_3,
        FocusItems_4,
        FocusItems_5,
    }
    #endregion

    private List<UI_FocusItem> _items = new();
    private bool _horizontal = false;
    private bool _closeOnCancel = true;

    public event Action Closed;

    private int _index = -1;

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        Bind<UI_FocusItem>(typeof(FocusItems));

        _items.Add(Get<UI_FocusItem>(FocusItems.FocusItems_1));
        _items.Add(Get<UI_FocusItem>(FocusItems.FocusItems_2));
        _items.Add(Get<UI_FocusItem>(FocusItems.FocusItems_3));
        _items.Add(Get<UI_FocusItem>(FocusItems.FocusItems_4));
        _items.Add(Get<UI_FocusItem>(FocusItems.FocusItems_5));

        return true;
    }

    EActionMap IInputReceiver.RequiredContext { get { return EActionMap.UI; } }

    public void SetInfo()
    {
        OnOpened();
    }

    /// <summary>UI를 열 때 호출</summary>
    private void OnOpened()
    {
        SetFocus(0);
        Managers.Input.PushOverlay(this);
    }

    /// <summary>UI를 닫을 때 호출. Cancel 경로에서는 InputManager가 이미 Pop한 뒤 호출된다.</summary>
    public void OnClosed()
    {
        Managers.Input.PopOverlay(this); // 이미 제거됐으면 내부에서 no-op
        SetFocus(-1);
        Closed?.Invoke();
    }

    #region IUIInputReceiver
    void IUIInputReceiver.OnNavigate(Vector2 direction)
    {
        if (_items == null || _items.Count == 0)
            return;

        // 주 축만 사용 (대각선 입력 시 큰 쪽 채택)
        float axis = _horizontal ? direction.x : -direction.y; // 화면 위쪽(+y) = 목록 이전 항목
        if (Mathf.Abs(_horizontal ? direction.x : direction.y) < 0.5f)
            return;

        int step = axis > 0f ? 1 : -1;
        int next = (_index + step + _items.Count) % _items.Count; // 순환
        SetFocus(next);
    }

    void IUIInputReceiver.OnSubmit()
    {
        if (_index < 0 || _index >= _items.Count)
            return;

        _items[_index].Submit();
    }

    bool IUIInputReceiver.OnCancel()
    {
        if (_closeOnCancel == false)
            return true; // 소비 → 자동 Pop 방지 (닫히면 안 되는 UI)

        return false;    // 미소비 → InputManager가 Pop, 이어서 Close()
    }
    #endregion

    private void SetFocus(int index)
    {
        if (_index >= 0 && _index < _items.Count)
            _items[_index].SetFocused(false);

        _index = index;

        if (_index >= 0 && _index < _items.Count)
            _items[_index].SetFocused(true);
    }

    // Cancel로 Pop된 경우 Close()를 이어서 호출해 주기 위한 훅
    private void OnDisable()
    {
        if (Managers.Input != null && Managers.Input.HasOverlay)
            Managers.Input.PopOverlay(this);
    }
}
