using System;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 확인 버튼 하나로 닫히는 Popup의 공통 동작.
/// 규칙: 버튼(클릭 또는 Submit)을 누르면 Popup이 먼저 닫히고, 그 다음 콜백이 1회 불린다.
///       Cancel 입력으로는 닫히지 않는다.
/// 프리팹 규약: 하위에 Button_Confirm(Button + Image).
/// </summary>
public abstract class UI_ConfirmPopup : UI_Popup
{
    private enum ConfirmButtons
    {
        Button_Confirm,
    }

    private readonly UIFocusNavigator _navigator = new UIFocusNavigator();
    private readonly List<UI_FocusItem> _focusItems = new(1);
    private Action _onConfirmed; // null이 아니면 "열려 있고 아직 확인 전"

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        BindButtons(typeof(ConfirmButtons));

        Button confirm = GetButton(ConfirmButtons.Button_Confirm);
        if (confirm == null)
        {
            LogPrinter.LogError($"[{name}] Button_Confirm 없음 — 프리팹의 하위 오브젝트 이름 확인");
            return true;
        }

        BindEvent(confirm, OnConfirmClicked); // 마우스 클릭

        // 패드·키보드용 포커스 항목은 코드에서 붙인다 — 프리팹에는 Button만 있으면 된다
        UI_FocusItem focusItem = Util.GetOrAddComponent<UI_FocusItem>(confirm.gameObject);
        focusItem.Submitted += Confirm;
        _focusItems.Add(focusItem);

        return true;
    }

    /// <summary>파생 클래스의 SetInfo가 내용을 채운 뒤 호출한다.</summary>
    protected void Open(Action onConfirmed)
    {
        _onConfirmed = onConfirmed;
        _navigator.Open(_focusItems);
    }

    /// <summary>버튼을 누르지 않았는데 닫아야 할 때 (상태가 먼저 바뀐 경우). 콜백을 부르지 않는다.</summary>
    public void Dismiss()
    {
        if (gameObject.activeSelf)
            Managers.UI.ClosePopupUI(this);
    }

    private void OnConfirmClicked(PointerEventData _)
    {
        Confirm();
    }

    private void Confirm()
    {
        Action callback = _onConfirmed;
        if (callback == null)
            return; // 이미 처리됨 (연타)

        // 순서: 닫기 → 통지. 닫지 못했으면(맨 위 Popup이 아님) 진행하지 않는다
        if (Managers.UI.ClosePopupUI(this) == false)
            return;

        callback.Invoke();
    }

    // ClosePopupUI의 SetActive(false), 밖에서의 비활성, 씬 정리 — 어느 경로로 꺼지든 여기서 정리된다
    private void OnDisable()
    {
        _navigator.Close();
        _onConfirmed = null;
    }
}