using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using static Define;

/// <summary>
/// 규칙 1개 = 카드 1장. Interaction Editor의 카드와 같은 구성 (머리글 + ▲▼·복제·삭제 + 두 목록).
/// 값 칸은 "Set.Rules.Array.data[i]" 경로에 바인딩한다 (Undo 자동).
/// 이벤트만 PopupField(개수용 Count를 뺀 목록)라 바인딩 대신 직접 읽고 쓰며, 표시는 목록(StateRuleListView)이 Refresh로 맞춘다.
/// </summary>
public sealed class StateRuleCardView : VisualElement
{
    private static readonly Color NormalBorder = new Color(0.35f, 0.6f, 0.9f);
    private static readonly Color ErrorColor = new Color(1f, 0.45f, 0.45f);
    private static readonly Color WarningColor = new Color(1f, 0.82f, 0.4f);
    private static readonly Color ErrorTint = new Color(1f, 0.3f, 0.3f, 0.16f);
    private static readonly Color WarningTint = new Color(1f, 0.8f, 0.3f, 0.12f);

    // 고를 수 있는 이벤트 — Count는 배열 크기용이라 뺀다. None은 "(선택 필요)"로 보이며 검사에서 에러
    private static readonly List<EGameEventType> EventChoices = new List<EGameEventType>
    {
        EGameEventType.None, EGameEventType.DayAdvanced, EGameEventType.CreatureDied, EGameEventType.Signal,
    };

    private struct IssueMark
    {
        public bool IsError;
        public string Text;
    }

    private readonly SerializedObject _so;
    private readonly StateRuleListView _owner;
    private readonly int _index;
    private readonly string _path; // Set.Rules.Array.data[i]

    private readonly Label _orderLabel;
    private readonly PopupField<EGameEventType> _eventField;
    private readonly IntegerField _ruleKeyValue;
    private readonly Label _ruleKeyHint;
    private readonly Label _senderLabel; // 신호 규칙: 이 신호 ID를 내는 상호작용 (있을 때만 보인다)
    private readonly Label _messageLabel;
    private readonly EntryList _require;
    private readonly EntryList _ops;

    private readonly List<string> _ruleMessages = new List<string>();
    private bool _hasError;
    private bool _hasWarning;
    private bool _ruleHasError;

    public StateRuleCardView(SerializedObject so, int index, StateRuleListView owner)
    {
        _so = so;
        _index = index;
        _owner = owner;
        _path = $"{StateRuleListView.RulesPath}.Array.data[{index}]";

        style.marginLeft = 6;
        style.marginRight = 6;
        style.marginTop = 4;
        style.marginBottom = 4;
        style.paddingLeft = 6;
        style.paddingRight = 6;
        style.paddingTop = 4;
        style.paddingBottom = 6;
        style.borderTopLeftRadius = 4;
        style.borderTopRightRadius = 4;
        style.borderBottomLeftRadius = 4;
        style.borderBottomRightRadius = 4;
        style.backgroundColor = new Color(0f, 0f, 0f, 0.12f);
        style.borderLeftWidth = 3;
        style.borderLeftColor = NormalBorder;

        // 1행: 실행 순번 + ▲▼·복제·삭제
        VisualElement top = Row();
        _orderLabel = new Label();
        _orderLabel.style.flexGrow = 1;
        _orderLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        top.Add(_orderLabel);
        top.Add(new Button(() => _owner.MoveRule(_index, -1)) { text = "▲", tooltip = "위로 — 같은 이벤트에서 먼저 실행" });
        top.Add(new Button(() => _owner.MoveRule(_index, +1)) { text = "▼", tooltip = "아래로 — 같은 이벤트에서 나중에 실행" });
        top.Add(new Button(() => _owner.DuplicateRule(_index)) { text = "복제" });
        top.Add(new Button(() => _owner.DeleteRule(_index)) { text = "삭제", tooltip = "Ctrl+Z로 되돌리기" });
        Add(top);

        // 2행: 이벤트 + ruleKey
        VisualElement trigger = Row();
        _eventField = new PopupField<EGameEventType>(EventChoices, EGameEventType.None, FormatEvent, FormatEvent);
        _eventField.style.width = 120;
        _eventField.tooltip = "event — 이 규칙을 실행시키는 사건";
        _eventField.RegisterValueChangedCallback(e => SetEvent(e.newValue));
        trigger.Add(_eventField);

        var hasRuleKey = new Toggle
        {
            text = "특정 값만",
            bindingPath = _path + "." + nameof(StateRuleDefinition.HasRuleKey),
            tooltip = "끄면 이 종류의 모든 이벤트에 반응 (JSON에서 ruleKey 생략). 켜고 0을 넣으면 0번에만 반응",
        };
        hasRuleKey.style.marginLeft = 8;
        hasRuleKey.RegisterValueChangedCallback(e =>
        {
            _ruleKeyValue.SetEnabled(e.newValue);
            _ruleKeyHint.text = RuleKeyHint(ReadEvent(), e.newValue);
        });
        trigger.Add(hasRuleKey);

        _ruleKeyValue = new IntegerField
        {
            bindingPath = _path + "." + nameof(StateRuleDefinition.RuleKeyValue),
            isDelayed = true,
            tooltip = "ruleKey",
        };
        _ruleKeyValue.style.width = 80;
        trigger.Add(_ruleKeyValue);

        _ruleKeyHint = new Label();
        _ruleKeyHint.style.marginLeft = 6;
        _ruleKeyHint.style.opacity = 0.6f;
        _ruleKeyHint.style.flexGrow = 1;
        trigger.Add(_ruleKeyHint);
        Add(trigger);

        // 신호 규칙 + "특정 값만"일 때, 이 신호 ID를 내는 상호작용을 보여 준다 (정보 — 경고가 아니다)
        _senderLabel = new Label
        {
            tooltip = "상호작용의 '신호 발생' 효과가 이 신호 ID를 낸다. 디스크에 저장된 상호작용 파일 기준이며, 고치는 곳은 Interaction Editor",
        };
        _senderLabel.style.paddingLeft = 4;
        _senderLabel.style.opacity = 0.75f;
        _senderLabel.style.whiteSpace = WhiteSpace.Normal;
        _senderLabel.style.display = DisplayStyle.None;
        Add(_senderLabel);

        // 3행: 설명
        var description = new TextField("설명")
        {
            bindingPath = _path + "." + nameof(StateRuleDefinition.Description),
            isDelayed = true,
            tooltip = "desc — 기획 메모. 게임 동작에는 영향 없음",
        };
        description.labelElement.style.minWidth = 40;
        description.labelElement.style.width = 40;
        Add(description);

        // 규칙 단위 검사 결과 (event 지정 필요, ops 없음 등) — 문제가 있을 때만 보인다
        _messageLabel = new Label();
        _messageLabel.style.whiteSpace = WhiteSpace.Normal;
        _messageLabel.style.paddingLeft = 4;
        _messageLabel.style.display = DisplayStyle.None;
        Add(_messageLabel);

        // 4행: 선행 조건 | 값 변경
        VisualElement columns = Row();
        columns.style.marginTop = 4;
        columns.style.alignItems = Align.FlexStart;
        _require = new EntryList(this, "선행 조건 (require) — 모두 참일 때만 실행", isOps: false);
        _require.style.marginRight = 6;
        columns.Add(_require);
        _ops = new EntryList(this, "값 변경 (ops)", isOps: true);
        columns.Add(_ops);
        Add(columns);

        this.Bind(_so);
    }

    #region 읽기 · 갱신
    public EGameEventType ReadEvent()
    {
        SerializedProperty property = _so.FindProperty(_path + "." + nameof(StateRuleDefinition.Event));
        return property != null ? (EGameEventType)property.intValue : EGameEventType.None;
    }

    /// <summary>목록이 값 변경·Undo 뒤에 호출한다. orderInEvent: 이 파일에서 같은 이벤트 규칙 중 몇 번째인지.</summary>
    public void Refresh(int orderInEvent)
    {
        SerializedProperty rule = _so.FindProperty(_path);
        if (rule == null)
            return;

        var evt = (EGameEventType)rule.FindPropertyRelative(nameof(StateRuleDefinition.Event)).intValue;
        bool hasKey = rule.FindPropertyRelative(nameof(StateRuleDefinition.HasRuleKey)).boolValue;

        // 목록에 없는 값(숫자로 적힌 잘못된 event 등)은 "(선택 필요)"로 보여 준다. 실제 값은 검사 문장에 나온다
        _eventField.SetValueWithoutNotify(EventChoices.Contains(evt) ? evt : EGameEventType.None);
        _orderLabel.text = orderInEvent > 0 && evt != EGameEventType.None
            ? $"#{_index}    {EditorEnumLabels.Of(evt)} 규칙 중 {orderInEvent}번째"
            : $"#{_index}";
        _ruleKeyValue.SetEnabled(hasKey);
        _ruleKeyHint.text = RuleKeyHint(evt, hasKey);

        // 상호작용이 없을 때는 줄을 숨긴다 — 그 경우는 툴 경고가 알린다
        string senders = null;
        if (evt == EGameEventType.Signal && hasKey)
            senders = _owner.DescribeSignalSenders(rule.FindPropertyRelative(nameof(StateRuleDefinition.RuleKeyValue)).intValue);
        _senderLabel.text = senders != null ? $"이 신호를 내는 상호작용: {senders}" : "";
        _senderLabel.style.display = senders != null ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void SetEvent(EGameEventType value)
    {
        _so.Update();
        SerializedProperty property = _so.FindProperty(_path + "." + nameof(StateRuleDefinition.Event));
        if (property == null || property.intValue == (int)value)
            return;

        property.intValue = (int)value;
        _so.ApplyModifiedProperties();
        Undo.SetCurrentGroupName("이벤트 변경");
    }

    private static string RuleKeyHint(EGameEventType evt, bool hasKey)
    {
        switch (evt)
        {
            case EGameEventType.DayAdvanced:
                return hasKey ? "= 새 일차 (예: 5 → 5일차가 될 때 한 번)" : "생략 — 매일";
            case EGameEventType.CreatureDied:
                return hasKey ? "= 죽은 개체의 templateID" : "생략 — 모든 개체의 죽음";
            case EGameEventType.Signal:
                return hasKey ? "= 신호 ID" : "생략 — 모든 신호";
            default:
                return "이벤트를 먼저 고르세요";
        }
    }

    private static string FormatEvent(EGameEventType value)
    {
        return EditorEnumLabels.Of(value);
    }

    private static VisualElement Row()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        return row;
    }
    #endregion

    #region 검사 결과 (목록이 호출)
    public void ClearIssues()
    {
        _ruleMessages.Clear();
        _hasError = false;
        _hasWarning = false;
        _ruleHasError = false;
        _require.ClearMarks();
        _ops.ClearMarks();
    }

    public void AddIssue(StateDefinitionIssue issue)
    {
        _hasError |= issue.IsError;
        _hasWarning |= issue.IsError == false;

        switch (issue.Section)
        {
            case EStateDefinitionSection.Require:
                _require.AddMark(issue);
                break;
            case EStateDefinitionSection.Op:
                _ops.AddMark(issue);
                break;
            default:
                _ruleHasError |= issue.IsError;
                _ruleMessages.Add((issue.IsError ? "✕ " : "⚠ ") + issue.Text);
                break;
        }
    }

    public void ApplyIssueStyles()
    {
        style.borderLeftColor = _hasError ? ErrorColor : _hasWarning ? WarningColor : NormalBorder;

        if (_ruleMessages.Count > 0)
        {
            _messageLabel.text = string.Join("\n", _ruleMessages);
            _messageLabel.style.color = _ruleHasError ? ErrorColor : WarningColor;
            _messageLabel.style.display = DisplayStyle.Flex;
        }
        else
        {
            _messageLabel.style.display = DisplayStyle.None;
        }

        _require.RefreshRowStates();
        _ops.RefreshRowStates();
    }

    /// <summary>검증 목록에서 이동했을 때 해당 require·ops 행을 선택한다.</summary>
    public void Highlight(EStateDefinitionSection section, int subIndex)
    {
        if (section == EStateDefinitionSection.Require)
            _require.Select(subIndex);
        else if (section == EStateDefinitionSection.Op)
            _ops.Select(subIndex);
    }
    #endregion

    #region 선행 조건 · 값 변경 목록
    /// <summary>require 또는 ops 목록. 배열 경로에 바인딩된 ListView라 드래그 재정렬과 Undo가 된다.</summary>
    private sealed class EntryList : VisualElement
    {
        private readonly StateRuleCardView _card;
        private readonly bool _isOps;
        private readonly string _arrayPath;
        private readonly ListView _list;
        private readonly Dictionary<int, IssueMark> _marks = new Dictionary<int, IssueMark>(); // 행 순번 → 검사 결과

        public EntryList(StateRuleCardView card, string title, bool isOps)
        {
            _card = card;
            _isOps = isOps;
            _arrayPath = card._path + "." + (isOps ? nameof(StateRuleDefinition.Ops) : nameof(StateRuleDefinition.Require));
            style.flexGrow = 1;
            style.flexBasis = 0;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            var titleLabel = new Label(title);
            titleLabel.style.flexGrow = 1;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(titleLabel);

            Button addButton = null;
            addButton = new Button(() => _card._owner.AddEntry(_card._index, _isOps, addButton)) { text = "+", tooltip = "키를 골라 추가" };
            header.Add(addButton);
            Add(header);

            _list = new ListView
            {
                bindingPath = _arrayPath,
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                fixedItemHeight = 22,
                selectionType = SelectionType.Single,
                showBorder = true,
                showBoundCollectionSize = false,
                showAddRemoveFooter = false,
                makeItem = () => new EntryRow(this),
                bindItem = BindRow,
                unbindItem = UnbindRow,
            };
            Add(_list);

            // 행을 선택하면 그 키의 사용처를 오른쪽 패널에 보여 준다
            _list.selectionChanged += _ =>
            {
                if (_list.selectedIndex >= 0)
                    _card._owner.NotifyEntrySelected(_card._index, _isOps, _list.selectedIndex);
            };
        }

        private sealed class EntryRow : VisualElement
        {
            public readonly Label Status;
            public readonly Button Key;
            public readonly EnumField Operator;
            public readonly IntegerField Value;
            public int Index = -1;

            public EntryRow(EntryList owner)
            {
                style.flexDirection = FlexDirection.Row;
                style.alignItems = Align.Center;
                style.flexGrow = 1;

                Status = new Label();
                Status.style.width = 14;
                Status.style.flexShrink = 0;
                Status.style.unityTextAlign = TextAnchor.MiddleCenter;
                Add(Status);

                // 키는 직접 입력하지 않는다 — 눌러서 팝업에서 고른다 (오타 방지). 버튼 글자는 키 값에 바인딩
                Key = new Button(() => owner.ChangeKey(Index, Key)) { tooltip = "눌러서 키 바꾸기" };
                Key.style.flexGrow = 1;
                Key.style.flexShrink = 1;
                Key.style.minWidth = 80;
                Key.style.unityTextAlign = TextAnchor.MiddleLeft;
                Add(Key);

                Operator = owner._isOps ? new EnumField(EStateOp.Set) : new EnumField(EComparison.GreaterOrEqual);
                Operator.style.width = owner._isOps ? 130 : 80;
                Operator.style.flexShrink = 0;
                Add(Operator);

                Value = new IntegerField { isDelayed = true };
                Value.style.width = 56;
                Value.style.flexShrink = 0;
                Add(Value);

                var remove = new Button(() => owner.Remove(Index)) { text = "×", tooltip = "삭제 (Ctrl+Z로 되돌리기)" };
                remove.style.width = 22;
                remove.style.flexShrink = 0;
                Add(remove);
            }
        }

        private void BindRow(VisualElement element, int index)
        {
            var row = (EntryRow)element;
            row.Index = index;

            SerializedProperty array = _card._so.FindProperty(_arrayPath);
            if (array == null || index >= array.arraySize)
                return;

            SerializedProperty entry = array.GetArrayElementAtIndex(index);
            row.Key.BindProperty(entry.FindPropertyRelative(nameof(StateOpDefinition.Key)));
            row.Operator.BindProperty(entry.FindPropertyRelative(_isOps ? nameof(StateOpDefinition.Op) : nameof(StateRequireDefinition.Comparison)));
            row.Value.BindProperty(entry.FindPropertyRelative(nameof(StateOpDefinition.Value)));
            ApplyRowState(row);
        }

        private static void UnbindRow(VisualElement element, int index)
        {
            var row = (EntryRow)element;
            row.Key.Unbind();
            row.Operator.Unbind();
            row.Value.Unbind();
            row.Index = -1;
        }

        private void ChangeKey(int index, VisualElement activator)
        {
            if (index >= 0)
                _card._owner.ChangeEntryKey(_card._index, _isOps, index, activator);
        }

        private void Remove(int index)
        {
            if (index >= 0)
                _card._owner.RemoveEntry(_card._index, _isOps, index);
        }

        public void ClearMarks()
        {
            _marks.Clear();
        }

        public void AddMark(StateDefinitionIssue issue)
        {
            if (_marks.TryGetValue(issue.SubIndex, out IssueMark mark))
            {
                mark.IsError |= issue.IsError;
                mark.Text += "\n" + issue.Text;
                _marks[issue.SubIndex] = mark;
            }
            else
            {
                _marks.Add(issue.SubIndex, new IssueMark { IsError = issue.IsError, Text = issue.Text });
            }
        }

        /// <summary>보이는 행만 다시 칠한다 (다시 바인딩하지 않으므로 입력 중인 칸이 흔들리지 않는다).</summary>
        public void RefreshRowStates()
        {
            _list.Query<EntryRow>().ForEach(ApplyRowState);
        }

        private void ApplyRowState(EntryRow row)
        {
            if (row.Index >= 0 && _marks.TryGetValue(row.Index, out IssueMark mark))
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

        public void Select(int index)
        {
            if (index < 0)
                return;
            _list.ScrollToItem(index);
            _list.SetSelection(index);
        }
    }
    #endregion
}