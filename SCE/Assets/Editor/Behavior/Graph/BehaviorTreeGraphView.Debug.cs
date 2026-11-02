using System;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

/// <summary>
/// 디버거 표시 — Play 중 대상 몬스터의 실행 모습 (BehaviorDebugFrame)을 카드 · 줄 · 선에 보인다: 진행 경로 (카드 테두리 · 선 색), 진행 중 Task · 끊는 중 · 멈춘 노드,
/// Decorator 결과 (✓ · ✗ · 지켜봄) · Service 다음 갱신, 노드의 실행 값 글, ROOT 카드의 디버거 글 (대상 · 평가 횟수 — 창이 만든 글, SetDebugRootText).
/// 실행 중인 노드와 카드 · 줄은 이름표로 잇는다 — 이름표가 같은 카드 · 줄 (같은 노드에 붙은 줄)에만 보이고, 편집본이 실행 중인 트리와 다르면 DebugMismatch.
/// breakpoint 표시 (카드 머리의 ●)는 Play와 상관없이 보인다 — 있는가 · 켜고 끄기는 창이 정한다 (SetBreakpointHandlers). 다시 그려도 남는다 (RebuildNow가 ApplyDebug)
/// </summary>
public sealed partial class BehaviorTreeGraphView
{
    private BehaviorDebugFrame _debugFrame;  // 보이는 모습 — 없으면 null (디버거 표시 없음)
    private int _debugHeldNode = -1;         // 멈춘 노드의 실행 순서 — 없으면 -1
    private string _debugRootText = "";
    private Func<BehaviorMainNode, bool> _hasBreakpoint;
    private Action<BehaviorMainNode> _toggleBreakpoint;
    private BehaviorTreeDefinition _debugIndexed; // 아래 두 사전을 만든 정의
    private bool _debugIndexedUnlabeled;          // 그 정의에 이름표가 빈 노드가 있다
    private readonly Dictionary<string, int> _debugMainIndex = new Dictionary<string, int>(StringComparer.Ordinal);      // 이름표 → Main 노드 실행 순서
    private readonly Dictionary<string, int> _debugAuxiliaryIndex = new Dictionary<string, int>(StringComparer.Ordinal); // 이름표 → 보조 노드 번호
    private readonly HashSet<Edge> _debugPathEdges = new HashSet<Edge>(); // 진행 경로의 선 — 입히는 동안만

    /// <summary>
    /// 보이는 모습의 트리가 편집본과 다르다 — 이어진 카드 가운데 실행 중인 트리에 없는 이름표 · 번호 또는 부모가 다른 카드가 있거나, 그 줄 가운데 실행 중인 트리에 없는 줄 ·
    /// 다른 노드에 붙은 줄 · 차례가 다른 줄이 있거나, 실행 중인 노드 · 줄 수가 이어진 카드 · 그 줄 수와 다르다 (구조만 — 값만 바꾼 편집은 창이 따로 알린다). 보이는 모습이 없으면 false
    /// </summary>
    public bool DebugMismatch { get; private set; }

    /// <summary>
    /// 보이는 모습의 트리 (게임이 읽은 트리)에 이름표가 빈 노드가 있다 — 이름표를 저장하지 않은 트리 파일 (편집기는 불러올 때 채운다)이라 그 노드는 카드 · 줄 · breakpoint와 이어지지 않는다.
    /// 보이는 모습이 없으면 false
    /// </summary>
    public bool DebugUnlabeled { get; private set; }

    /// <summary>breakpoint가 있는가 · 켜고 끄기 (창 — 트리 Id와 노드 이름표로)</summary>
    internal void SetBreakpointHandlers(Func<BehaviorMainNode, bool> hasBreakpoint, Action<BehaviorMainNode> toggleBreakpoint)
    {
        _hasBreakpoint = hasBreakpoint;
        _toggleBreakpoint = toggleBreakpoint;
        RefreshBreakpoints();
    }

    internal bool HasBreakpoint(BehaviorMainNode node)
    {
        return node != null && _hasBreakpoint != null && _hasBreakpoint(node);
    }

    /// <summary>breakpoint를 켜고 끌 수 있는가 — 트리 Id와 이름표가 있어야 한다 (breakpoint는 트리 Id + 이름표)</summary>
    internal bool CanToggleBreakpoint(BehaviorMainNode node)
    {
        return node != null && _toggleBreakpoint != null && string.IsNullOrEmpty(node.NodeId) == false
            && _document != null && string.IsNullOrEmpty(_document.Tree.Id) == false;
    }

    /// <summary>카드 메뉴 'breakpoint' — 켜고 끈 뒤 표시를 맞춘다</summary>
    internal void ToggleBreakpoint(BehaviorMainNode node)
    {
        if (CanToggleBreakpoint(node) == false)
            return;
        _toggleBreakpoint(node);
        RefreshBreakpoints();
    }

    /// <summary>카드마다 breakpoint 표시 (●)를 맞춘다 — 트리를 바꾸거나 모두 끈 뒤에도</summary>
    public void RefreshBreakpoints()
    {
        foreach (BehaviorNodeCard card in _cards.Values)
            card.SetBreakpoint(HasBreakpoint(card.MainNode));
    }

    /// <summary>
    /// 실행 모습을 보인다 (null이거나 정의가 없으면 카드 · 줄 · 선의 디버거 표시를 지운다). heldNode = 멈춘 노드의 실행 순서 (없으면 -1).
    /// 모습은 참조로 둔다 — 같은 모습을 다시 담은 뒤에도 다시 부른다. 보이는 문서를 바꾸면 (Show) 지워진다. O(카드 수 + 줄 수 + 선 수)
    /// </summary>
    public void ShowDebug(BehaviorDebugFrame frame, int heldNode)
    {
        _debugFrame = frame != null && frame.Definition != null ? frame : null;
        _debugHeldNode = _debugFrame != null ? heldNode : -1;
        ApplyDebug();
    }

    /// <summary>ROOT 카드의 디버거 글 (SetDebugRootText — 없으면 빈 글)</summary>
    public string DebugRootText => _debugRootText;

    /// <summary>ROOT 카드의 디버거 글 (비면 감춤) — 다시 그려도 남는다</summary>
    public void SetDebugRootText(string text)
    {
        _debugRootText = text ?? "";
        _rootCard?.SetDebug(_debugRootText);
    }

    /// <summary>보이는 모습 · ROOT 글을 잊는다 — 다른 문서를 보일 때 (그 모습은 다른 트리의 것일 수 있다)</summary>
    private void ForgetDebug()
    {
        _debugFrame = null;
        _debugHeldNode = -1;
        _debugRootText = "";
    }

    /// <summary>보이는 모습을 카드 · 줄 · 선에 입힌다 (모습이 없으면 지운다) — breakpoint 표시도</summary>
    private void ApplyDebug()
    {
        BehaviorDebugFrame frame = _debugFrame;
        if (frame != null && frame.Definition == null)
            frame = null; // 담은 뒤 비운 모습 (Clear)
        if (frame != null)
            IndexDefinition(frame.Definition);
        _rootCard?.SetDebug(_debugRootText);
        _debugPathEdges.Clear();

        bool mismatch = false;
        int connected = 0;
        int connectedRows = 0;
        foreach (BehaviorNodeCard card in _cards.Values)
        {
            card.SetBreakpoint(HasBreakpoint(card.MainNode));
            int index = frame != null ? MainIndexOf(card.MainNode) : -1;
            bool isConnected = frame != null && card.Order >= 0;
            if (isConnected)
            {
                connected++;
                if (index != card.Order || (index >= 0 && frame.Definition.GetMainNode(index).ParentIndex != ParentOrderOf(card)))
                    mismatch = true;
            }
            EBehaviorDebugNodeState state = index >= 0 ? frame.StateOf(index) : EBehaviorDebugNodeState.Idle;
            card.SetDebug(state, index >= 0 && index == _debugHeldNode, index >= 0 ? frame.MainTextOf(index) : "");
            if (state != EBehaviorDebugNodeState.Idle)
            {
                foreach (Edge edge in card.InputPort.connections) // 부모 카드 · ROOT 카드에서 들어오는 선
                    _debugPathEdges.Add(edge);
            }

            IReadOnlyList<BehaviorAuxiliaryRow> rows = card.Rows;
            if (isConnected)
                connectedRows += rows.Count;
            int previous = -1; // 이 카드에서 앞 줄의 보조 노드 번호 — 실행 중인 트리에서도 Decorator · Service 차례로 늘어난다
            for (int i = 0; i < rows.Count; i++)
            {
                int auxiliary = frame != null && index >= 0 ? AuxiliaryIndexOf(rows[i].AuxiliaryNode) : -1;
                if (auxiliary >= 0 && frame.Definition.GetAuxiliaryNode(auxiliary).OwnerIndex != index)
                {
                    mismatch = true; // 다른 카드로 옮긴 줄 — 보이지 않는다
                    auxiliary = -1;
                }
                else if (auxiliary >= 0)
                {
                    if (auxiliary <= previous)
                        mismatch = true; // 카드 안에서 차례를 바꾼 줄
                    previous = auxiliary;
                }
                else if (isConnected)
                {
                    mismatch = true; // 실행 중인 트리에 없는 줄
                }
                if (auxiliary < 0)
                {
                    rows[i].SetDebug(EBehaviorDebugCondition.Unknown, false, float.NaN, "");
                    continue;
                }
                frame.TryGetServiceNext(auxiliary, out float serviceNext);
                rows[i].SetDebug(frame.ConditionOf(auxiliary), frame.IsObserving(auxiliary), serviceNext, frame.AuxiliaryTextOf(auxiliary));
            }
        }
        DebugMismatch = frame != null && (mismatch || connected != frame.Definition.MainNodeCount || connectedRows != frame.Definition.AuxiliaryNodeCount);
        DebugUnlabeled = frame != null && _debugIndexedUnlabeled;

        foreach (Edge edge in edges.ToList())
        {
            if (edge is BehaviorGraphEdge debugEdge)
                debugEdge.SetDebugColor(_debugPathEdges.Contains(edge) ? BehaviorGraphStyle.DebugPath : (Color?)null);
        }
        _debugPathEdges.Clear();
    }

    /// <summary>편집본에서 카드의 부모 카드 번호 — ROOT 카드에 이어졌거나 이어지지 않았으면 -1</summary>
    private static int ParentOrderOf(BehaviorNodeCard card)
    {
        foreach (Edge edge in card.InputPort.connections)
            return edge.output != null && edge.output.node is BehaviorNodeCard parent ? parent.Order : -1;
        return -1;
    }

    /// <summary>이름표로 실행 중인 Main 노드의 실행 순서 — 이름표가 없거나 실행 중인 트리에 없으면 -1</summary>
    private int MainIndexOf(BehaviorMainNode node)
    {
        return string.IsNullOrEmpty(node.NodeId) == false && _debugMainIndex.TryGetValue(node.NodeId, out int index) ? index : -1;
    }

    private int AuxiliaryIndexOf(BehaviorAuxiliaryNode node)
    {
        return string.IsNullOrEmpty(node.NodeId) == false && _debugAuxiliaryIndex.TryGetValue(node.NodeId, out int index) ? index : -1;
    }

    /// <summary>정의의 이름표 → 번호 사전 (정의가 바뀔 때만 다시) — 이름표가 겹치면 앞의 노드 것. 이름표가 빈 노드가 있는지도 (DebugUnlabeled)</summary>
    private void IndexDefinition(BehaviorTreeDefinition definition)
    {
        if (ReferenceEquals(definition, _debugIndexed))
            return;
        _debugIndexed = definition;
        _debugIndexedUnlabeled = HasUnlabeledNodes(definition);
        _debugMainIndex.Clear();
        _debugAuxiliaryIndex.Clear();
        for (int i = 0; i < definition.MainNodeCount; i++)
        {
            string id = definition.GetMainNode(i).NodeId;
            if (string.IsNullOrEmpty(id) == false && _debugMainIndex.ContainsKey(id) == false)
                _debugMainIndex.Add(id, i);
        }
        for (int i = 0; i < definition.AuxiliaryNodeCount; i++)
        {
            string id = definition.GetAuxiliaryNode(i).NodeId;
            if (string.IsNullOrEmpty(id) == false && _debugAuxiliaryIndex.ContainsKey(id) == false)
                _debugAuxiliaryIndex.Add(id, i);
        }
    }

    /// <summary>정의 (게임이 읽은 트리)에 이름표가 빈 노드 (Main · 보조)가 있는가 — 그 노드는 카드 · 줄 · breakpoint와 이어지지 않는다. O(노드 수)</summary>
    internal static bool HasUnlabeledNodes(BehaviorTreeDefinition definition)
    {
        for (int i = 0; i < definition.MainNodeCount; i++)
        {
            if (string.IsNullOrEmpty(definition.GetMainNode(i).NodeId))
                return true;
        }
        for (int i = 0; i < definition.AuxiliaryNodeCount; i++)
        {
            if (string.IsNullOrEmpty(definition.GetAuxiliaryNode(i).NodeId))
                return true;
        }
        return false;
    }

    /// <summary>정의에 그 이름표의 Main 노드가 있는가 (breakpoint는 Main 노드에 — 실행기가 트리 Id + 이름표로 찾는다). 빈 이름표면 false. O(노드 수)</summary>
    internal static bool HasMainNodeId(BehaviorTreeDefinition definition, string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId))
            return false;
        for (int i = 0; i < definition.MainNodeCount; i++)
        {
            if (string.Equals(definition.GetMainNode(i).NodeId, nodeId, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}