using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Main 노드 (Composite · Task) 카드 — 위에서 아래로: 입력 칸 · 메모 줄 · Decorator 줄 (파랑) · 머리 (breakpoint ● · 이름 · 이름표 · 검사 표시 · 실행 순서 번호) · 실행 값 글 (디버거) ·
/// Service 줄 (초록) · 출력 칸 (Composite만). 노드 하나를 보이기만 한다 — 바꾸기는 그래프 (BehaviorTreeGraphView)가 문서에 한다. 이어지지 않은 노드의 카드는 흐리고 번호가 없다.
/// 디버거: 진행 경로 · 진행 중 · 끊는 중 · 멈춘 노드를 겹쳐 그리는 테두리 색으로 (SetDebug — 카드 크기는 그대로). 오른쪽 클릭: Decorator 추가 · Service 추가 · 복사 · 복제 · breakpoint · 부모에서 끊기 · 지우기.
/// 고르기가 바뀌면 그래프에 알린다 (세부 설정).
/// </summary>
public sealed class BehaviorNodeCard : Node
{
    private readonly BehaviorTreeGraphView _graph;
    private readonly Label _subtitle;
    private readonly Label _orderBadge;
    private readonly Label _issueMark;
    private readonly Label _memoLine;
    private readonly Label _breakpointDot;
    private readonly Label _debugLine;
    private readonly VisualElement _debugOutline;
    private readonly string _description;
    private readonly List<BehaviorAuxiliaryRow> _rows = new List<BehaviorAuxiliaryRow>();

    public BehaviorMainNode MainNode { get; }

    public BehaviorGraphPort InputPort { get; }

    /// <summary>자식을 잇는 칸 — Composite만 (Task는 null)</summary>
    public BehaviorGraphPort OutputPort { get; }

    /// <summary>카드 안 Decorator · Service 줄 (Decorator 먼저, 목록 차례)</summary>
    public IReadOnlyList<BehaviorAuxiliaryRow> Rows => _rows;

    public BehaviorNodeCard(BehaviorTreeGraphView graph, BehaviorMainNode node, BehaviorNodeRegistry registry)
    {
        _graph = graph;
        MainNode = node;
        bool known = registry.TryGetInfo(node.GetType(), out BehaviorNodeRegistry.NodeInfo info);
        string displayName = known ? info.DisplayName : node.GetType().Name;
        title = displayName;
        _description = known ? BehaviorNodePicker.Title(in info) + "\n" + info.Description : node.GetType().Name + " — 등록되지 않은 노드";

        VisualElement middle = BehaviorGraphStyle.BuildBody(this);
        InputPort = BehaviorGraphPort.Create(Direction.Input, Port.Capacity.Single, graph.OnPortDroppedOutside);
        inputContainer.Add(InputPort);
        if (node is BehaviorComposite)
        {
            OutputPort = BehaviorGraphPort.Create(Direction.Output, Port.Capacity.Multi, graph.OnPortDroppedOutside);
            outputContainer.Add(OutputPort);
        }
        else
        {
            outputContainer.style.display = DisplayStyle.None;
        }

        _memoLine = BehaviorGraphStyle.MemoLine();
        middle.Add(_memoLine);
        middle.Add(AuxiliaryRows(node.Decorators, registry));
        VisualElement header = BehaviorGraphStyle.Header(displayName, node.NodeId,
            node is BehaviorComposite ? BehaviorGraphStyle.CompositeHeader : BehaviorGraphStyle.TaskHeader, out _subtitle, out _orderBadge);
        _issueMark = BehaviorGraphStyle.IssueMark();
        header.Insert(header.IndexOf(_orderBadge), _issueMark);
        _breakpointDot = BehaviorGraphStyle.BreakpointDot();
        header.Insert(0, _breakpointDot);
        header.tooltip = _description;
        middle.Add(header);
        _debugLine = BehaviorGraphStyle.DebugLine();
        middle.Add(_debugLine);
        middle.Add(AuxiliaryRows(node.Services, registry));
        _debugOutline = BehaviorGraphStyle.DebugOutline();
        mainContainer.Add(_debugOutline); // 마지막 — 카드 위에 겹쳐 그린다
        SetOrder(-1);
    }

    /// <summary>보이는 실행 순서 번호 — 이어지지 않은 노드는 -1</summary>
    public int Order { get; private set; } = -1;

    /// <summary>실행 순서 번호 — 이어지지 않은 노드는 -1 (번호 없음 · 흐리게)</summary>
    public void SetOrder(int index)
    {
        Order = index;
        _orderBadge.text = index >= 0 ? index.ToString() : "";
        _orderBadge.style.display = index >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
        style.opacity = index >= 0 ? 1f : BehaviorGraphStyle.LooseOpacity;
    }

    /// <summary>노드 값으로 이름표 · 줄 요약을 다시 쓴다 (칸 값 · 이름표가 바뀐 뒤 — 카드를 다시 만들지 않는다)</summary>
    public void Refresh()
    {
        BehaviorGraphStyle.SetText(_subtitle, MainNode.NodeId);
        for (int i = 0; i < _rows.Count; i++)
            _rows[i].Refresh();
    }

    /// <summary>메모 — 카드 위쪽 줄 (비면 감춤)</summary>
    public void SetMemo(string memo)
    {
        BehaviorGraphStyle.SetText(_memoLine, memo);
    }

    /// <summary>검사 표시 — 이 노드의 가장 무거운 문제 (없으면 null)와 문장</summary>
    public void SetIssue(EBehaviorMessageSeverity? severity, string text)
    {
        BehaviorGraphStyle.SetIssue(_issueMark, severity, text);
    }

    /// <summary>breakpoint 표시 (●)</summary>
    public void SetBreakpoint(bool on)
    {
        _breakpointDot.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
    }

    /// <summary>보이는 디버거 상태 — 테두리 · 실행 값 글이 이것을 따른다</summary>
    public EBehaviorDebugNodeState DebugState { get; private set; }

    /// <summary>멈춘 노드 (breakpoint)로 보이는가</summary>
    public bool IsDebugHeld { get; private set; }

    /// <summary>보이는 실행 값 글 (감췄으면 빈 글)</summary>
    public string DebugText => _debugLine.style.display.value == DisplayStyle.None ? "" : _debugLine.text;

    /// <summary>
    /// 디버거 표시 — 상태에 따라 겹쳐 그리는 테두리 (멈춘 노드 빨강 · 진행 중 초록 · 끊는 중 주황 · 진행 경로 호박색 · 그 밖은 없음)와 실행 값 글 (비면 감춤, 멈춘 노드는 "■ 멈춤" 앞에).
    /// 디버거를 끄면 Idle · false · 빈 글
    /// </summary>
    public void SetDebug(EBehaviorDebugNodeState state, bool held, string text)
    {
        DebugState = state;
        IsDebugHeld = held;
        Color? border = held ? BehaviorGraphStyle.DebugHeld
            : state == EBehaviorDebugNodeState.Running ? BehaviorGraphStyle.DebugRunning
            : state == EBehaviorDebugNodeState.Aborting ? BehaviorGraphStyle.DebugAborting
            : state == EBehaviorDebugNodeState.OnPath ? BehaviorGraphStyle.DebugPath
            : (Color?)null;
        float width = held || state == EBehaviorDebugNodeState.Running || state == EBehaviorDebugNodeState.Aborting ? BehaviorGraphStyle.DebugActiveBorder : BehaviorGraphStyle.DebugPathBorder;
        BehaviorGraphStyle.SetOutline(_debugOutline, border, width);
        string line = text ?? "";
        if (held)
            line = line.Length > 0 ? "■ 멈춤 · " + line : "■ 멈춤";
        else if (state == EBehaviorDebugNodeState.Aborting)
            line = line.Length > 0 ? "끊는 중 · " + line : "끊는 중";
        BehaviorGraphStyle.SetText(_debugLine, line);
    }

    public override void OnSelected()
    {
        base.OnSelected();
        _graph.OnCardSelectionChanged();
    }

    public override void OnUnselected()
    {
        base.OnUnselected();
        _graph.OnCardSelectionChanged();
    }

    public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
    {
        if (evt.target != this)
            return;
        Vector2 position = evt.mousePosition;
        evt.menu.AppendAction("Decorator 추가…", _ => _graph.AddAuxiliary(MainNode, EBehaviorNodeKind.Decorator, position));
        evt.menu.AppendAction("Service 추가…", _ => _graph.AddAuxiliary(MainNode, EBehaviorNodeKind.Service, position));
        evt.menu.AppendSeparator();
        evt.menu.AppendAction("복사", _ => _graph.CopyCard(this));
        evt.menu.AppendAction("복제", _ => _graph.DuplicateCard(this));
        evt.menu.AppendSeparator();
        evt.menu.AppendAction(_graph.HasBreakpoint(MainNode) ? "breakpoint 끄기" : "breakpoint 켜기", _ => _graph.ToggleBreakpoint(MainNode),
            _graph.CanToggleBreakpoint(MainNode) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        evt.menu.AppendSeparator();
        evt.menu.AppendAction("부모에서 끊기", _ => _graph.DisconnectFromParent(MainNode),
            _graph.HasParent(MainNode) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        evt.menu.AppendAction("지우기", _ => _graph.DeleteCard(this));
    }

    /// <summary>접지 않는다 — 접기는 칸 묶음을 기본 자리로 옮긴다</summary>
    protected override void ToggleCollapse()
    {
    }

    /// <summary>Decorator · Service 줄 묶음 — 없으면 감춘다</summary>
    private VisualElement AuxiliaryRows<T>(T[] auxiliaries, BehaviorNodeRegistry registry) where T : BehaviorAuxiliaryNode
    {
        var rows = new VisualElement();
        int count = auxiliaries?.Length ?? 0;
        for (int i = 0; i < count; i++)
        {
            if (auxiliaries[i] == null)
                continue;
            var row = new BehaviorAuxiliaryRow(_graph, MainNode, auxiliaries[i], registry, i, count);
            _rows.Add(row);
            rows.Add(row);
        }
        rows.style.display = rows.childCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        rows.style.paddingTop = 2;
        rows.style.paddingBottom = 2;
        return rows;
    }
}