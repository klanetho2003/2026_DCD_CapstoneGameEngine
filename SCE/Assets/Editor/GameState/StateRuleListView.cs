using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using static Define;

/// <summary>
/// 규칙 탭 — 이벤트 필터 + 규칙 카드 목록.
/// 카드는 "Set.Rules.Array.data[i]" 경로에 묶여 있어서, 순서 이동·값 편집과 그 Undo는 바인딩이 내용만 바꿔 보여 준다.
/// 규칙 개수가 바뀔 때(추가·복제·삭제와 그 Undo)만 카드를 다시 만들고, 이때 스크롤 위치를 유지한다.
/// </summary>
public sealed class StateRuleListView : VisualElement
{
    public const string RulesPath = "Set.Rules";
    private const long DeferMs = 50;

    // 필터는 파일·탭을 바꿔도 유지된다 (에디터를 켜 둔 동안)
    private static EGameEventType s_filter = EGameEventType.None;
    private static readonly List<EGameEventType> FilterChoices = new List<EGameEventType>
    {
        EGameEventType.None, EGameEventType.DayAdvanced, EGameEventType.CreatureDied, EGameEventType.Signal,
    };

    private readonly SerializedObject _so;
    private readonly Func<bool, List<EditorSearchPicker.Item>> _buildKeyItems; // 인자: 값 변경용(시스템 키 제외)인지
    private readonly Func<int, string> _describeSignalSenders;
    private readonly PopupField<EGameEventType> _filterField;
    private readonly ScrollView _scroll;
    private readonly List<StateRuleCardView> _cards = new List<StateRuleCardView>();

    private IReadOnlyList<StateDefinitionIssue> _issues;
    private int _setIndex = -1;

    public StateRuleListView(SerializedObject so, Func<bool, List<EditorSearchPicker.Item>> buildKeyItems, Func<int, string> describeSignalSenders)
    {
        _so = so;
        _buildKeyItems = buildKeyItems;
        _describeSignalSenders = describeSignalSenders; // 아래 Rebuild()가 카드를 만들며 쓰므로 그보다 먼저 대입
        style.flexGrow = 1;

        var bar = new VisualElement();
        bar.style.flexDirection = FlexDirection.Row;
        bar.style.alignItems = Align.Center;
        bar.style.paddingLeft = 6;
        bar.style.paddingRight = 6;
        bar.style.paddingTop = 2;
        bar.style.paddingBottom = 2;

        _filterField = new PopupField<EGameEventType>("이벤트", FilterChoices, s_filter, FormatFilter, FormatFilter);
        _filterField.labelElement.style.minWidth = 40;
        _filterField.labelElement.style.width = 40;
        _filterField.style.width = 180;
        _filterField.RegisterValueChangedCallback(e =>
        {
            s_filter = e.newValue;
            RefreshCardStates();
        });
        bar.Add(_filterField);

        var hint = new Label("같은 이벤트의 규칙은 위에서 아래 순서로 실행 — 앞 규칙이 바꾼 값을 뒤 규칙의 선행 조건이 본다");
        hint.style.flexGrow = 1;
        hint.style.marginLeft = 8;
        hint.style.opacity = 0.6f;
        hint.style.whiteSpace = WhiteSpace.Normal;
        bar.Add(hint);

        Button addButton = null;
        addButton = new Button(() => ShowAddRuleMenu(addButton)) { text = "+ 규칙", tooltip = "이벤트를 골라 맨 아래에 추가" };
        bar.Add(addButton);
        Add(bar);

        _scroll = new ScrollView(ScrollViewMode.Vertical);
        _scroll.style.flexGrow = 1;
        Add(_scroll);

        Rebuild();
        this.TrackSerializedObjectValue(so, _ => OnAnyChange());
    }

    private SerializedProperty Rules()
    {
        return _so.FindProperty(RulesPath);
    }

    private static string EntriesPath(int ruleIndex, bool isOps)
    {
        string array = isOps ? nameof(StateRuleDefinition.Ops) : nameof(StateRuleDefinition.Require);
        return $"{RulesPath}.Array.data[{ruleIndex}].{array}";
    }

    private static string FormatFilter(EGameEventType value)
    {
        return value == EGameEventType.None ? "전체" : EditorEnumLabels.Of(value);
    }

    #region 카드 만들기 · 갱신
    private void Rebuild()
    {
        for (int i = 0; i < _cards.Count; i++)
            _cards[i].Unbind();
        _scroll.Clear();
        _cards.Clear();

        _so.Update();
        int count = Rules().arraySize;
        for (int i = 0; i < count; i++)
        {
            var card = new StateRuleCardView(_so, i, this);
            _cards.Add(card);
            _scroll.Add(card);
        }
        if (count == 0)
        {
            var empty = new Label("규칙이 없습니다 — [+ 규칙]으로 추가하세요");
            empty.style.paddingLeft = 8;
            empty.style.paddingTop = 8;
            empty.style.opacity = 0.6f;
            _scroll.Add(empty);
        }

        RefreshCardStates();
        ApplyIssues();
    }

    /// <summary>다시 만든 직후에는 스크롤이 맨 위로 가므로, 배치가 끝난 뒤 원래 자리(또는 지정한 카드)로 되돌린다.</summary>
    private void RebuildKeepScroll(int scrollToIndex)
    {
        Vector2 offset = _scroll.scrollOffset;
        Rebuild();
        _scroll.schedule.Execute(() =>
        {
            _scroll.scrollOffset = offset;
            if (scrollToIndex >= 0 && scrollToIndex < _cards.Count)
                _scroll.ScrollTo(_cards[scrollToIndex]);
        }).ExecuteLater(DeferMs);
    }

    /// <summary>값이 바뀔 때마다 (편집·Undo 포함). 개수가 달라졌으면 다시 만들고, 아니면 표시만 갱신한다.</summary>
    private void OnAnyChange()
    {
        if (panel == null)
            return;

        _so.Update();
        if (Rules().arraySize != _cards.Count)
            RebuildKeepScroll(-1);
        else
            RefreshCardStates();
    }

    /// <summary>순번("개체 사망 규칙 중 2번째"), 이벤트 표시, ruleKey 칸 활성, 필터 적용. O(규칙 수).</summary>
    private void RefreshCardStates()
    {
        _so.Update();
        var orderByEvent = new int[(int)EGameEventType.Count];
        for (int i = 0; i < _cards.Count; i++)
        {
            StateRuleCardView card = _cards[i];
            EGameEventType evt = card.ReadEvent();
            int order = (uint)evt < (uint)EGameEventType.Count ? ++orderByEvent[(int)evt] : 0;
            card.Refresh(order);

            bool visible = s_filter == EGameEventType.None || evt == s_filter;
            card.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void ScrollToCardLater(int index)
    {
        _scroll.schedule.Execute(() =>
        {
            if (index >= 0 && index < _cards.Count)
                _scroll.ScrollTo(_cards[index]);
        }).ExecuteLater(DeferMs);
    }
    #endregion

    #region 규칙 구조 변경 (카드 버튼이 호출)
    private void ShowAddRuleMenu(VisualElement activator)
    {
        var menu = new GenericMenu();
        for (int i = 1; i < FilterChoices.Count; i++) // 0번(전체)은 제외
        {
            EGameEventType evt = FilterChoices[i];
            menu.AddItem(new GUIContent(EditorEnumLabels.Of(evt)), false, () => AddRule(evt));
        }
        menu.DropDown(activator.worldBound);
    }

    private void AddRule(EGameEventType evt)
    {
        _so.Update();
        SerializedProperty rules = Rules();
        int index = rules.arraySize;
        rules.InsertArrayElementAtIndex(index); // 마지막 규칙이 복사되므로 아래에서 전부 덮어쓴다
        SerializedProperty rule = rules.GetArrayElementAtIndex(index);
        rule.FindPropertyRelative(nameof(StateRuleDefinition.Event)).intValue = (int)evt;
        rule.FindPropertyRelative(nameof(StateRuleDefinition.HasRuleKey)).boolValue = false;
        rule.FindPropertyRelative(nameof(StateRuleDefinition.RuleKeyValue)).intValue = 0;
        rule.FindPropertyRelative(nameof(StateRuleDefinition.Description)).stringValue = "";
        rule.FindPropertyRelative(nameof(StateRuleDefinition.Require)).ClearArray();
        rule.FindPropertyRelative(nameof(StateRuleDefinition.Ops)).ClearArray();
        _so.ApplyModifiedProperties();
        Undo.SetCurrentGroupName("규칙 추가");

        if (s_filter != EGameEventType.None && s_filter != evt) // 새 카드가 필터에 가려지지 않게
        {
            s_filter = evt;
            _filterField.SetValueWithoutNotify(evt);
        }
        RebuildKeepScroll(index);
    }

    internal void MoveRule(int index, int delta)
    {
        _so.Update();
        SerializedProperty rules = Rules();
        int target = index + delta;
        if (index < 0 || target < 0 || target >= rules.arraySize)
            return;

        rules.MoveArrayElement(index, target);
        _so.ApplyModifiedProperties();
        Undo.SetCurrentGroupName("규칙 순서 변경");

        RefreshCardStates();
        ScrollToCardLater(target); // 카드는 그대로, 내용이 자리를 바꾼다 — 옮겨진 규칙을 따라간다
    }

    internal void DuplicateRule(int index)
    {
        _so.Update();
        SerializedProperty rules = Rules();
        if (index < 0 || index >= rules.arraySize)
            return;

        rules.InsertArrayElementAtIndex(index); // index와 index+1이 같은 값 — 아래쪽이 복사본
        _so.ApplyModifiedProperties();
        Undo.SetCurrentGroupName("규칙 복제");
        RebuildKeepScroll(index + 1);
    }

    internal void DeleteRule(int index)
    {
        _so.Update();
        SerializedProperty rules = Rules();
        if (index < 0 || index >= rules.arraySize)
            return;

        rules.DeleteArrayElementAtIndex(index);
        _so.ApplyModifiedProperties();
        Undo.SetCurrentGroupName("규칙 삭제");
        RebuildKeepScroll(-1);
    }
    #endregion

    #region 선행 조건·값 변경 행 (카드 목록이 호출)
    internal void AddEntry(int ruleIndex, bool isOps, VisualElement activator)
    {
        PickKey(activator, isOps, null, key =>
        {
            _so.Update();
            SerializedProperty array = _so.FindProperty(EntriesPath(ruleIndex, isOps));
            if (array == null)
                return;

            int index = array.arraySize;
            array.InsertArrayElementAtIndex(index);
            SerializedProperty entry = array.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative(nameof(StateOpDefinition.Key)).stringValue = key;
            if (isOps)
                entry.FindPropertyRelative(nameof(StateOpDefinition.Op)).intValue = (int)EStateOp.Set;
            else
                entry.FindPropertyRelative(nameof(StateRequireDefinition.Comparison)).intValue = (int)EComparison.GreaterOrEqual;
            entry.FindPropertyRelative(nameof(StateOpDefinition.Value)).intValue = 1;
            _so.ApplyModifiedProperties();
            Undo.SetCurrentGroupName(isOps ? "값 변경 추가" : "선행 조건 추가");
        });
    }

    internal void ChangeEntryKey(int ruleIndex, bool isOps, int entryIndex, VisualElement activator)
    {
        _so.Update();
        SerializedProperty keyProperty = _so.FindProperty($"{EntriesPath(ruleIndex, isOps)}.Array.data[{entryIndex}].{nameof(StateOpDefinition.Key)}");
        if (keyProperty == null)
            return;

        string path = keyProperty.propertyPath;
        PickKey(activator, isOps, keyProperty.stringValue, key =>
        {
            _so.Update();
            SerializedProperty target = _so.FindProperty(path);
            if (target == null)
                return;
            target.stringValue = key;
            _so.ApplyModifiedProperties();
            Undo.SetCurrentGroupName("키 변경");
        });
    }

    internal void RemoveEntry(int ruleIndex, bool isOps, int entryIndex)
    {
        _so.Update();
        SerializedProperty array = _so.FindProperty(EntriesPath(ruleIndex, isOps));
        if (array == null || entryIndex < 0 || entryIndex >= array.arraySize)
            return;

        array.DeleteArrayElementAtIndex(entryIndex);
        _so.ApplyModifiedProperties();
        Undo.SetCurrentGroupName(isOps ? "값 변경 삭제" : "선행 조건 삭제");
    }

    private void PickKey(VisualElement activator, bool forWrite, string current, Action<string> onPick)
    {
        List<EditorSearchPicker.Item> items = _buildKeyItems(forWrite);
        string header = forWrite
            ? "바꿀 키 — 시스템 키(sys.)는 규칙으로 바꿀 수 없어 목록에 없습니다"
            : "조건으로 읽을 키";
        Rect rect = GUIUtility.GUIToScreenRect(activator.worldBound);

        EditorSearchPicker.Show(rect, header, items, current, item =>
        {
            if (panel == null) // 팝업이 떠 있는 사이 파일·탭이 바뀌었으면 무시
                return;
            onPick((string)item.Payload);
        });
    }
    #endregion

    #region 검사 결과 · 이동 (창이 호출)

    /// <summary>require/ops 행이 선택될 때 그 키 이름을 알린다 (창의 키 사용처 패널용).</summary>
    public Action<string> KeySelected;

    internal void NotifyEntrySelected(int ruleIndex, bool isOps, int entryIndex)
    {
        if (KeySelected == null)
            return;

        _so.Update();
        SerializedProperty key = _so.FindProperty($"{EntriesPath(ruleIndex, isOps)}.Array.data[{entryIndex}].{nameof(StateOpDefinition.Key)}");
        if (key != null)
            KeySelected(key.stringValue);
    }

    /// <summary>[6] 카드가 호출: 이 신호 ID를 내는 상호작용 문구. 없으면 null.</summary>
    internal string DescribeSignalSenders(int signalId)
    {
        return _describeSignalSenders != null ? _describeSignalSenders(signalId) : null;
    }

    /// <summary>[6] 창이 호출: 상호작용 파일을 다시 읽은 뒤, 카드의 "이 신호를 내는 상호작용" 표시를 맞춘다.</summary>
    public void RefreshExternalReferences()
    {
        RefreshCardStates();
    }

    public void SetIssues(IReadOnlyList<StateDefinitionIssue> issues, int setIndex)
    {
        _issues = issues;
        _setIndex = setIndex;
        ApplyIssues();
    }

    private void ApplyIssues()
    {
        for (int i = 0; i < _cards.Count; i++)
            _cards[i].ClearIssues();

        if (_issues != null)
        {
            for (int i = 0; i < _issues.Count; i++)
            {
                StateDefinitionIssue issue = _issues[i];
                if (issue.SetIndex != _setIndex || issue.ItemIndex < 0 || issue.ItemIndex >= _cards.Count)
                    continue;
                if (issue.Section == EStateDefinitionSection.Rule
                    || issue.Section == EStateDefinitionSection.Require
                    || issue.Section == EStateDefinitionSection.Op)
                {
                    _cards[issue.ItemIndex].AddIssue(issue);
                }
            }
        }

        for (int i = 0; i < _cards.Count; i++)
            _cards[i].ApplyIssueStyles();
    }

    /// <summary>검증 목록에서 이동. 필터에 가려진 카드면 전체 보기로 바꾼 뒤 이동하고, require·ops 행을 선택한다.</summary>
    public void ScrollToRule(int ruleIndex, EStateDefinitionSection section, int subIndex)
    {
        if (ruleIndex < 0 || ruleIndex >= _cards.Count)
            return;

        StateRuleCardView card = _cards[ruleIndex];
        if (card.style.display.value == DisplayStyle.None)
        {
            s_filter = EGameEventType.None;
            _filterField.SetValueWithoutNotify(s_filter);
            RefreshCardStates();
        }

        _scroll.schedule.Execute(() =>
        {
            _scroll.ScrollTo(card);
            card.Highlight(section, subIndex);
        }).ExecuteLater(DeferMs);
    }
    #endregion
}