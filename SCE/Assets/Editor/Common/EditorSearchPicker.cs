using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 검색 가능한 선택 팝업 (공용). 상태 키 · 신호 · 크리처 · Interaction 노드 선택이 쓴다.
/// 키보드: 검색 칸에서 ↑↓ 이동, Enter 선택, Esc 닫기.  마우스: 항목을 한 번 누르면 선택.
/// 선택은 Enter·클릭에서만 한다 — selectionChanged에서 선택하면 화살표로 움직이기만 해도 선택되고 닫힌다.
///
/// 고를 수 없는 항목 (Item.Disabled, R8b-2b): 목록에서 빼지 않고 흐리게 보인다 — 무엇이 있는지는 보이되 고를 수 없다.
///  - ↑↓와 처음 선택은 흐린 항목을 건너뛰고, 눌러도 선택되지 않는다. 이유는 오른쪽 표시(Badge)와 아래 설명 칸(Detail)에 쓴다.
///  - 키보드는 늘 검색 칸이 받는다 — 목록 자체의 ↑↓는 흐린 항목을 모르므로, 목록이 포커스를 가져가면 검색 칸으로 다시 옮긴다.
/// Badge · Detail · Disabled를 쓰지 않는 호출은 전과 같다 (설명 칸도 생기지 않는다).
/// </summary>
public sealed class EditorSearchPicker : EditorWindow
{
    /// <summary>오른쪽 표시(Badge)의 색 — 쓰임새로 고른다. 색 값은 EditorStatusColors.</summary>
    public enum EBadgeTone
    {
        Muted = 0, // 참고
        Warning,   // 고를 수 있지만 고른 뒤 확인할 것이 있다
    }

    public readonly struct Item
    {
        public readonly string Title;
        public readonly string Subtitle;
        public readonly object Payload;
        public readonly string Badge;       // 제목 오른쪽 짧은 표시. "" = 없음
        public readonly EBadgeTone BadgeTone;
        public readonly string Detail;      // 아래 설명 칸 — 마우스가 올라간 항목, 없으면 선택한 항목의 것. "" = 없음
        public readonly bool Disabled;      // 흐리게 보이고 고를 수 없다 (목록에는 남는다)

        public Item(string title, string subtitle, object payload,
                    string badge = null, EBadgeTone badgeTone = EBadgeTone.Muted, string detail = null, bool disabled = false)
        {
            Title = title ?? "";
            Subtitle = subtitle ?? "";
            Payload = payload;
            Badge = badge ?? "";
            BadgeTone = badgeTone;
            Detail = detail ?? "";
            Disabled = disabled;
        }
    }

    private const float Width = 380f;
    private const float WideWidth = 460f;    // 표시·설명이 있는 목록 — 제목과 표시가 한 줄에, 설명이 덜 접히게
    private const float Height = 320f;
    private const float DetailHeight = 100f; // 설명 칸 높이. 고정 — 내용 따라 높이가 바뀌면 마우스 아래 행이 움직인다
    private const float DisabledOpacity = 0.4f;
    private const long DeferMs = 50;

    // ShowAsDropDown 전에 채운다 (CreateGUI가 그 뒤에 불린다)
    private string _header;
    private IReadOnlyList<Item> _items;
    private object _current;
    private Action<Item> _onPick;
    private bool _hasDetail;

    private readonly List<Item> _filtered = new List<Item>();
    private ListView _list;
    private Label _empty;
    private Label _detail;              // 설명 칸 — Detail이 있는 목록에만
    private VisualElement _searchInput; // 키보드를 받는 검색 칸
    private string _search = "";
    private int _selected = -1;         // 선택한 항목 — 늘 고를 수 있는 항목이거나 -1
    private int _hovered = -1;          // 마우스가 올라간 항목 — 설명 칸이 먼저 보여 준다

    /// <param name="current">지금 값 — 목록에서 미리 선택해 둔다 (없으면 null. 흐린 항목이면 선택하지 않는다)</param>
    public static void Show(Rect activatorScreenRect, string header, IReadOnlyList<Item> items, object current, Action<Item> onPick)
    {
        bool hasBadge = false;
        bool hasDetail = false;
        for (int i = 0; i < items.Count; i++)
        {
            hasBadge |= items[i].Badge.Length > 0;
            hasDetail |= items[i].Detail.Length > 0;
        }

        var window = CreateInstance<EditorSearchPicker>();
        window._header = header;
        window._items = items;
        window._current = current;
        window._onPick = onPick;
        window._hasDetail = hasDetail;

        float width = Mathf.Max(hasBadge || hasDetail ? WideWidth : Width, activatorScreenRect.width);
        float height = hasDetail ? Height + DetailHeight : Height;
        window.ShowAsDropDown(activatorScreenRect, new Vector2(width, height));
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
        _searchInput = search.Q("unity-text-input");

        _list = new ListView
        {
            itemsSource = _filtered,
            fixedItemHeight = 36,
            selectionType = SelectionType.Single,
            makeItem = MakeRow,
            bindItem = BindRow,
        };
        _list.selectedIndicesChanged += OnListSelectionChanged;       // 마우스로 바뀐 선택 — 흐린 항목이면 직전 선택으로 바꾼다
        _list.RegisterCallback<FocusInEvent>(_ => FocusSearchLater()); // 키보드는 늘 검색 칸으로 (↑↓·Enter·Esc는 OnSearchKeyDown 한 곳)
        _list.RegisterCallback<PointerLeaveEvent>(_ => SetHovered(-1));
        _list.style.flexGrow = 1;
        root.Add(_list);

        _empty = new Label("결과 없음");
        _empty.style.paddingLeft = 8;
        _empty.style.paddingTop = 6;
        _empty.style.opacity = 0.6f;
        root.Add(_empty);

        if (_hasDetail)
        {
            _detail = new Label();
            _detail.style.height = DetailHeight;
            _detail.style.flexShrink = 0;
            _detail.style.paddingLeft = 6;
            _detail.style.paddingRight = 6;
            _detail.style.paddingTop = 4;
            _detail.style.fontSize = 11;
            _detail.style.whiteSpace = WhiteSpace.Normal;
            _detail.style.overflow = Overflow.Hidden;
            _detail.style.borderTopWidth = 1;
            _detail.style.borderTopColor = new Color(0f, 0f, 0f, 0.35f);
            root.Add(_detail);
        }

        Refresh();
        SelectCurrent();
        FocusSearchLater();
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
        row.style.paddingRight = 6;
        row.style.paddingTop = 2;

        var line = new VisualElement();
        line.style.flexDirection = FlexDirection.Row;
        line.style.alignItems = Align.Center;

        var title = new Label { name = "title" };
        title.style.flexGrow = 1;
        title.style.flexShrink = 1;
        title.style.whiteSpace = WhiteSpace.NoWrap;
        title.style.overflow = Overflow.Hidden;
        title.style.textOverflow = TextOverflow.Ellipsis;
        line.Add(title);

        var badge = new Label { name = "badge" };
        badge.style.flexShrink = 0;
        badge.style.marginLeft = 6;
        badge.style.fontSize = 10;
        line.Add(badge);
        row.Add(line);

        var subtitle = new Label { name = "subtitle" };
        subtitle.style.opacity = 0.55f;
        subtitle.style.fontSize = 10;
        subtitle.style.whiteSpace = WhiteSpace.NoWrap;
        subtitle.style.overflow = Overflow.Hidden;
        subtitle.style.textOverflow = TextOverflow.Ellipsis;
        row.Add(subtitle);

        // 등록은 행을 만들 때 한 번만 하고, 순번은 이벤트 시점에 읽는다 (행은 스크롤하며 다른 항목에 다시 쓰인다)
        row.RegisterCallback<ClickEvent>(_ =>
        {
            if (row.userData is int index)
                PickIndex(index); // 흐린 항목이면 PickIndex가 무시한다
        });
        row.RegisterCallback<PointerDownEvent>(evt =>
        {
            // 흐린 항목의 누름은 목록의 선택 처리(부모 쪽)까지 보내지 않는다 — 선택 표시가 흐린 항목으로 옮겨 가지 않게
            if (row.userData is int index && IsDisabled(index))
                evt.StopPropagation();
        });
        row.RegisterCallback<PointerMoveEvent>(_ =>
        {
            if (row.userData is int index && index != _hovered)
                SetHovered(index);
        });
        return row;
    }

    private void BindRow(VisualElement element, int index)
    {
        Item item = _filtered[index];
        element.userData = index;
        element.style.opacity = item.Disabled ? DisabledOpacity : 1f;
        element.Q<Label>("title").text = item.Title;
        element.Q<Label>("subtitle").text = item.Subtitle;

        Label badge = element.Q<Label>("badge");
        badge.text = item.Badge;
        badge.style.display = item.Badge.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        badge.style.color = item.BadgeTone == EBadgeTone.Warning ? EditorStatusColors.Warning : EditorStatusColors.Status;
    }

    private void Refresh()
    {
        _filtered.Clear();
        for (int i = 0; i < _items.Count; i++)
        {
            Item item = _items[i];
            if (EditorSearch.Matches(item.Title, _search) || EditorSearch.Matches(item.Subtitle, _search))
            {
                _filtered.Add(item);
            }
        }

        if (_list == null)
            return;

        _hovered = -1;
        _list.RefreshItems();
        // 검색 결과에서 고를 수 있는 첫 항목을 미리 골라 둔다 — 바로 Enter를 누르면 그것이 선택된다
        int first = FirstEnabled(_filtered);
        Select(first);
        if (first >= 0)
            ScrollToLater(first);
        _empty.style.display = _filtered.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    /// <summary>지금 값을 미리 선택한다. 흐린 항목이면 선택하지 않는다 (Refresh가 고른 첫 항목이 남는다).</summary>
    private void SelectCurrent()
    {
        if (_current == null)
            return;

        for (int i = 0; i < _filtered.Count; i++)
        {
            if (Equals(_filtered[i].Payload, _current) == false)
                continue;

            if (_filtered[i].Disabled == false)
            {
                Select(i);
                ScrollToLater(i); // 배치가 끝난 뒤 스크롤
            }
            return;
        }
    }

    /// <summary>알림 없이 선택을 옮긴다. 고를 수 없는 순번(흐린 항목 · 범위 밖 · -1)이면 선택을 비운다.</summary>
    private void Select(int index)
    {
        if (index >= 0 && index < _filtered.Count && _filtered[index].Disabled == false)
        {
            _selected = index;
            _list.SetSelectionWithoutNotify(new[] { index });
        }
        else
        {
            _selected = -1;
            _list.SetSelectionWithoutNotify(Array.Empty<int>());
        }
        UpdateDetail();
    }

    private void ScrollToLater(int index)
    {
        _list.schedule.Execute(() =>
        {
            if (index < _filtered.Count)
                _list.ScrollToItem(index);
        }).ExecuteLater(DeferMs);
    }

    /// <summary>
    /// 마우스로 바뀐 선택 (알림 없는 선택은 오지 않는다). 흐린 항목이 선택되었으면 직전 선택으로 바꾼다 —
    /// 보통은 MakeRow가 누름을 막지만, 목록이 다른 단계에서 선택하더라도 흐린 항목이 선택된 채 남지 않게 한다.
    /// </summary>
    private void OnListSelectionChanged(IEnumerable<int> indices)
    {
        int index = _list.selectedIndex;
        if (IsDisabled(index))
        {
            Select(_selected);
            return;
        }
        _selected = index;
        UpdateDetail();
    }

    private bool IsDisabled(int index)
    {
        return index >= 0 && index < _filtered.Count && _filtered[index].Disabled;
    }
    #endregion

    #region 설명 칸
    private void SetHovered(int index)
    {
        _hovered = index;
        UpdateDetail();
    }

    /// <summary>설명 칸: 마우스가 올라간 항목, 없으면 선택한 항목의 Detail.</summary>
    private void UpdateDetail()
    {
        if (_detail == null)
            return;

        int index = _hovered >= 0 && _hovered < _filtered.Count ? _hovered : _selected;
        _detail.text = index >= 0 && index < _filtered.Count ? _filtered[index].Detail : "";
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

    private void MoveSelection(int step)
    {
        int next = _selected < 0 ? FirstEnabled(_filtered) : NextEnabled(_filtered, _selected, step);
        if (next < 0)
            return; // 끝 — 그 자리에 머문다 (흐린 항목만 남았어도)

        Select(next);
        _list.ScrollToItem(next);
    }

    private void PickSelected()
    {
        PickIndex(_selected >= 0 ? _selected : FirstEnabled(_filtered));
    }

    private void PickIndex(int index)
    {
        if (index < 0 || index >= _filtered.Count || _filtered[index].Disabled)
            return; // 흐린 항목 — 이유는 표시와 설명 칸에 있다

        Item picked = _filtered[index];
        Action<Item> callback = _onPick;
        _onPick = null;
        Close();
        callback?.Invoke(picked); // 닫은 뒤 호출 — 콜백이 다른 팝업을 열어도 안전
    }

    private void FocusSearchLater()
    {
        rootVisualElement.schedule.Execute(() => _searchInput?.Focus());
    }
    #endregion

    #region 고를 수 있는 항목 찾기 (순수 — 테스트가 부른다)
    /// <summary>고를 수 있는 첫 항목. 없으면 -1. O(n)</summary>
    public static int FirstEnabled(IReadOnlyList<Item> items)
    {
        return NextEnabled(items, -1, 1);
    }

    /// <summary>from에서 step(+1 · -1) 방향으로 고를 수 있는 다음 항목. 끝까지 없으면 -1 — 반대쪽 끝으로 넘어가지 않는다. O(n)</summary>
    public static int NextEnabled(IReadOnlyList<Item> items, int from, int step)
    {
        if (step == 0)
            return -1;

        for (int i = from + step; i >= 0 && i < items.Count; i += step)
        {
            if (items[i].Disabled == false)
                return i;
        }
        return -1;
    }
    #endregion
}