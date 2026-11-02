using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 정리 배치 — 위에서 아래로 (Unreal 모양). 깊이마다 한 줄 (줄 높이 = 그 깊이 카드의 가장 큰 높이), 자식은 차례대로 왼쪽부터,
/// 부모는 첫 자식과 끝 자식의 가운데 위 (자기 부분 트리 폭 안으로). ROOT 카드가 맨 위 줄, 루트 부분 트리가 그 아래,
/// 이어지지 않은 부분 트리는 오른쪽에 목록 차례로 (루트 노드와 같은 줄부터).
/// 카드 크기는 부르는 쪽이 준다 (화면이 잰 크기 — 재기 전이면 어림). 카드 자리 (왼쪽 위)만 바꾼다 — 자식 차례는 그대로. O(노드 수)
/// 저장된 자리를 쓰는 처음 배치도 여기 — 가로 차례를 자식 차례에 맞추기 (MatchOrder)와 자리가 없는 카드만 놓기 (PlaceMissing). 배치 파일에 자리가 있는 트리를 열 때 있던 자리를 지킨다.
/// </summary>
public static class BehaviorTreeLayout
{
    public const float HorizontalGap = 40f; // 옆 카드 사이
    public const float VerticalGap = 60f;   // 줄 사이
    public const float LooseGap = 120f;     // 부분 트리 사이

    public static void Arrange(BehaviorTreeDocumentAsset document, Vector2 rootCardSize, Func<BehaviorMainNode, Vector2> sizeOf)
    {
        var walk = new Walk(document, sizeOf);
        BehaviorMainNode root = document.Tree.Root;
        walk.Measure(root, 1);
        for (int i = 0; i < document.LooseNodes.Count; i++)
            walk.Measure(document.LooseNodes[i], 1);
        walk.FixRows(rootCardSize.y);

        float right = rootCardSize.x;
        if (walk.IsMeasured(root))
        {
            float rootCenter = walk.Place(root, 0f, 1);
            float rootCardLeft = rootCenter - rootCardSize.x / 2f;
            document.RootCardPosition = new Vector2(rootCardLeft, 0f);
            right = Mathf.Max(walk.WidthOf(root), rootCardLeft + rootCardSize.x);
        }
        else
        {
            document.RootCardPosition = Vector2.zero;
        }

        float left = right + LooseGap;
        for (int i = 0; i < document.LooseNodes.Count; i++)
        {
            BehaviorMainNode loose = document.LooseNodes[i];
            if (walk.IsMeasured(loose) == false || walk.IsPlaced(loose))
                continue;
            walk.Place(loose, left, 1);
            left += walk.WidthOf(loose) + LooseGap;
        }
    }

    /// <summary>
    /// 자리가 없는 카드만 놓는다 — 있는 자리는 그대로 (배치 파일에 없는 노드: 밖에서 트리 JSON에 더한 노드 · 이름표가 바뀐 노드 등). 위에서 아래로:
    /// 트리 루트는 ROOT 카드 아래, 자식은 형제 차례대로 앞 형제의 오른쪽 (없으면 뒤 형제의 왼쪽, 자리가 있는 형제가 없으면 부모 카드의 가운데 아래 줄).
    /// 앞뒤 형제 사이가 좁으면 그 가운데 — 겹쳐도 가로 자리 차례 (자식 차례 — BehaviorTreeEditing.SortChildren)가 지금 차례와 같게.
    /// 이어지지 않은 부분 트리의 맨 위 노드는 놓인 카드들의 오른쪽 바깥, 루트 노드와 같은 줄. 놓은 카드 수를 반환한다. O(노드 수 × 자리가 없는 이어지지 않은 맨 위 노드 수)
    /// </summary>
    public static int PlaceMissing(BehaviorTreeDocumentAsset document, Vector2 rootCardSize, Func<BehaviorMainNode, Vector2> sizeOf)
    {
        var visited = new HashSet<BehaviorMainNode>();
        int placed = 0;
        float firstRowTop = document.RootCardPosition.y + rootCardSize.y + VerticalGap;
        BehaviorMainNode root = document.Tree.Root;
        if (root != null)
        {
            if (string.IsNullOrEmpty(root.NodeId) == false && document.TryGetPosition(root.NodeId, out _) == false)
            {
                document.SetPosition(root.NodeId, new Vector2(document.RootCardPosition.x + (rootCardSize.x - sizeOf(root).x) / 2f, firstRowTop));
                placed++;
            }
            placed += PlaceMissingChildren(document, root, sizeOf, visited);
        }

        for (int i = 0; i < document.LooseNodes.Count; i++)
        {
            BehaviorMainNode loose = document.LooseNodes[i];
            if (loose == null || visited.Contains(loose))
                continue;
            if (string.IsNullOrEmpty(loose.NodeId) == false && document.TryGetPosition(loose.NodeId, out _) == false)
            {
                document.SetPosition(loose.NodeId, new Vector2(RightEdge(document, rootCardSize, sizeOf) + LooseGap, firstRowTop));
                placed++;
            }
            placed += PlaceMissingChildren(document, loose, sizeOf, visited);
        }
        return placed;
    }

    /// <summary>부모 (자리가 있음) 아래의 자리가 없는 자식을 형제 차례에 맞게 놓고, 자식마다 그 아래로 이어 간다. 놓은 수. O(부분 트리 노드 수 × 형제 수)</summary>
    private static int PlaceMissingChildren(BehaviorTreeDocumentAsset document, BehaviorMainNode parent, Func<BehaviorMainNode, Vector2> sizeOf, HashSet<BehaviorMainNode> visited)
    {
        if (visited.Add(parent) == false || parent is BehaviorComposite composite == false || composite.Children == null)
            return 0;

        int placed = 0;
        BehaviorMainNode[] children = composite.Children;
        for (int i = 0; i < children.Length; i++)
        {
            BehaviorMainNode child = children[i];
            if (child == null || visited.Contains(child) || string.IsNullOrEmpty(child.NodeId) || document.TryGetPosition(child.NodeId, out _))
                continue;
            document.SetPosition(child.NodeId, PositionAmongSiblings(document, parent, children, i, sizeOf));
            placed++;
        }
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null)
                placed += PlaceMissingChildren(document, children[i], sizeOf, visited);
        }
        return placed;
    }

    /// <summary>
    /// 자식 index의 자리 — 자리가 있는 앞 형제의 오른쪽 (없으면 뒤 형제의 왼쪽, 둘 다 없으면 부모 카드의 가운데 아래 줄). 앞뒤 형제 사이가 좁으면 그 가운데 (겹쳐도 차례는 지킨다)
    /// </summary>
    private static Vector2 PositionAmongSiblings(BehaviorTreeDocumentAsset document, BehaviorMainNode parent, BehaviorMainNode[] children, int index, Func<BehaviorMainNode, Vector2> sizeOf)
    {
        Vector2 size = sizeOf(children[index]);
        bool hasBefore = TryGetSiblingPosition(document, children, index, -1, out BehaviorMainNode before, out Vector2 beforePosition);
        bool hasAfter = TryGetSiblingPosition(document, children, index, 1, out _, out Vector2 afterPosition);
        Vector2 position;
        if (hasBefore)
        {
            position = new Vector2(beforePosition.x + sizeOf(before).x + HorizontalGap, beforePosition.y);
        }
        else if (hasAfter)
        {
            position = new Vector2(afterPosition.x - size.x - HorizontalGap, afterPosition.y);
        }
        else
        {
            document.TryGetPosition(parent.NodeId, out Vector2 parentPosition);
            Vector2 parentSize = sizeOf(parent);
            position = new Vector2(parentPosition.x + (parentSize.x - size.x) / 2f, parentPosition.y + parentSize.y + VerticalGap);
        }
        if (hasBefore && hasAfter && position.x >= afterPosition.x)
            position.x = (beforePosition.x + afterPosition.x) / 2f; // 사이가 좁다 — 겹쳐도 차례는 지킨다
        return position;
    }

    /// <summary>
    /// 자리가 있는 자식의 가로 차례가 지금 자식 차례와 다른 Composite가 있는가 — 밖에서 트리 JSON의 자식 차례만 바뀐 경우.
    /// 그대로 두면 카드 하나를 옮길 때 자식 차례가 가로 자리로 다시 정해진다 (BehaviorTreeEditing.SortChildren). O(노드 수)
    /// </summary>
    public static bool HasOrderMismatch(BehaviorTreeDocumentAsset document)
    {
        var nodes = new List<BehaviorMainNode>();
        BehaviorTreeEditing.CollectMainNodes(document, nodes);
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is BehaviorComposite composite && composite.Children != null && OutOfOrderChildren(document, composite.Children).Count > 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 가로 차례를 지금 자식 차례에 맞춘다 — Composite마다 가로 자리가 차례대로인 가장 긴 자식 묶음은 그대로 두고, 나머지 자식을 그 사이 차례 자리로 옮긴다
    /// (PlaceMissing과 같은 자리 규칙 — 그 아래 카드도 함께 옮겨 모양 그대로). 옮긴 자식 수를 반환한다. O(노드 수 + 형제 수²)
    /// </summary>
    public static int MatchOrder(BehaviorTreeDocumentAsset document, Func<BehaviorMainNode, Vector2> sizeOf)
    {
        int moved = 0;
        var nodes = new List<BehaviorMainNode>();
        BehaviorTreeEditing.CollectMainNodes(document, nodes); // 위에서 아래로 — 부모를 먼저 맞춘다
        for (int n = 0; n < nodes.Count; n++)
        {
            if (nodes[n] is BehaviorComposite composite == false || composite.Children == null)
                continue;
            BehaviorMainNode[] children = composite.Children;
            List<int> outOfOrder = OutOfOrderChildren(document, children);
            if (outOfOrder.Count == 0)
                continue;

            var oldPositions = new Vector2[outOfOrder.Count];
            for (int k = 0; k < outOfOrder.Count; k++)
            {
                document.TryGetPosition(children[outOfOrder[k]].NodeId, out oldPositions[k]);
                document.RemovePosition(children[outOfOrder[k]].NodeId); // 맞는 형제 사이에 다시 놓는다
            }
            for (int k = 0; k < outOfOrder.Count; k++)
            {
                BehaviorMainNode child = children[outOfOrder[k]];
                Vector2 position = PositionAmongSiblings(document, composite, children, outOfOrder[k], sizeOf);
                document.SetPosition(child.NodeId, position);
                ShiftDescendants(document, child, position - oldPositions[k], new HashSet<BehaviorMainNode>());
                moved++;
            }
        }
        return moved;
    }

    /// <summary>
    /// 자리가 있는 자식 가운데 가로 차례가 맞지 않는 것 (자식 index 차례) — 가로 자리가 차례대로인 (같아도 됨 — SortChildren은 같은 자리면 지금 차례) 가장 긴 묶음 밖의 자식.
    /// 빈 칸이 있는 자식 목록은 SortChildren이 손대지 않으므로 (차례가 바뀌지 않는다) 맞출 것이 없다. O(형제 수²) — 형제 수가 작다
    /// </summary>
    private static List<int> OutOfOrderChildren(BehaviorTreeDocumentAsset document, BehaviorMainNode[] children)
    {
        var placed = new List<int>();
        var xs = new List<float>();
        if (Array.IndexOf(children, null) >= 0)
            return placed;
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null && document.TryGetPosition(children[i].NodeId, out Vector2 position))
            {
                placed.Add(i);
                xs.Add(position.x);
            }
        }

        var length = new int[xs.Count];
        var previous = new int[xs.Count];
        int last = -1;
        for (int k = 0; k < xs.Count; k++)
        {
            length[k] = 1;
            previous[k] = -1;
            for (int j = 0; j < k; j++)
            {
                if (xs[j] <= xs[k] && length[j] + 1 > length[k])
                {
                    length[k] = length[j] + 1;
                    previous[k] = j;
                }
            }
            if (last < 0 || length[k] > length[last])
                last = k;
        }

        var keep = new bool[xs.Count];
        for (int k = last; k >= 0; k = previous[k])
            keep[k] = true;
        var outOfOrder = new List<int>();
        for (int k = 0; k < xs.Count; k++)
        {
            if (keep[k] == false)
                outOfOrder.Add(placed[k]);
        }
        return outOfOrder;
    }

    /// <summary>노드 아래 카드 (자리가 있는 것)를 delta만큼 옮긴다 — 옮긴 부모를 따라 모양 그대로</summary>
    private static void ShiftDescendants(BehaviorTreeDocumentAsset document, BehaviorMainNode node, Vector2 delta, HashSet<BehaviorMainNode> visited)
    {
        if (visited.Add(node) == false || node is BehaviorComposite composite == false || composite.Children == null)
            return;
        foreach (BehaviorMainNode child in composite.Children)
        {
            if (child == null)
                continue;
            if (document.TryGetPosition(child.NodeId, out Vector2 position))
                document.SetPosition(child.NodeId, position + delta);
            ShiftDescendants(document, child, delta, visited);
        }
    }

    /// <summary>index의 앞 (step -1) 또는 뒤 (step 1)에서 자리가 있는 가장 가까운 형제</summary>
    private static bool TryGetSiblingPosition(BehaviorTreeDocumentAsset document, BehaviorMainNode[] children, int index, int step, out BehaviorMainNode sibling, out Vector2 position)
    {
        for (int i = index + step; i >= 0 && i < children.Length; i += step)
        {
            sibling = children[i];
            if (sibling != null && document.TryGetPosition(sibling.NodeId, out position))
                return true;
        }
        sibling = null;
        position = default;
        return false;
    }

    /// <summary>놓인 카드 (ROOT 카드 포함)의 오른쪽 끝 — 그래프 좌표. O(노드 수)</summary>
    private static float RightEdge(BehaviorTreeDocumentAsset document, Vector2 rootCardSize, Func<BehaviorMainNode, Vector2> sizeOf)
    {
        float right = document.RootCardPosition.x + rootCardSize.x;
        var nodes = new List<BehaviorMainNode>();
        BehaviorTreeEditing.CollectMainNodes(document, nodes);
        for (int i = 0; i < nodes.Count; i++)
        {
            if (document.TryGetPosition(nodes[i].NodeId, out Vector2 position))
                right = Mathf.Max(right, position.x + sizeOf(nodes[i]).x);
        }
        return right;
    }

    /// <summary>한 번의 배치 — 재기 (부분 트리 폭 · 줄 높이), 그다음 놓기. 같은 노드 객체는 처음 만난 자리에만 놓는다</summary>
    private sealed class Walk
    {
        private readonly BehaviorTreeDocumentAsset _document;
        private readonly Func<BehaviorMainNode, Vector2> _sizeOf;
        private readonly Dictionary<BehaviorMainNode, Vector2> _sizes = new Dictionary<BehaviorMainNode, Vector2>();
        private readonly Dictionary<BehaviorMainNode, float> _widths = new Dictionary<BehaviorMainNode, float>();
        private readonly Dictionary<BehaviorMainNode, float> _childSpans = new Dictionary<BehaviorMainNode, float>();
        private readonly HashSet<BehaviorMainNode> _placed = new HashSet<BehaviorMainNode>();
        private readonly List<float> _rowHeights = new List<float> { 0f };
        private readonly List<float> _rowTops = new List<float>();

        public Walk(BehaviorTreeDocumentAsset document, Func<BehaviorMainNode, Vector2> sizeOf)
        {
            _document = document;
            _sizeOf = sizeOf;
        }

        public bool IsMeasured(BehaviorMainNode node)
        {
            return node != null && _widths.ContainsKey(node);
        }

        public bool IsPlaced(BehaviorMainNode node)
        {
            return _placed.Contains(node);
        }

        public float WidthOf(BehaviorMainNode node)
        {
            return _widths[node];
        }

        /// <summary>부분 트리 폭 = 카드 폭과 자식 부분 트리 폭 합 (사이 간격 포함) 가운데 큰 것. 이미 잰 노드 · 빈 칸은 0 (놓지 않음)</summary>
        public float Measure(BehaviorMainNode node, int depth)
        {
            if (node == null || _widths.ContainsKey(node))
                return 0f;
            Vector2 size = _sizeOf(node);
            _sizes[node] = size;
            _widths[node] = size.x; // 재는 중 — 같은 객체를 다시 만나면 건너뛴다
            while (_rowHeights.Count <= depth)
                _rowHeights.Add(0f);
            _rowHeights[depth] = Mathf.Max(_rowHeights[depth], size.y);

            float span = 0f;
            bool first = true;
            if (node is BehaviorComposite composite && composite.Children != null)
            {
                for (int i = 0; i < composite.Children.Length; i++)
                {
                    BehaviorMainNode child = composite.Children[i];
                    if (child == null || _widths.ContainsKey(child))
                        continue;
                    float width = Measure(child, depth + 1);
                    span += first ? width : HorizontalGap + width;
                    first = false;
                }
            }
            _childSpans[node] = span;
            _widths[node] = Mathf.Max(size.x, span);
            return _widths[node];
        }

        /// <summary>줄 위치 — 0줄은 ROOT 카드</summary>
        public void FixRows(float rootCardHeight)
        {
            _rowHeights[0] = Mathf.Max(_rowHeights[0], rootCardHeight);
            float top = 0f;
            for (int depth = 0; depth < _rowHeights.Count; depth++)
            {
                _rowTops.Add(top);
                top += _rowHeights[depth] + VerticalGap;
            }
        }

        /// <summary>부분 트리를 left부터 폭 안에 놓는다 — 카드 가운데 x를 반환</summary>
        public float Place(BehaviorMainNode node, float left, int depth)
        {
            _placed.Add(node);
            Vector2 size = _sizes[node];
            float width = _widths[node];
            float span = _childSpans[node];

            float x = left + (width - size.x) / 2f;
            if (span > 0f && node is BehaviorComposite composite)
            {
                float childLeft = left + (width - span) / 2f;
                float firstCenter = float.NaN;
                float lastCenter = 0f;
                for (int i = 0; i < composite.Children.Length; i++)
                {
                    BehaviorMainNode child = composite.Children[i];
                    if (child == null || _placed.Contains(child) || _childSpans.ContainsKey(child) == false)
                        continue;
                    float center = Place(child, childLeft, depth + 1);
                    if (float.IsNaN(firstCenter))
                        firstCenter = center;
                    lastCenter = center;
                    childLeft += _widths[child] + HorizontalGap;
                }
                if (float.IsNaN(firstCenter) == false)
                    x = Mathf.Clamp((firstCenter + lastCenter) / 2f - size.x / 2f, left, left + width - size.x);
            }
            _document.SetPosition(node.NodeId, new Vector2(x, _rowTops[depth]));
            return x + size.x / 2f;
        }
    }
}