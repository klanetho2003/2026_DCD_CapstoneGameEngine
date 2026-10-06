using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using static Define;

/// <summary>
/// 상태 키 표 — 한 행에 키 · 기본값 · 초기화 · 주기 · 설명.
/// 값 편집과 드래그 재정렬은 SerializedObject 바인딩에 맡기고 (Undo 자동),
/// 추가·복제·삭제는 SerializedProperty 배열 조작 + ApplyModifiedProperties로 처리한다 (역시 Undo 자동).
/// 키 이름만은 바인딩하지 않는다 — 이름을 바꾸면 그 키를 쓰는 규칙도 함께 바꿀지 창이 물어야 하기 때문.
/// </summary>
public sealed class StateKeyTableView : VisualElement
{
    public const string KeysPath = "Set.Keys";

    private const float HandleWidth = 20f;    // Animated 재정렬 손잡이 자리 (헤더 정렬용)
    private const float StatusWidth = 18f;
    private const float KeyWidth = 200f;
    private const float NumberWidth = 64f;
    private const float ResetWidth = 120f;
    private const float DuplicateWidth = 44f;
    private const float RemoveWidth = 24f;
    private const long DeferMs = 50;          // 바인딩이 늘어난 배열을 반영할 때까지 기다리는 시간

    private static readonly Color ErrorTint = new Color(1f, 0.3f, 0.3f, 0.16f);
    private static readonly Color WarningTint = new Color(1f, 0.8f, 0.3f, 0.12f);
    private static readonly Color ErrorColor = new Color(1f, 0.45f, 0.45f);
    private static readonly Color WarningColor = new Color(1f, 0.82f, 0.4f);

    private struct IssueMark
    {
        public bool IsError;
        public string Text;
    }

    private readonly SerializedObject _serializedObject;
    private readonly SerializedProperty _keys;
    private readonly Func<string, string> _makeUniqueKey;       // 모든 파일 기준 고유 이름 (창이 제공)
    private readonly Action<int, string, string> _renameKey;    // [5-A] (행 순번, 예전 이름, 새 이름) — 창이 참조까지 처리
    private readonly Action<string> _keySelected;               // [5-A] 행 선택 → 키 사용처 표시
    private readonly ListView _list;
    private readonly Dictionary<int, IssueMark> _marks = new Dictionary<int, IssueMark>(); // 키 순번 → 검사 결과

    public StateKeyTableView(SerializedObject serializedObject, Func<string, string> makeUniqueKey,
                             Action<int, string, string> renameKey, Action<string> keySelected)
    {
        _serializedObject = serializedObject;
        _keys = serializedObject.FindProperty(KeysPath);
        _makeUniqueKey = makeUniqueKey;
        _renameKey = renameKey;
        _keySelected = keySelected;

        style.flexGrow = 1;
        Add(BuildHeader());

        _list = new ListView
        {
            bindingPath = KeysPath,
            fixedItemHeight = 24,
            virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
            reorderable = true,
            reorderMode = ListViewReorderMode.Animated,
            showBoundCollectionSize = false,
            showAddRemoveFooter = false,
            selectionType = SelectionType.Single,
            makeItem = () => new Row(this),
            bindItem = BindRow,
            unbindItem = UnbindRow,
        };
        _list.selectionChanged += _ => NotifySelection(); // [5-A]
        _list.style.flexGrow = 1;
        Add(_list);

        this.Bind(serializedObject);
    }

    private SerializedProperty KeyNameAt(int index)
    {
        return _keys.GetArrayElementAtIndex(index).FindPropertyRelative(nameof(StateKeyDefinition.Key));
    }

    #region 구조 변경 (추가·복제·삭제)
    public void AddKey()
    {
        string name = _makeUniqueKey("new.key"); // 배열을 바꾸기 전에 계산 (지금 이름들 기준)

        _serializedObject.Update();
        int index = _keys.arraySize;
        _keys.InsertArrayElementAtIndex(index); // 마지막 원소가 복사되므로 아래에서 전부 덮어쓴다
        SerializedProperty item = _keys.GetArrayElementAtIndex(index);
        item.FindPropertyRelative(nameof(StateKeyDefinition.Key)).stringValue = name;
        item.FindPropertyRelative(nameof(StateKeyDefinition.Default)).intValue = 0;
        item.FindPropertyRelative(nameof(StateKeyDefinition.Reset)).enumValueIndex = (int)EStateResetPolicy.None;
        item.FindPropertyRelative(nameof(StateKeyDefinition.Interval)).intValue = 0;
        item.FindPropertyRelative(nameof(StateKeyDefinition.Description)).stringValue = "";
        _serializedObject.ApplyModifiedProperties();
        Undo.SetCurrentGroupName("상태 키 추가");

        SelectLater(index, focusKey: true);
    }

    private void DuplicateAt(int index)
    {
        _serializedObject.Update();
        if (index < 0 || index >= _keys.arraySize)
            return;

        string name = _makeUniqueKey(KeyNameAt(index).stringValue + ".copy");

        _keys.InsertArrayElementAtIndex(index); // index와 index+1이 같은 값이 된다 — 아래쪽(index+1)을 복사본으로 쓴다
        KeyNameAt(index + 1).stringValue = name;
        _serializedObject.ApplyModifiedProperties();
        Undo.SetCurrentGroupName("상태 키 복제");

        SelectLater(index + 1, focusKey: true);
    }

    private new void RemoveAt(int index)
    {
        _serializedObject.Update();
        if (index < 0 || index >= _keys.arraySize)
            return;

        _keys.DeleteArrayElementAtIndex(index);
        _serializedObject.ApplyModifiedProperties();
        Undo.SetCurrentGroupName("상태 키 삭제");
        _list.ClearSelection();
    }

    /// <summary>검증 목록·키 사용처에서 이동할 때. 표를 막 만든 직후일 수 있어 한 박자 늦춘다.</summary>
    public void ScrollToKey(int index)
    {
        SelectLater(index, focusKey: false);
    }

    private void SelectLater(int index, bool focusKey)
    {
        _list.schedule.Execute(() =>
        {
            if (_list.itemsSource == null || index < 0 || index >= _list.itemsSource.Count)
                return;

            _list.ScrollToItem(index);
            _list.SetSelection(index);

            if (focusKey) // 행이 화면에 만들어진 뒤 포커스
                _list.schedule.Execute(() => _list.GetRootElementForIndex(index)?.Q<Row>()?.Key.Focus()).ExecuteLater(DeferMs);
        }).ExecuteLater(DeferMs);
    }
    #endregion

    #region 이름 변경 · 선택 [5-A]
    /// <summary>이름 칸에서 Enter·포커스 이동으로 확정했을 때. 실제 변경(선언 + 규칙 참조 + Undo)은 창이 한다.</summary>
    private void OnKeyCommitted(int index, string newName)
    {
        _serializedObject.Update();
        if (index < 0 || index >= _keys.arraySize)
            return;

        string oldName = KeyNameAt(index).stringValue;
        newName = newName ?? "";
        if (oldName != newName)
            _renameKey?.Invoke(index, oldName, newName);
    }

    private void NotifySelection()
    {
        int index = _list.selectedIndex;
        if (index < 0)
            return;

        _serializedObject.Update();
        if (index < _keys.arraySize)
            _keySelected?.Invoke(KeyNameAt(index).stringValue);
    }
    #endregion

    #region 검사 결과 표시
    /// <summary>창이 검사 후 호출한다. 이 파일(setIndex)의 키 구역 결과만 행에 표시하며, 에러가 경고보다 우선한다.</summary>
    public void SetIssues(IReadOnlyList<StateDefinitionIssue> issues, int setIndex)
    {
        _marks.Clear();
        for (int i = 0; i < issues.Count; i++)
        {
            StateDefinitionIssue issue = issues[i];
            if (issue.SetIndex != setIndex || issue.Section != EStateDefinitionSection.Key)
                continue;

            if (_marks.TryGetValue(issue.ItemIndex, out IssueMark mark))
            {
                mark.IsError |= issue.IsError;
                mark.Text += "\n" + issue.Text;
                _marks[issue.ItemIndex] = mark;
            }
            else
            {
                _marks.Add(issue.ItemIndex, new IssueMark { IsError = issue.IsError, Text = issue.Text });
            }
        }
        RefreshRowStates();
    }

    /// <summary>
    /// 화면에 보이는 행만 다시 칠한다. RefreshItems로 다시 바인딩하지 않는 이유: 입력 중인 칸의 포커스가 흔들릴 수 있어서.
    /// Undo·이름 변경 취소 뒤의 이름 칸 글자와 주기 칸 활성 상태도 여기서 맞춘다.
    /// </summary>
    public void RefreshRowStates()
    {
        _serializedObject.Update();
        _list.Query<Row>().ForEach(row =>
        {
            if (row.Index >= 0 && row.Index < _keys.arraySize)
                ApplyRowState(row, _keys.GetArrayElementAtIndex(row.Index));
        });
    }
    #endregion

    #region 행
    private sealed class Row : VisualElement
    {
        public readonly Label Status;
        public readonly TextField Key;
        public readonly IntegerField Default;
        public readonly EnumField Reset;
        public readonly IntegerField Interval;
        public readonly TextField Description;
        public int Index = -1;

        public Row(StateKeyTableView table)
        {
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.flexGrow = 1;

            Status = new Label();
            Status.style.width = StatusWidth;
            Status.style.flexShrink = 0;
            Status.style.unityTextAlign = TextAnchor.MiddleCenter;
            Add(Status);

            // isDelayed: Enter나 포커스 이동 때 한 번만 확정된다
            // [5-A] 이름 칸은 바인딩하지 않고, 확정되면 창의 이름 변경 절차로 보낸다
            Key = new TextField { isDelayed = true };
            Key.RegisterValueChangedCallback(e => table.OnKeyCommitted(Index, e.newValue));
            Add(Fixed(Key, KeyWidth));

            Default = new IntegerField { isDelayed = true };
            Add(Fixed(Default, NumberWidth));

            Reset = new EnumField(EStateResetPolicy.None);
            Add(Fixed(Reset, ResetWidth));

            Interval = new IntegerField { isDelayed = true };
            Add(Fixed(Interval, NumberWidth));

            Description = new TextField { isDelayed = true };
            Description.style.flexGrow = 1;
            Description.style.flexShrink = 1;
            Add(Description);

            // 클릭 처리는 행을 만들 때 한 번만 등록하고, 실행 시점의 Index를 읽는다 (재사용 행에 핸들러가 쌓이지 않게)
            Add(Fixed(new Button(() => table.DuplicateAt(Index)) { text = "복제", tooltip = "아래에 복사본 추가" }, DuplicateWidth));
            Add(Fixed(new Button(() => table.RemoveAt(Index)) { text = "×", tooltip = "삭제 (Ctrl+Z로 되돌리기)" }, RemoveWidth));

            // 초기화 정책을 바꾸면 주기 칸을 바로 켜고 끈다
            Reset.RegisterValueChangedCallback(e => Interval.SetEnabled(IsEveryNDays(e.newValue)));
        }
    }

    private void BindRow(VisualElement element, int index)
    {
        var row = (Row)element;
        row.Index = index;
        if (index >= _keys.arraySize)
            return;

        SerializedProperty item = _keys.GetArrayElementAtIndex(index);
        row.Default.BindProperty(item.FindPropertyRelative(nameof(StateKeyDefinition.Default)));
        row.Reset.BindProperty(item.FindPropertyRelative(nameof(StateKeyDefinition.Reset)));
        row.Interval.BindProperty(item.FindPropertyRelative(nameof(StateKeyDefinition.Interval)));
        row.Description.BindProperty(item.FindPropertyRelative(nameof(StateKeyDefinition.Description)));

        ApplyRowState(row, item);
    }

    private static void UnbindRow(VisualElement element, int index)
    {
        var row = (Row)element;
        row.Default.Unbind();
        row.Reset.Unbind();
        row.Interval.Unbind();
        row.Description.Unbind();
        row.Index = -1;
    }

    private void ApplyRowState(Row row, SerializedProperty item)
    {
        // [5-A] 이름 칸은 직접 맞춘다. 값이 다를 때만 넣는다 — 같을 때 넣으면 입력 중인 글자가 지워진다
        string key = item.FindPropertyRelative(nameof(StateKeyDefinition.Key)).stringValue;
        if (row.Key.value != key)
            row.Key.SetValueWithoutNotify(key);

        int reset = item.FindPropertyRelative(nameof(StateKeyDefinition.Reset)).enumValueIndex;
        row.Interval.SetEnabled(reset == (int)EStateResetPolicy.EveryNDays);

        if (_marks.TryGetValue(row.Index, out IssueMark mark))
        {
            row.Status.text = mark.IsError ? "✕" : "⚠";
            row.Status.style.color = mark.IsError ? ErrorColor : WarningColor;
            row.style.backgroundColor = mark.IsError ? ErrorTint : WarningTint;
            row.tooltip = mark.Text;
        }
        else
        {
            row.Status.text = "";
            row.style.backgroundColor = StyleKeyword.Null;
            row.tooltip = "";
        }
    }
    #endregion

    #region 헤더·도우미
    private static VisualElement BuildHeader()
    {
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.paddingTop = 2;
        header.style.paddingBottom = 2;
        header.style.borderBottomWidth = 1;
        header.style.borderBottomColor = new Color(0f, 0f, 0f, 0.35f);

        header.Add(Spacer(HandleWidth + StatusWidth));
        header.Add(HeaderLabel("키 (key)", KeyWidth, "영문·숫자·'.'·'_'·'-'만. 모든 파일을 통틀어 고유해야 한다. 이름을 바꾸면 이 키를 쓰는 규칙도 함께 바꿀지 묻는다"));
        header.Add(HeaderLabel("기본값", NumberWidth, "default — 새 게임 시작과 주기 초기화 때 돌아가는 값"));
        header.Add(HeaderLabel("초기화 (reset)", ResetWidth, "초기화 안 함 / 매일 / N일마다"));
        header.Add(HeaderLabel("주기 (일)", NumberWidth, "interval — N일마다에서만 사용, 1 이상"));

        Label description = HeaderLabel("설명 (desc)", 0f, "기획 메모. 게임 동작에는 영향 없음");
        description.style.width = StyleKeyword.Auto;
        description.style.flexGrow = 1;
        header.Add(description);

        header.Add(Spacer(DuplicateWidth + RemoveWidth));
        return header;
    }

    private static Label HeaderLabel(string text, float width, string tooltip)
    {
        var label = new Label(text) { tooltip = tooltip };
        label.style.width = width;
        label.style.flexShrink = 0;
        label.style.paddingLeft = 3;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        return label;
    }

    private static VisualElement Spacer(float width)
    {
        var spacer = new VisualElement();
        spacer.style.width = width;
        spacer.style.flexShrink = 0;
        return spacer;
    }

    private static T Fixed<T>(T element, float width) where T : VisualElement
    {
        element.style.width = width;
        element.style.flexShrink = 0;
        return element;
    }

    private static bool IsEveryNDays(Enum value)
    {
        return value is EStateResetPolicy policy && policy == EStateResetPolicy.EveryNDays;
    }
    #endregion
}