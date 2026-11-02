using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 편집 사본 (BehaviorTreeDocumentAsset)의 구조 바꾸기 — 잇기 · 끊기 · 노드 더하기 · 지우기 · Decorator · Service · 자식 차례 · 실행 순서 · 이름표 바꾸기 · 트리 Id 규칙 · 새 트리 뼈대.
/// 그래프 화면 (GraphView)과 따로 둔다 — 화면은 이 함수로 문서를 바꾸고 문서에서 다시 그린다. Undo 기록은 부르는 쪽이 바꾸기 전에 한다.
/// Main 노드의 자리는 셋 중 하나: 루트 (Tree.Root) · Composite의 자식 · 이어지지 않은 부분 트리의 맨 위 (LooseNodes). 한 노드 객체는 한 자리에만 둔다.
/// 배열 (Children · Decorators · Services)은 고칠 때마다 새로 만든다. 빈 칸 (null)은 건드리지 않고 건너뛴다 — 컴파일 검사가 알린다.
/// 훑기는 같은 노드 객체를 한 번만 본다 — 데이터가 깨져 한 객체가 두 자리에 있어도 끝난다.
/// </summary>
public static class BehaviorTreeEditing
{
    #region 모으기 · 찾기
    /// <summary>Main 노드 — 루트 부분 트리 (깊이 우선 — 실행 순서와 같은 차례), 그다음 이어지지 않은 부분 트리 (목록 차례). O(노드 수)</summary>
    public static void CollectMainNodes(BehaviorTreeDocumentAsset document, List<BehaviorMainNode> nodes)
    {
        nodes.Clear();
        var seen = new HashSet<BehaviorNode>();
        VisitMain(document.Tree.Root, seen, nodes);
        for (int i = 0; i < document.LooseNodes.Count; i++)
            VisitMain(document.LooseNodes[i], seen, nodes);
    }

    /// <summary>모든 노드 — Main 노드마다 그 노드, Decorator, Service, 그다음 자식 (컴파일의 차례). 루트 부분 트리 다음 이어지지 않은 부분 트리. O(노드 수)</summary>
    public static void CollectAllNodes(BehaviorTreeDocumentAsset document, List<BehaviorNode> nodes)
    {
        nodes.Clear();
        var seen = new HashSet<BehaviorNode>();
        VisitAll(document.Tree.Root, seen, nodes);
        for (int i = 0; i < document.LooseNodes.Count; i++)
            VisitAll(document.LooseNodes[i], seen, nodes);
    }

    /// <summary>top 부분 트리의 모든 노드 — CollectAllNodes와 같은 차례 (노드, Decorator, Service, 그다음 자식). 문서에 없어도 된다 (붙이기 전). nodes는 먼저 비운다. O(부분 트리 크기)</summary>
    public static void CollectSubtreeNodes(BehaviorMainNode top, List<BehaviorNode> nodes)
    {
        nodes.Clear();
        VisitAll(top, new HashSet<BehaviorNode>(), nodes);
    }

    /// <summary>노드의 부모 Composite — 루트 · 이어지지 않은 맨 위 · 문서에 없는 노드면 false. O(노드 수)</summary>
    public static bool TryGetParent(BehaviorTreeDocumentAsset document, BehaviorMainNode node, out BehaviorComposite parent)
    {
        var nodes = new List<BehaviorMainNode>();
        CollectMainNodes(document, nodes);
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is BehaviorComposite composite && node != null && IndexOf(composite.Children, node) >= 0)
            {
                parent = composite;
                return true;
            }
        }
        parent = null;
        return false;
    }

    /// <summary>그 이름표의 노드 (Main · Decorator · Service — 루트 부분 트리 다음 이어지지 않은 부분 트리 차례의 첫 노드). 없거나 빈 이름표면 false. O(노드 수)</summary>
    public static bool TryFindNode(BehaviorTreeDocumentAsset document, string nodeId, out BehaviorNode node)
    {
        node = null;
        if (string.IsNullOrEmpty(nodeId))
            return false;
        var nodes = new List<BehaviorNode>();
        CollectAllNodes(document, nodes);
        for (int i = 0; i < nodes.Count; i++)
        {
            if (string.Equals(nodes[i].NodeId, nodeId, StringComparison.Ordinal))
            {
                node = nodes[i];
                return true;
            }
        }
        return false;
    }

    /// <summary>Decorator · Service를 가진 Main 노드. 문서에 없으면 false. O(노드 수)</summary>
    public static bool TryGetOwner(BehaviorTreeDocumentAsset document, BehaviorAuxiliaryNode node, out BehaviorMainNode owner)
    {
        var nodes = new List<BehaviorMainNode>();
        CollectMainNodes(document, nodes);
        for (int i = 0; i < nodes.Count; i++)
        {
            if (node != null && IndexOfAuxiliary(nodes[i], node, out _) >= 0)
            {
                owner = nodes[i];
                return true;
            }
        }
        owner = null;
        return false;
    }

    /// <summary>top의 부분 트리에 node가 있는가 (top 자신 포함). O(부분 트리 크기)</summary>
    public static bool Contains(BehaviorMainNode top, BehaviorMainNode node)
    {
        if (top == null || node == null)
            return false;
        var nodes = new List<BehaviorMainNode>();
        VisitMain(top, new HashSet<BehaviorNode>(), nodes);
        return nodes.Contains(node);
    }

    /// <summary>문서에 있는 Main 노드인가 (루트 부분 트리 · 이어지지 않은 부분 트리). O(노드 수)</summary>
    public static bool IsInDocument(BehaviorTreeDocumentAsset document, BehaviorMainNode node)
    {
        if (node == null)
            return false;
        var nodes = new List<BehaviorMainNode>();
        CollectMainNodes(document, nodes);
        return nodes.Contains(node);
    }

    /// <summary>실행 순서에 드는가 — 루트 부분 트리에 있다. O(노드 수)</summary>
    public static bool IsInTree(BehaviorTreeDocumentAsset document, BehaviorMainNode node)
    {
        return Contains(document.Tree.Root, node);
    }

    /// <summary>parent와 child가 이어져 있는가 (parent null = 루트 자리). O(자식 수)</summary>
    public static bool IsConnected(BehaviorTreeDocumentAsset document, BehaviorMainNode parent, BehaviorMainNode child)
    {
        if (child == null)
            return false;
        if (parent == null)
            return document.Tree.Root == child;
        return parent is BehaviorComposite composite && IndexOf(composite.Children, child) >= 0;
    }
    #endregion

    #region 잇기 · 끊기
    /// <summary>
    /// parent 아래에 child를 이을 수 있는가 (parent null = 루트 자리). 루트 자리에는 Composite만, 부모는 Composite만.
    /// 고리 (자기 자신 · 자기 부분 트리 안의 부모) · 문서에 없는 노드는 안 된다. 이미 그 자리에 이어진 노드는 된다 (다시 잇기 — 그대로). O(노드 수)
    /// </summary>
    public static bool CanConnect(BehaviorTreeDocumentAsset document, BehaviorMainNode parent, BehaviorMainNode child)
    {
        if (IsInDocument(document, child) == false)
            return false;
        if (parent == null)
            return child is BehaviorComposite;
        return parent is BehaviorComposite && IsInDocument(document, parent) && Contains(child, parent) == false;
    }

    /// <summary>parent 아래에 이을 수 있는 노드 모두 (parent null = 루트 자리) — CanConnect가 참인 노드, 문서 차례 (CollectMainNodes). O(노드 수)</summary>
    public static void CollectConnectableChildren(BehaviorTreeDocumentAsset document, BehaviorMainNode parent, List<BehaviorMainNode> children)
    {
        children.Clear();
        var nodes = new List<BehaviorMainNode>();
        CollectMainNodes(document, nodes);
        if (parent == null)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] is BehaviorComposite)
                    children.Add(nodes[i]);
            }
            return;
        }
        if (parent is BehaviorComposite == false || nodes.Contains(parent) == false)
            return;

        // 고리가 되는 노드 = parent와 그 조상 (그 노드의 부분 트리에 parent가 든다)
        Dictionary<BehaviorMainNode, BehaviorComposite> parents = ParentMap(nodes);
        var ancestors = new HashSet<BehaviorMainNode>();
        BehaviorMainNode node = parent;
        while (node != null && ancestors.Add(node))
            node = parents.TryGetValue(node, out BehaviorComposite up) ? up : null;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (ancestors.Contains(nodes[i]) == false)
                children.Add(nodes[i]);
        }
    }

    /// <summary>child를 이을 수 있는 부모 Composite 모두 (CanConnect가 참인 것, 문서 차례). 루트 자리에 이을 수 있으면 true. 문서에 없는 노드면 비우고 false. O(노드 수)</summary>
    public static bool CollectConnectableParents(BehaviorTreeDocumentAsset document, BehaviorMainNode child, List<BehaviorComposite> parents)
    {
        parents.Clear();
        var nodes = new List<BehaviorMainNode>();
        CollectMainNodes(document, nodes);
        if (child == null || nodes.Contains(child) == false)
            return false;

        var subtree = new List<BehaviorMainNode>();
        VisitMain(child, new HashSet<BehaviorNode>(), subtree);
        var inside = new HashSet<BehaviorMainNode>(subtree);
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is BehaviorComposite composite && inside.Contains(composite) == false)
                parents.Add(composite);
        }
        return child is BehaviorComposite;
    }

    /// <summary>
    /// child를 지금 자리에서 떼어 parent의 자식 끝에 잇는다 (parent null = 루트 자리 — 있던 루트는 이어지지 않은 목록 끝으로).
    /// 자식 차례는 카드의 가로 자리로 다시 정한다 (SortChildren). 이미 그 자리면 바꾸지 않고 true, 이을 수 없으면 (CanConnect) 바꾸지 않고 false. O(노드 수)
    /// </summary>
    public static bool Connect(BehaviorTreeDocumentAsset document, BehaviorMainNode parent, BehaviorMainNode child)
    {
        if (CanConnect(document, parent, child) == false)
            return false;
        if (IsConnected(document, parent, child))
            return true; // 이미 그 자리 — 그대로

        Detach(document, child);
        if (parent == null)
        {
            if (document.Tree.Root != null)
                document.LooseNodes.Add(document.Tree.Root);
            document.Tree.Root = child;
            return true;
        }

        var composite = (BehaviorComposite)parent;
        composite.Children = Append(composite.Children, child);
        SortChildren(document, composite);
        return true;
    }

    /// <summary>parent와 child 사이를 끊는다 (parent null = 루트 자리) — child 부분 트리는 이어지지 않은 목록 끝으로 (카드 자리 그대로). 그 사이가 이어져 있지 않으면 false. O(자식 수)</summary>
    public static bool Disconnect(BehaviorTreeDocumentAsset document, BehaviorMainNode parent, BehaviorMainNode child)
    {
        if (child == null)
            return false;
        if (parent == null)
        {
            if (document.Tree.Root != child)
                return false;
            document.Tree.Root = null;
        }
        else
        {
            if (parent is BehaviorComposite composite == false || IndexOf(composite.Children, child) < 0)
                return false;
            composite.Children = Without(composite.Children, child);
        }
        document.LooseNodes.Add(child);
        return true;
    }
    #endregion

    #region 더하기 · 지우기
    /// <summary>새 노드를 이어지지 않은 목록 끝에 두고 카드 자리를 정한다. 이름표는 부르는 쪽이 먼저 정한다 (BehaviorNodeIds.Next)</summary>
    public static void AddLoose(BehaviorTreeDocumentAsset document, BehaviorMainNode node, Vector2 position)
    {
        document.LooseNodes.Add(node);
        document.SetPosition(node.NodeId, position);
    }

    /// <summary>
    /// 노드를 지운다 — 그 Decorator · Service도 함께. 자식 부분 트리는 이어지지 않은 목록 끝으로 (차례대로, 카드 자리 그대로), 노드의 카드 자리 · 노드와 그 Decorator · Service의 메모는 지운다.
    /// 문서에 없으면 false. O(노드 수)
    /// </summary>
    public static bool Remove(BehaviorTreeDocumentAsset document, BehaviorMainNode node)
    {
        if (node == null || Detach(document, node) == false)
            return false;

        if (node is BehaviorComposite composite && composite.Children != null)
        {
            for (int i = 0; i < composite.Children.Length; i++)
            {
                if (composite.Children[i] != null)
                    document.LooseNodes.Add(composite.Children[i]);
            }
            composite.Children = Array.Empty<BehaviorMainNode>();
        }
        document.RemovePosition(node.NodeId);
        document.RemoveMemo(node.NodeId);
        RemoveMemos(document, node.Decorators);
        RemoveMemos(document, node.Services);
        return true;
    }

    /// <summary>
    /// 노드 이름표 (NodeId)로 쓸 수 없는 까닭 — 비었거나 앞뒤에 공백이 있음 · 문서의 다른 노드 (이어지지 않은 노드 · Decorator · Service 포함)와 겹침 · 문서에 없는 노드.
    /// 쓸 수 있으면 (지금 이름표와 같을 때도) null. O(노드 수)
    /// </summary>
    public static string NodeIdError(BehaviorTreeDocumentAsset document, BehaviorNode node, string nodeId)
    {
        if (node == null)
            return "노드가 없습니다";
        if (string.Equals(node.NodeId, nodeId, StringComparison.Ordinal))
            return null;
        if (string.IsNullOrEmpty(nodeId) || nodeId.Trim() != nodeId)
            return "이름표는 비거나 앞뒤에 공백이 있으면 안 됩니다";
        var nodes = new List<BehaviorNode>();
        CollectAllNodes(document, nodes);
        if (nodes.Contains(node) == false)
            return "문서에 없는 노드입니다";
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] != node && string.Equals(nodes[i].NodeId, nodeId, StringComparison.Ordinal))
                return $"'{nodeId}' 이름표를 다른 노드가 쓰고 있습니다";
        }
        return null;
    }

    /// <summary>
    /// 노드 이름표 (NodeId)를 바꾼다 — 카드 자리 · 메모가 새 이름표로 따라간다. 같은 이름표면 그대로 true.
    /// 쓸 수 없는 이름표 (NodeIdError)면 바꾸지 않고 false와 까닭. O(노드 수)
    /// </summary>
    public static bool RenameNode(BehaviorTreeDocumentAsset document, BehaviorNode node, string nodeId, out string error)
    {
        error = NodeIdError(document, node, nodeId);
        if (error != null)
            return false;
        string old = node.NodeId;
        if (string.Equals(old, nodeId, StringComparison.Ordinal))
            return true;
        if (document.TryGetPosition(old, out Vector2 position))
        {
            document.RemovePosition(old);
            document.SetPosition(nodeId, position);
        }
        if (document.TryGetMemo(old, out string memo))
        {
            document.RemoveMemo(old);
            document.SetMemo(nodeId, memo);
        }
        node.NodeId = nodeId;
        return true;
    }

    /// <summary>
    /// 트리 Id로 쓸 수 없는 까닭 — 비었거나 앞뒤에 공백 · 파일 이름으로 쓸 수 없는 글자 (파일 이름 = Id) · 다른 트리의 Id나 파일 이름과 겹침 (대소문자를 가리지 않는다 — Windows · macOS의 파일 이름).
    /// takenNames = 다른 트리의 Id · 파일 이름 (확장자 없이) — 부르는 쪽이 모은다 (이 트리의 것은 뺀다). 쓸 수 있으면 null. O(이름 수)
    /// </summary>
    public static string TreeIdError(string id, IEnumerable<string> takenNames)
    {
        if (string.IsNullOrEmpty(id) || id.Trim() != id)
            return "Id는 비거나 앞뒤에 공백이 있으면 안 됩니다";
        if (EditorJsonFile.CanBeFileName(id) == false)
            return "파일 이름으로 쓸 수 없는 글자가 있습니다 — 파일 이름이 Id입니다";
        foreach (string name in takenNames)
        {
            if (string.Equals(name, id, StringComparison.Ordinal))
                return $"'{id}' Id를 다른 트리가 쓰고 있습니다";
            if (string.Equals(name, id, StringComparison.OrdinalIgnoreCase))
                return $"'{name}' Id를 쓰는 다른 트리가 있습니다 — 대소문자만 달라도 파일 이름이 겹칩니다";
        }
        return null;
    }

    /// <summary>새 트리의 뼈대 — 그 Id · 기본 설정 · 빈 Blackboard · 루트 Selector 아래 Idle 하나 (바로 컴파일되는 가장 작은 트리 — 쓰는 몬스터는 가만히 있는다). 이름표는 문서가 채운다</summary>
    public static BehaviorTreeAsset NewTree(string id)
    {
        return new BehaviorTreeAsset
        {
            Id = id,
            Root = new BehaviorSelector { Children = new BehaviorMainNode[] { new BehaviorIdleTask() } },
        };
    }

    private static void RemoveMemos<T>(BehaviorTreeDocumentAsset document, T[] auxiliaries) where T : BehaviorAuxiliaryNode
    {
        if (auxiliaries == null)
            return;
        for (int i = 0; i < auxiliaries.Length; i++)
        {
            if (auxiliaries[i] != null)
                document.RemoveMemo(auxiliaries[i].NodeId);
        }
    }

    /// <summary>노드를 지금 자리 (루트 · 부모의 자식 · 이어지지 않은 목록)에서 뗀다. 문서에 없으면 false</summary>
    private static bool Detach(BehaviorTreeDocumentAsset document, BehaviorMainNode node)
    {
        if (document.Tree.Root == node)
        {
            document.Tree.Root = null;
            return true;
        }
        if (document.LooseNodes.Remove(node))
            return true;
        if (TryGetParent(document, node, out BehaviorComposite parent))
        {
            parent.Children = Without(parent.Children, node);
            return true;
        }
        return false;
    }
    #endregion

    #region Decorator · Service
    public static void AddDecorator(BehaviorMainNode owner, BehaviorDecorator decorator)
    {
        owner.Decorators = Append(owner.Decorators, decorator);
    }

    public static void AddService(BehaviorMainNode owner, BehaviorService service)
    {
        owner.Services = Append(owner.Services, service);
    }

    /// <summary>Decorator · Service의 owner 목록 (Decorators · Services)에서의 자리와 그 목록의 길이 — owner에 없으면 -1</summary>
    public static int IndexOfAuxiliary(BehaviorMainNode owner, BehaviorAuxiliaryNode node, out int count)
    {
        if (node is BehaviorDecorator decorator)
        {
            count = owner.Decorators?.Length ?? 0;
            return IndexOf(owner.Decorators, decorator);
        }
        if (node is BehaviorService service)
        {
            count = owner.Services?.Length ?? 0;
            return IndexOf(owner.Services, service);
        }
        count = 0;
        return -1;
    }

    /// <summary>Decorator · Service 하나를 뺀다 — 그 메모도 지운다. owner에 없으면 false</summary>
    public static bool RemoveAuxiliary(BehaviorTreeDocumentAsset document, BehaviorMainNode owner, BehaviorAuxiliaryNode node)
    {
        if (node is BehaviorDecorator decorator && IndexOf(owner.Decorators, decorator) >= 0)
            owner.Decorators = Without(owner.Decorators, decorator);
        else if (node is BehaviorService service && IndexOf(owner.Services, service) >= 0)
            owner.Services = Without(owner.Services, service);
        else
            return false;
        document.RemoveMemo(node.NodeId);
        return true;
    }

    /// <summary>Decorator · Service를 같은 목록 안에서 delta 칸 옮긴다 (−1 위로 · +1 아래로). 목록 끝을 넘거나 owner에 없으면 false</summary>
    public static bool MoveAuxiliary(BehaviorMainNode owner, BehaviorAuxiliaryNode node, int delta)
    {
        if (node is BehaviorDecorator decorator)
            return TryMove(ref owner.Decorators, decorator, delta);
        if (node is BehaviorService service)
            return TryMove(ref owner.Services, service, delta);
        return false;
    }
    #endregion

    #region 차례
    /// <summary>
    /// Composite의 자식 차례를 카드의 가로 자리 (왼쪽부터 — Unreal처럼 차례 = 가로 자리)로 정한다. 가로 자리가 같으면 지금 차례.
    /// 빈 칸이 있거나 자리를 모르는 자식이 있으면 그대로 둔다. 바뀌었으면 true. O(자식 수 log 자식 수)
    /// </summary>
    public static bool SortChildren(BehaviorTreeDocumentAsset document, BehaviorComposite composite)
    {
        BehaviorMainNode[] children = composite.Children;
        if (children == null || children.Length < 2)
            return false;

        var x = new float[children.Length];
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] == null || document.TryGetPosition(children[i].NodeId, out Vector2 position) == false)
                return false;
            x[i] = position.x;
        }

        var order = new int[children.Length];
        for (int i = 0; i < order.Length; i++)
            order[i] = i;
        Array.Sort(order, (a, b) => x[a] != x[b] ? x[a].CompareTo(x[b]) : a.CompareTo(b));

        bool changed = false;
        var sorted = new BehaviorMainNode[children.Length];
        for (int i = 0; i < order.Length; i++)
        {
            sorted[i] = children[order[i]];
            changed |= order[i] != i;
        }
        if (changed)
            composite.Children = sorted;
        return changed;
    }

    /// <summary>실행 순서 — 루트부터 깊이 우선 (노드 다음 자식, 차례대로). 컴파일이 정하는 Main 노드 번호 (Index)와 같다. 이어지지 않은 노드는 들지 않는다. O(노드 수)</summary>
    public static void ExecutionOrder(BehaviorTreeAsset tree, Dictionary<BehaviorMainNode, int> order)
    {
        order.Clear();
        var nodes = new List<BehaviorMainNode>();
        VisitMain(tree.Root, new HashSet<BehaviorNode>(), nodes);
        for (int i = 0; i < nodes.Count; i++)
            order[nodes[i]] = i;
    }
    #endregion

    #region 도우미
    private static void VisitMain(BehaviorMainNode node, HashSet<BehaviorNode> seen, List<BehaviorMainNode> nodes)
    {
        if (node == null || seen.Add(node) == false)
            return;
        nodes.Add(node);
        if (node is BehaviorComposite composite && composite.Children != null)
        {
            for (int i = 0; i < composite.Children.Length; i++)
                VisitMain(composite.Children[i], seen, nodes);
        }
    }

    /// <summary>자식 → 부모 Composite (같은 객체가 두 자리에 있으면 처음 만난 부모)</summary>
    private static Dictionary<BehaviorMainNode, BehaviorComposite> ParentMap(List<BehaviorMainNode> nodes)
    {
        var parents = new Dictionary<BehaviorMainNode, BehaviorComposite>();
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is BehaviorComposite composite && composite.Children != null)
            {
                for (int c = 0; c < composite.Children.Length; c++)
                {
                    BehaviorMainNode child = composite.Children[c];
                    if (child != null && parents.ContainsKey(child) == false)
                        parents.Add(child, composite);
                }
            }
        }
        return parents;
    }

    private static void VisitAll(BehaviorMainNode node, HashSet<BehaviorNode> seen, List<BehaviorNode> nodes)
    {
        if (node == null || seen.Add(node) == false)
            return;
        nodes.Add(node);
        AddAuxiliaries(node.Decorators, seen, nodes);
        AddAuxiliaries(node.Services, seen, nodes);
        if (node is BehaviorComposite composite && composite.Children != null)
        {
            for (int i = 0; i < composite.Children.Length; i++)
                VisitAll(composite.Children[i], seen, nodes);
        }
    }

    private static void AddAuxiliaries<T>(T[] auxiliaries, HashSet<BehaviorNode> seen, List<BehaviorNode> nodes) where T : BehaviorAuxiliaryNode
    {
        if (auxiliaries == null)
            return;
        for (int i = 0; i < auxiliaries.Length; i++)
        {
            if (auxiliaries[i] != null && seen.Add(auxiliaries[i]))
                nodes.Add(auxiliaries[i]);
        }
    }

    private static int IndexOf<T>(T[] array, T item) where T : class
    {
        if (array == null || item == null)
            return -1;
        for (int i = 0; i < array.Length; i++)
        {
            if (ReferenceEquals(array[i], item))
                return i;
        }
        return -1;
    }

    private static T[] Append<T>(T[] array, T item)
    {
        int length = array?.Length ?? 0;
        var result = new T[length + 1];
        if (length > 0)
            Array.Copy(array, result, length);
        result[length] = item;
        return result;
    }

    /// <summary>item의 첫 자리를 뺀 새 배열 (없으면 그대로)</summary>
    private static T[] Without<T>(T[] array, T item) where T : class
    {
        int index = IndexOf(array, item);
        if (index < 0)
            return array;
        var result = new T[array.Length - 1];
        Array.Copy(array, 0, result, 0, index);
        Array.Copy(array, index + 1, result, index, array.Length - index - 1);
        return result;
    }

    /// <summary>배열의 item을 delta 칸 옮긴다 (빼서 그 자리에 넣는다 — 사이의 원소는 한 칸씩 밀린다). 없거나 끝을 넘으면 false. O(배열 길이)</summary>
    internal static bool TryMove<T>(ref T[] array, T item, int delta) where T : class
    {
        int index = IndexOf(array, item);
        int target = index + delta;
        if (index < 0 || delta == 0 || target < 0 || target >= array.Length)
            return false;
        var list = new List<T>(array);
        list.RemoveAt(index);
        list.Insert(target, item);
        array = list.ToArray();
        return true;
    }
    #endregion
}