using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;

/// <summary>
/// ROOT 카드 — 트리의 시작. 출력 칸 하나에 루트 노드 (Composite만)를 잇는다. 지울 수 없다 (옮길 수는 있다).
/// 머리 = "ROOT" · 트리 Id · 트리 전체의 검사 표시 (설정 · Blackboard · Id), 그 아래 디버거 글 (대상 · 평가 횟수 — SetDebug). 고르면 세부 설정이 트리 설정을 보인다.
/// </summary>
public sealed class BehaviorRootCard : Node
{
    private readonly BehaviorTreeGraphView _graph;
    private readonly Label _subtitle;
    private readonly Label _issueMark;
    private readonly Label _debugLine;

    public BehaviorGraphPort OutputPort { get; }

    public BehaviorRootCard(BehaviorTreeGraphView graph, string treeId)
    {
        _graph = graph;
        capabilities &= ~(Capabilities.Deletable | Capabilities.Copiable);
        title = "ROOT";

        VisualElement middle = BehaviorGraphStyle.BuildBody(this);
        inputContainer.style.display = DisplayStyle.None;
        OutputPort = BehaviorGraphPort.Create(Direction.Output, Port.Capacity.Single, graph.OnPortDroppedOutside);
        outputContainer.Add(OutputPort);

        VisualElement header = BehaviorGraphStyle.Header("ROOT", treeId, BehaviorGraphStyle.RootHeader, out _subtitle, out Label badge);
        badge.style.display = DisplayStyle.None;
        _issueMark = BehaviorGraphStyle.IssueMark();
        header.Insert(header.IndexOf(badge), _issueMark);
        middle.Add(header);
        _debugLine = BehaviorGraphStyle.DebugLine();
        middle.Add(_debugLine);
    }

    /// <summary>트리 Id를 다시 쓴다</summary>
    public void SetTreeId(string treeId)
    {
        BehaviorGraphStyle.SetText(_subtitle, treeId);
    }

    /// <summary>트리 전체의 검사 표시 — 가장 무거운 문제 (없으면 null)와 문장</summary>
    public void SetIssue(EBehaviorMessageSeverity? severity, string text)
    {
        BehaviorGraphStyle.SetIssue(_issueMark, severity, text);
    }

    /// <summary>디버거 글 (대상 · 평가 횟수 · 멈춘 까닭) — 비면 감춤</summary>
    public void SetDebug(string text)
    {
        BehaviorGraphStyle.SetText(_debugLine, text);
    }

    /// <summary>보이는 디버거 글 (감췄으면 빈 글)</summary>
    public string DebugText => _debugLine.style.display.value == DisplayStyle.None ? "" : _debugLine.text;

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

    /// <summary>메뉴 없음 — 기본 Node의 'Disconnect all'도 넣지 않는다</summary>
    public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
    {
    }

    /// <summary>접지 않는다 — 접기는 칸 묶음을 기본 자리로 옮긴다</summary>
    protected override void ToggleCollapse()
    {
    }
}