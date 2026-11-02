using System;
using System.Collections.Generic;

/// <summary>
/// 노드 이름표 (BehaviorNode.NodeId) 정하기 — Behavior Tree Editor는 카드 자리 · 고른 노드를 이름표로 찾는다. 문서 안 (이어지지 않은 노드 포함)에서 겹치지 않게 한다.
/// 새 이름표 = 노드 키 + 번호 (Selector1 · ChaseTarget2) — 문서에서 쓰이지 않은 가장 작은 번호.
/// </summary>
public static class BehaviorNodeIds
{
    /// <summary>
    /// 비었거나 겹치는 이름표를 새로 정한다. 겹치면 앞의 노드가 이름표를 지킨다 — 차례는 BehaviorTreeEditing.CollectAllNodes (루트 부분 트리 → 이어지지 않은 부분 트리).
    /// 정한 수를 반환한다. O(노드 수 + 새 이름표마다 번호 시도)
    /// </summary>
    public static int FillMissing(BehaviorTreeDocumentAsset document)
    {
        var nodes = new List<BehaviorNode>();
        BehaviorTreeEditing.CollectAllNodes(document, nodes);

        var used = new HashSet<string>(StringComparer.Ordinal);
        var unnamed = new List<BehaviorNode>();
        for (int i = 0; i < nodes.Count; i++)
        {
            string id = nodes[i].NodeId;
            if (string.IsNullOrEmpty(id) || used.Add(id) == false)
                unnamed.Add(nodes[i]);
        }
        for (int i = 0; i < unnamed.Count; i++)
            unnamed[i].NodeId = Take(Prefix(document.Registry, unnamed[i]), used);
        return unnamed.Count;
    }

    /// <summary>새 노드의 이름표 — 문서에서 쓰이지 않은 키 + 번호. 노드는 아직 문서 밖이다. O(노드 수)</summary>
    public static string Next(BehaviorTreeDocumentAsset document, BehaviorNode node)
    {
        return Take(Prefix(document.Registry, node), UsedIds(document));
    }

    /// <summary>
    /// 문서 밖에서 들어오는 노드 (붙이기)의 이름표 — 문서와 앞서 들어온 노드에 없으면 그대로, 비었거나 겹치면 새로 (키 + 번호 — 문서의 노드가 이름표를 지킨다).
    /// 옛 이름표 → 지금 이름표를 반환한다 (옛 이름표가 빈 노드는 빼고, 같은 옛 이름표가 둘이면 앞의 노드 것). 노드는 아직 문서 밖이다. O(노드 수 + 새 이름표마다 번호 시도)
    /// </summary>
    public static Dictionary<string, string> AssignIncoming(BehaviorTreeDocumentAsset document, IReadOnlyList<BehaviorNode> incoming)
    {
        HashSet<string> used = UsedIds(document);
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < incoming.Count; i++)
        {
            BehaviorNode node = incoming[i];
            string old = node.NodeId;
            if (string.IsNullOrEmpty(old) || used.Add(old) == false)
                node.NodeId = Take(Prefix(document.Registry, node), used);
            if (string.IsNullOrEmpty(old) == false && ids.ContainsKey(old) == false)
                ids.Add(old, node.NodeId);
        }
        return ids;
    }

    /// <summary>문서의 노드가 쓰는 이름표 (빈 이름표는 빼고)</summary>
    private static HashSet<string> UsedIds(BehaviorTreeDocumentAsset document)
    {
        var nodes = new List<BehaviorNode>();
        BehaviorTreeEditing.CollectAllNodes(document, nodes);

        var used = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < nodes.Count; i++)
        {
            if (string.IsNullOrEmpty(nodes[i].NodeId) == false)
                used.Add(nodes[i].NodeId);
        }
        return used;
    }

    /// <summary>이름표 머리 — 등록 키, 등록되지 않은 형식이면 클래스 이름</summary>
    private static string Prefix(BehaviorNodeRegistry registry, BehaviorNode node)
    {
        return registry != null && registry.TryGetKey(node.GetType(), out string key) ? key : node.GetType().Name;
    }

    /// <summary>prefix + 번호 가운데 used에 없는 첫 이름표 — used에 더하고 반환한다</summary>
    private static string Take(string prefix, HashSet<string> used)
    {
        for (int number = 1; ; number++)
        {
            string id = prefix + number;
            if (used.Add(id))
                return id;
        }
    }
}