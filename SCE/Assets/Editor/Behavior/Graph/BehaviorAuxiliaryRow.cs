using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 카드 안 Decorator (파랑) · Service (초록) 한 줄 — 이름 · 요약 (Decorator: Observer Aborts · 결과 뒤집기, Service: 간격 ± 편차) · 메모 표시 · 검사 표시 ·
/// 디버거 표시 (Decorator: 마지막 조건 결과 ✓ · ✗ · 지켜봄, Service: 다음 갱신까지 — 그리고 실행 값 글).
/// 왼쪽 클릭: 이 줄을 고른다 (세부 설정이 이 노드를 보인다 — 카드 고르기 · 끌기로 넘기지 않는다). 오른쪽 클릭: 위로 · 아래로 (같은 목록 안) · 복사 · 복제 · 지우기.
/// 바꾸기는 그래프 (BehaviorTreeGraphView)가 문서에 한다.
/// </summary>
public sealed class BehaviorAuxiliaryRow : VisualElement
{
    private readonly BehaviorTreeGraphView _graph;
    private readonly BehaviorMainNode _owner;
    private readonly int _index;
    private readonly int _count;
    private readonly string _name;
    private readonly string _description;
    private readonly Label _summaryLabel;
    private readonly Label _memoMark;
    private readonly Label _issueMark;
    private readonly Label _debugMark;
    private readonly Label _debugLine;
    private string _memo = "";

    public BehaviorAuxiliaryNode AuxiliaryNode { get; }

    public BehaviorMainNode Owner => _owner;

    /// <param name="index">owner의 목록 (Decorators · Services)에서의 자리</param>
    /// <param name="count">그 목록의 길이 — 위로 · 아래로를 쓸 수 있는가</param>
    public BehaviorAuxiliaryRow(BehaviorTreeGraphView graph, BehaviorMainNode owner, BehaviorAuxiliaryNode node, BehaviorNodeRegistry registry, int index, int count)
    {
        _graph = graph;
        _owner = owner;
        _index = index;
        _count = count;
        AuxiliaryNode = node;

        style.backgroundColor = node is BehaviorService ? BehaviorGraphStyle.ServiceRow : BehaviorGraphStyle.DecoratorRow;
        style.marginLeft = 4;
        style.marginRight = 4;
        style.marginTop = 1;
        style.marginBottom = 1;
        style.paddingLeft = 6;
        style.paddingRight = 6;
        style.paddingTop = 3;
        style.paddingBottom = 3;
        style.borderTopLeftRadius = 3;
        style.borderTopRightRadius = 3;
        style.borderBottomLeftRadius = 3;
        style.borderBottomRightRadius = 3;
        SetSelected(false);

        bool known = registry.TryGetInfo(node.GetType(), out BehaviorNodeRegistry.NodeInfo info);
        _name = known ? info.DisplayName : node.GetType().Name;
        _description = known ? BehaviorNodePicker.Title(in info) + "\n" + info.Description : node.GetType().Name + " — 등록되지 않은 노드";

        var line = new VisualElement();
        line.style.flexDirection = FlexDirection.Row;
        line.style.alignItems = Align.Center;
        var nameLabel = new Label(_name);
        nameLabel.style.color = Color.white;
        nameLabel.style.flexGrow = 1;
        line.Add(nameLabel);
        _memoMark = new Label("✎");
        _memoMark.style.color = BehaviorGraphStyle.SubText;
        _memoMark.style.display = DisplayStyle.None;
        line.Add(_memoMark);
        _debugMark = new Label();
        _debugMark.style.fontSize = 10;
        _debugMark.style.unityFontStyleAndWeight = FontStyle.Bold;
        _debugMark.style.marginLeft = 4;
        _debugMark.style.display = DisplayStyle.None;
        line.Add(_debugMark);
        _issueMark = BehaviorGraphStyle.IssueMark();
        line.Add(_issueMark);
        Add(line);

        _summaryLabel = new Label();
        _summaryLabel.style.fontSize = 10;
        _summaryLabel.style.color = BehaviorGraphStyle.SubText;
        Add(_summaryLabel);
        _debugLine = BehaviorGraphStyle.DebugLine();
        _debugLine.style.paddingLeft = 0;
        Add(_debugLine);
        Refresh();

        RegisterCallback<MouseDownEvent>(OnMouseDown);
        this.AddManipulator(new ContextualMenuManipulator(BuildMenu));
    }

    /// <summary>노드 값으로 요약 · 툴팁을 다시 쓴다 (칸 값이 바뀐 뒤 — 줄을 다시 만들지 않는다)</summary>
    public void Refresh()
    {
        BehaviorGraphStyle.SetText(_summaryLabel, Summary(AuxiliaryNode));
        tooltip = _description + "\n이름표: " + AuxiliaryNode.NodeId + (_memo.Length > 0 ? "\n메모: " + _memo : "");
    }

    /// <summary>메모 — 있으면 ✎ 표시 (글은 툴팁)</summary>
    public void SetMemo(string memo)
    {
        _memo = memo ?? "";
        _memoMark.style.display = _memo.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        Refresh();
    }

    /// <summary>검사 표시 — 이 노드의 가장 무거운 문제 (없으면 null)와 문장</summary>
    public void SetIssue(EBehaviorMessageSeverity? severity, string text)
    {
        BehaviorGraphStyle.SetIssue(_issueMark, severity, text);
    }

    /// <summary>보이는 디버거 표시 (감췄으면 빈 글) — 오른쪽 표시 · 실행 값 글</summary>
    public string DebugMarkText => _debugMark.style.display.value == DisplayStyle.None ? "" : _debugMark.text;

    public string DebugText => _debugLine.style.display.value == DisplayStyle.None ? "" : _debugLine.text;

    /// <summary>
    /// 디버거 표시 — Decorator: 마지막 조건 결과 (✓ 초록 · ✗ 빨강 · 아직 보지 않았으면 없음) + 지켜보는 중이면 "지켜봄", Service: 갱신 중이면 "갱신 n초 뒤" (serviceNext — 아니면 NaN).
    /// 실행 값 글은 text (비면 감춤). 디버거를 끄면 Unknown · false · NaN · 빈 글
    /// </summary>
    public void SetDebug(EBehaviorDebugCondition condition, bool observing, float serviceNext, string text)
    {
        string mark = "";
        Color color = BehaviorGraphStyle.DebugText;
        if (AuxiliaryNode is BehaviorService)
        {
            if (float.IsNaN(serviceNext) == false)
                mark = "갱신 " + BehaviorDebugText.Number(serviceNext) + "초 뒤";
        }
        else
        {
            if (condition == EBehaviorDebugCondition.True)
            {
                mark = "✓";
                color = BehaviorGraphStyle.ConditionTrue;
            }
            else if (condition == EBehaviorDebugCondition.False)
            {
                mark = "✗";
                color = BehaviorGraphStyle.ConditionFalse;
            }
            if (observing)
                mark = mark.Length > 0 ? mark + " 지켜봄" : "지켜봄";
        }
        _debugMark.style.color = color;
        BehaviorGraphStyle.SetText(_debugMark, mark);
        BehaviorGraphStyle.SetText(_debugLine, text);
    }

    /// <summary>고른 줄 표시 — 흰 테두리</summary>
    public void SetSelected(bool selected)
    {
        float width = selected ? 1f : 0f;
        style.borderLeftWidth = width;
        style.borderRightWidth = width;
        style.borderTopWidth = width;
        style.borderBottomWidth = width;
        style.borderLeftColor = BehaviorGraphStyle.SelectedRowBorder;
        style.borderRightColor = BehaviorGraphStyle.SelectedRowBorder;
        style.borderTopColor = BehaviorGraphStyle.SelectedRowBorder;
        style.borderBottomColor = BehaviorGraphStyle.SelectedRowBorder;
    }

    /// <summary>왼쪽 클릭 — 이 줄을 고른다. 카드 고르기 · 끌기로 넘기지 않는다 (카드는 머리로 끈다)</summary>
    private void OnMouseDown(MouseDownEvent evt)
    {
        if (evt.button != (int)MouseButton.LeftMouse)
            return;
        _graph.SelectAuxiliary(_owner, AuxiliaryNode);
        evt.StopPropagation();
    }

    private void BuildMenu(ContextualMenuPopulateEvent evt)
    {
        if (evt.target != this)
            return;
        evt.menu.AppendAction("위로", _ => _graph.MoveAuxiliary(_owner, AuxiliaryNode, -1), StatusOf(_index > 0));
        evt.menu.AppendAction("아래로", _ => _graph.MoveAuxiliary(_owner, AuxiliaryNode, +1), StatusOf(_index + 1 < _count));
        evt.menu.AppendSeparator();
        evt.menu.AppendAction("복사", _ => _graph.CopyRow(_owner, AuxiliaryNode));
        evt.menu.AppendAction("복제", _ => _graph.DuplicateRow(_owner, AuxiliaryNode));
        evt.menu.AppendSeparator();
        evt.menu.AppendAction("지우기", _ => _graph.RemoveAuxiliary(_owner, AuxiliaryNode));
    }

    private static DropdownMenuAction.Status StatusOf(bool enabled)
    {
        return enabled ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;
    }

    /// <summary>요약 한 줄 — Decorator: "Aborts Both · 결과 뒤집기" (None · 뒤집지 않음이면 빈 글), Service: "0.5초 ± 0.1"</summary>
    public static string Summary(BehaviorAuxiliaryNode node)
    {
        switch (node)
        {
            case BehaviorDecorator decorator:
                string aborts = decorator.AbortMode != EBehaviorAbortMode.None ? "Aborts " + decorator.AbortMode : "";
                if (decorator.InverseCondition == false)
                    return aborts;
                return aborts.Length > 0 ? aborts + " · 결과 뒤집기" : "결과 뒤집기";
            case BehaviorService service:
                return service.Interval.ToString("0.##", CultureInfo.InvariantCulture) + "초 ± " + service.RandomDeviation.ToString("0.##", CultureInfo.InvariantCulture);
            default:
                return "";
        }
    }
}