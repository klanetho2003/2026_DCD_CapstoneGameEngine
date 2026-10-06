using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 검색 가능한 선택 팝업 (공용). 상태 키 선택에 쓰고, Interaction 노드 선택도 이것으로 바꾼다.
/// 키보드: 검색 칸에서 ↑↓ 이동, Enter 선택, Esc 닫기.  마우스: 항목을 한 번 누르면 선택.
/// 선택은 Enter·클릭에서만 한다 — selectionChanged에서 선택하면 화살표로 움직이기만 해도 선택되고 닫힌다.
/// </summary>
public sealed class EditorSearchPicker : EditorWindow
{
    public readonly struct Item
    {
        public readonly string Title;
        public readonly string Subtitle;
        public readonly object Payload;

        public Item(string title, string subtitle, object payload)
        {
            Title = title ?? "";
            Subtitle = subtitle ?? "";
            Payload = payload;
        }
    }

    private const float Width = 380f;
    private const float Height = 320f;
    private const long DeferMs = 50;

    // ShowAsDropDown 전에 채운다 (CreateGUI가 그 뒤에 불린다)
    private string _header;
    private IReadOnlyList<Item> _items;
    private object _current;
    private Action<Item> _onPick;

    private readonly List<Item> _filtered = new List<Item>();
    private ListView _list;
    private Label _empty;
    private string _search = "";

    /// <param name="current">지금 값 — 목록에서 미리 선택해 둔다 (없으면 null)</param>
    public static void Show(Rect activatorScreenRect, string header, IReadOnlyList<Item> items, object current, Action<Item> onPick)
    {
        var window = CreateInstance<EditorSearchPicker>();
        window._header = header;
        window._items = items;
        window._current = current;
        window._onPick = onPick;
        window.ShowAsDropDown(activatorScreenRect, new Vector2(Mathf.Max(Width, activatorScreenRect.width), Height));
    }

    private void CreateGUI()
    {
        if (_items == null) // 열린 채로 스크립트가 컴파일되어 내용을 잃은 경우
        {
            Close();
            return;
        }

        VisualElement root = rootVisualElement;

        if (string.IsNullOrEmpty(_header) == false)
        {
            var header = new Label(_header);
            header.style.paddingLeft = 6;
            header.style.paddingTop = 4;
            header.style.paddingBottom = 2;
            header.style.opacity = 0.7f;
            header.style.whiteSpace = WhiteSpace.Normal;
            root.Add(header);
        }

        var search = new ToolbarSearchField();
        search.RegisterValueChangedCallback(e =>
        {
            _search = e.newValue ?? "";
            Refresh();
        });
        // TrickleDown: 검색 칸(TextField)보다 먼저 화살표·Enter·Esc를 가로챈다
        search.RegisterCallback<KeyDownEvent>(OnSearchKeyDown, TrickleDown.TrickleDown);
        root.Add(search);

        _list = new ListView
        {
            itemsSource = _filtered,
            fixedItemHeight = 36,
            selectionType = SelectionType.Single,
            makeItem = MakeRow,
            bindItem = BindRow,
        };
        _list.itemsChosen += _ => PickSelected(); // 목록에 포커스가 있을 때 Enter·더블클릭
        _list.style.flexGrow = 1;
        root.Add(_list);

        _empty = new Label("결과 없음");
        _empty.style.paddingLeft = 8;
        _empty.style.paddingTop = 6;
        _empty.style.opacity = 0.6f;
        root.Add(_empty);

        Refresh();
        SelectCurrent();
        root.schedule.Execute(() => search.Q("unity-text-input")?.Focus());
    }

    private void OnDisable()
    {
        _onPick = null;
    }

    #region 목록
    private VisualElement MakeRow()
    {
        var row = new VisualElement();
        row.style.paddingLeft = 6;
        row.style.paddingTop = 2;
        row.Add(new Label { name = "title" });

        var subtitle = new Label { name = "subtitle" };
        subtitle.style.opacity = 0.55f;
        subtitle.style.fontSize = 10;
        subtitle.style.whiteSpace = WhiteSpace.NoWrap;
        subtitle.style.overflow = Overflow.Hidden;
        subtitle.style.textOverflow = TextOverflow.Ellipsis;
        row.Add(subtitle);

        // 한 번 클릭으로 선택. 등록은 행을 만들 때 한 번만 하고, 순번은 클릭 시점에 읽는다
        row.RegisterCallback<ClickEvent>(_ =>
        {
            if (row.userData is int index)
                PickIndex(index);
        });
        return row;
    }

    private void BindRow(VisualElement element, int index)
    {
        Item item = _filtered[index];
        element.userData = index;
        element.Q<Label>("title").text = item.Title;
        element.Q<Label>("subtitle").text = item.Subtitle;
    }

    private void Refresh()
    {
        _filtered.Clear();
        for (int i = 0; i < _items.Count; i++)
        {
            Item item = _items[i];
            if (_search.Length == 0
                || item.Title.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                || item.Subtitle.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _filtered.Add(item);
            }
        }

        if (_list == null)
            return;

        _list.RefreshItems();
        // 검색 결과의 첫 항목을 미리 골라 둔다 — 바로 Enter를 누르면 그것이 선택된다
        if (_filtered.Count > 0)
            _list.SetSelectionWithoutNotify(new[] { 0 });
        else
            _list.ClearSelection();
        _empty.style.display = _filtered.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void SelectCurrent()
    {
        if (_current == null)
            return;

        for (int i = 0; i < _filtered.Count; i++)
        {
            if (Equals(_filtered[i].Payload, _current) == false)
                continue;

            int index = i;
            _list.SetSelectionWithoutNotify(new[] { index });
            _list.schedule.Execute(() => _list.ScrollToItem(index)).ExecuteLater(DeferMs); // 배치가 끝난 뒤 스크롤
            return;
        }
    }
    #endregion

    #region 키보드 · 선택
    private void OnSearchKeyDown(KeyDownEvent evt)
    {
        switch (evt.keyCode)
        {
            case KeyCode.DownArrow:
                MoveSelection(+1);
                evt.StopPropagation();
                break;
            case KeyCode.UpArrow:
                MoveSelection(-1);
                evt.StopPropagation();
                break;
            case KeyCode.Return:
            case KeyCode.KeypadEnter:
                PickSelected();
                evt.StopPropagation();
                break;
            case KeyCode.Escape:
                Close();
                evt.StopPropagation();
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (_filtered.Count == 0)
            return;

        int index = _list.selectedIndex < 0 ? 0 : Mathf.Clamp(_list.selectedIndex + delta, 0, _filtered.Count - 1);
        _list.SetSelectionWithoutNotify(new[] { index });
        _list.ScrollToItem(index);
    }

    private void PickSelected()
    {
        PickIndex(_list.selectedIndex >= 0 ? _list.selectedIndex : 0);
    }

    private void PickIndex(int index)
    {
        if (index < 0 || index >= _filtered.Count)
            return;

        Item picked = _filtered[index];
        Action<Item> callback = _onPick;
        _onPick = null;
        Close();
        callback?.Invoke(picked); // 닫은 뒤 호출 — 콜백이 다른 팝업을 열어도 안전
    }
    #endregion
}