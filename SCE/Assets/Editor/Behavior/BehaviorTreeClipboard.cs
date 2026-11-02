using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>클립보드에서 읽은 것 — 카드 (복사한 부분 트리의 맨 위 노드 · 카드 자리 · 메모) 또는 Decorator · Service 줄 하나, 그리고 그 노드들이 고른 키의 정의. 노드는 새 객체 — 한 번만 붙인다</summary>
public sealed class BehaviorClipboardData
{
    /// <summary>복사한 카드들의 왼쪽 위 (복사한 그래프의 자리) — 복제는 여기서 조금 옮겨 놓는다</summary>
    public Vector2 Origin;

    /// <summary>카드 — 맨 위 노드들 (고른 카드끼리 이어진 아래 노드와 함께)</summary>
    public readonly List<BehaviorMainNode> Tops = new List<BehaviorMainNode>();

    /// <summary>카드 자리 — Origin에서 떨어진 만큼 (복사한 때의 이름표)</summary>
    public readonly List<BehaviorCardPlacement> Placements = new List<BehaviorCardPlacement>();

    /// <summary>메모 (복사한 때의 이름표)</summary>
    public readonly List<BehaviorNodeMemo> Memos = new List<BehaviorNodeMemo>();

    /// <summary>노드들이 고른 키의 정의 (복사한 트리의 것)</summary>
    public readonly List<BehaviorBlackboardKey> Keys = new List<BehaviorBlackboardKey>();

    /// <summary>줄 하나 (카드 대신) — 카드면 null</summary>
    public BehaviorAuxiliaryNode Row;

    public bool IsRow => Row != null;
}

/// <summary>
/// 그래프 복사 · 붙이기의 글 (GraphView가 클립보드에 둔다) — 쓰기 · 읽기 · 문서에 붙이기. GraphView 없이 시험한다.
///  - 카드: {"behaviorTreeClipboard": 1, "origin": {x, y}, "keys": [...], "loose": [...], "cards": [{id, x, y}], "memos": [{id, text}]}
///    loose = 고른 카드 가운데 부모가 고른 카드가 아닌 것마다 그 노드와, 고른 카드끼리 이어진 아래 노드 (고르지 않은 자식은 뺀다), 노드마다 그 Decorator · Service와 함께.
///    cards = 고른 카드의 자리 (origin에서 떨어진 만큼), memos = 고른 카드와 그 줄의 메모, keys = 그 노드들이 고른 키의 정의 (트리 JSON의 blackboard와 같은 형식).
///  - 줄 하나: {"behaviorTreeClipboard": 1, "keys": [...], "row": {그 줄만 단 노드}, "memos": [...]} — 줄은 홀로 트리 JSON에 쓸 수 없어, 가진 노드와 같은 형식의 새 노드에 달아 쓴다 (origin · cards는 없다).
/// 노드 · 자리 · 메모는 배치 파일과 같은 형식 · 같은 엄격함 (BehaviorTreeLayoutFile) — 머리가 없거나 다른 판 · 모르는 키 · 맞지 않는 값이면 읽지 않는다.
/// 붙이기: 카드는 이어지지 않은 노드로 (붙이기만으로 실행 순서가 바뀌지 않게), 겹치는 이름표는 새로 (BehaviorNodeIds.AssignIncoming — 메모 · 카드 자리가 따라감),
/// 붙일 트리에 없는 키는 정의째 더한다 (BehaviorBlackboardEditing.AddMissingKeys). Undo 기록은 부르는 쪽이 붙이기 전에 한다.
/// </summary>
public static class BehaviorTreeClipboard
{
    public const string HeaderKey = "behaviorTreeClipboard";
    public const int FormatVersion = 1;

    private const string OriginKey = "origin";
    private const string KeysKey = "keys";
    private const string LooseKey = "loose";
    private const string CardsKey = "cards";
    private const string MemosKey = "memos";
    private const string RowKey = "row";
    private const string TreeKeysKey = "blackboard"; // 트리 JSON의 키 목록 칸
    private const int HeaderSearchLength = 256;       // 머리를 찾는 앞쪽 글자 수 — 붙일 수 있는지 볼 때 긴 글을 다 읽지 않게

    #region 쓰기
    /// <summary>
    /// 고른 카드 → 글 (문서 차례 — 같은 고르기는 늘 같은 글). 문서에 없는 노드 · null은 뺀다. copied = 복사한 카드 수.
    /// 복사할 카드가 없거나 고른 카드에 저장할 수 없는 값 (유한하지 않은 수 · 등록되지 않은 노드)이 있으면 null과 까닭. 문서는 바꾸지 않는다. O(고른 카드 수 + 문서의 노드 수)
    /// </summary>
    public static string CopyCards(BehaviorTreeDocumentAsset document, IEnumerable<BehaviorMainNode> nodes, out int copied, out string error)
    {
        copied = 0;
        var chosen = new HashSet<BehaviorMainNode>();
        foreach (BehaviorMainNode node in nodes)
        {
            if (node != null)
                chosen.Add(node);
        }
        var mains = new List<BehaviorMainNode>();
        BehaviorTreeEditing.CollectMainNodes(document, mains);
        var copiedNodes = new List<BehaviorMainNode>();
        for (int i = 0; i < mains.Count; i++)
        {
            if (chosen.Contains(mains[i]))
                copiedNodes.Add(mains[i]);
        }
        if (copiedNodes.Count == 0)
        {
            error = "복사할 카드가 없음";
            return null;
        }

        var copiedSet = new HashSet<BehaviorMainNode>(copiedNodes);
        var childOfCopied = new HashSet<BehaviorMainNode>(); // 부모가 고른 카드 — 그 부모 아래로 함께 간다
        for (int i = 0; i < copiedNodes.Count; i++)
        {
            if (copiedNodes[i] is BehaviorComposite composite && composite.Children != null)
            {
                for (int c = 0; c < composite.Children.Length; c++)
                {
                    if (composite.Children[c] != null)
                        childOfCopied.Add(composite.Children[c]);
                }
            }
        }

        try
        {
            var loose = new JArray();
            var nodesWithRows = new List<BehaviorNode>(); // 고른 카드와 그 줄 — 키 · 메모
            for (int i = 0; i < copiedNodes.Count; i++)
            {
                BehaviorMainNode node = copiedNodes[i];
                if (childOfCopied.Contains(node) == false)
                    loose.Add(ChosenToken(node, copiedSet, document.Registry));
                AddWithRows(node, nodesWithRows);
            }

            Vector2 origin = TopLeft(document, copiedNodes);
            var cards = new JArray();
            var placed = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < copiedNodes.Count; i++)
            {
                string id = copiedNodes[i].NodeId;
                if (string.IsNullOrEmpty(id) == false && placed.Add(id) && document.TryGetPosition(id, out Vector2 position))
                    cards.Add(BehaviorTreeLayoutFile.CardToken(id, position - origin));
            }

            var root = new JObject
            {
                [HeaderKey] = FormatVersion,
                [OriginKey] = BehaviorTreeLayoutFile.PointToken(origin),
                [KeysKey] = KeysToken(document, nodesWithRows),
                [LooseKey] = loose,
                [CardsKey] = cards,
                [MemosKey] = MemosToken(document, nodesWithRows),
            };
            copied = copiedNodes.Count;
            error = null;
            return root.ToString(Formatting.Indented);
        }
        catch (JsonException exception)
        {
            error = exception.Message;
            return null;
        }
    }

    /// <summary>
    /// Decorator · Service 줄 하나 → 글. 줄은 owner와 같은 형식의 새 노드 (칸은 기본값, 그 줄만 — 만들 때 든 줄 · 자식은 뺀다)에 달아 쓴다 — 붙일 때는 줄만 꺼낸다.
    /// owner에 없는 줄 · 등록되지 않은 형식 · 저장할 수 없는 값이면 null과 까닭. O(줄의 칸 수 + 키 수)
    /// </summary>
    public static string CopyRow(BehaviorTreeDocumentAsset document, BehaviorMainNode owner, BehaviorAuxiliaryNode row, out string error)
    {
        if (owner == null || row == null || BehaviorTreeEditing.IndexOfAuxiliary(owner, row, out _) < 0)
        {
            error = "복사할 줄이 없음";
            return null;
        }
        BehaviorNodeRegistry registry = document.Registry;
        BehaviorNode created = null;
        string createError = "등록되지 않은 노드";
        if (registry.TryGetInfo(owner.GetType(), out BehaviorNodeRegistry.NodeInfo info) == false
            || registry.TryCreate(info.Key, info.Kind, out created, out createError) == false
            || created is BehaviorMainNode == false)
        {
            error = $"줄을 달 노드를 만들지 못함 — {createError}";
            return null;
        }
        var holder = (BehaviorMainNode)created;
        holder.Decorators = row is BehaviorDecorator decorator ? new[] { decorator } : Array.Empty<BehaviorDecorator>();
        holder.Services = row is BehaviorService service ? new[] { service } : Array.Empty<BehaviorService>();
        if (holder is BehaviorComposite composite)
            composite.Children = Array.Empty<BehaviorMainNode>();

        try
        {
            var nodes = new List<BehaviorNode> { row };
            var root = new JObject
            {
                [HeaderKey] = FormatVersion,
                [KeysKey] = KeysToken(document, nodes),
                [RowKey] = BehaviorTreeLayoutFile.NodeToken(holder, registry),
                [MemosKey] = MemosToken(document, nodes),
            };
            error = null;
            return root.ToString(Formatting.Indented);
        }
        catch (JsonException exception)
        {
            error = exception.Message;
            return null;
        }
    }

    /// <summary>
    /// top과 그 아래 고른 카드 → 트리 JSON과 같은 형식의 토큰 (Decorator · Service와 함께) — 고르지 않은 자식은 쓰지 않는다 (그 값도 읽지 않음).
    /// 쓰는 동안만 Composite의 자식 배열을 고른 자식으로 바꾸고, 쓰고 나면 (실패해도) 원래 배열로 둔다
    /// </summary>
    private static JToken ChosenToken(BehaviorMainNode top, HashSet<BehaviorMainNode> chosen, BehaviorNodeRegistry registry)
    {
        var originals = new List<KeyValuePair<BehaviorComposite, BehaviorMainNode[]>>();
        try
        {
            KeepChosenChildren(top, chosen, new HashSet<BehaviorMainNode>(), originals);
            return BehaviorTreeLayoutFile.NodeToken(top, registry);
        }
        finally
        {
            for (int i = 0; i < originals.Count; i++)
                originals[i].Key.Children = originals[i].Value;
        }
    }

    /// <summary>Composite의 자식 배열을 고른 자식만 남긴 새 배열로 바꾸고 (원래 배열은 originals에), 남긴 자식에서 같은 일 (한 노드는 한 번)</summary>
    private static void KeepChosenChildren(BehaviorMainNode node, HashSet<BehaviorMainNode> chosen, HashSet<BehaviorMainNode> visited,
                                           List<KeyValuePair<BehaviorComposite, BehaviorMainNode[]>> originals)
    {
        if (node is BehaviorComposite composite == false || composite.Children == null || visited.Add(node) == false)
            return;
        BehaviorMainNode[] children = composite.Children;
        var kept = new List<BehaviorMainNode>();
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null && chosen.Contains(children[i]))
                kept.Add(children[i]);
        }
        originals.Add(new KeyValuePair<BehaviorComposite, BehaviorMainNode[]>(composite, children));
        composite.Children = kept.ToArray();
        for (int i = 0; i < kept.Count; i++)
            KeepChosenChildren(kept[i], chosen, visited, originals);
    }

    /// <summary>노드와 그 Decorator · Service</summary>
    private static void AddWithRows(BehaviorMainNode node, List<BehaviorNode> into)
    {
        into.Add(node);
        if (node.Decorators != null)
        {
            for (int i = 0; i < node.Decorators.Length; i++)
            {
                if (node.Decorators[i] != null)
                    into.Add(node.Decorators[i]);
            }
        }
        if (node.Services != null)
        {
            for (int i = 0; i < node.Services.Length; i++)
            {
                if (node.Services[i] != null)
                    into.Add(node.Services[i]);
            }
        }
    }

    /// <summary>카드들의 왼쪽 위 — 자리가 있는 카드만 본다 (하나도 없으면 원점)</summary>
    private static Vector2 TopLeft(BehaviorTreeDocumentAsset document, List<BehaviorMainNode> nodes)
    {
        bool found = false;
        Vector2 topLeft = Vector2.zero;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (document.TryGetPosition(nodes[i].NodeId, out Vector2 position) == false)
                continue;
            topLeft = found ? Vector2.Min(topLeft, position) : position;
            found = true;
        }
        return topLeft;
    }

    /// <summary>노드들이 고른 키의 정의 (문서의 키 — 이름이 같은 첫 키, 없는 이름은 뺀다) → 트리 JSON의 blackboard와 같은 형식</summary>
    private static JToken KeysToken(BehaviorTreeDocumentAsset document, List<BehaviorNode> nodes)
    {
        var names = new List<string>();
        BehaviorBlackboardEditing.CollectKeyNames(nodes, names);
        var keys = new List<BehaviorBlackboardKey>();
        for (int i = 0; i < names.Count; i++)
        {
            BehaviorBlackboardKey key = BehaviorBlackboardEditing.FindKey(document.Tree, names[i]);
            if (key != null)
                keys.Add(key);
        }
        string json = BehaviorTreeLoader.Serialize(new BehaviorTreeAsset { Keys = keys.ToArray() }, document.Registry);
        return BehaviorTreeLayoutFile.ReadToken(json)[TreeKeysKey];
    }

    /// <summary>노드들의 메모 (빈 메모 · 같은 이름표는 한 번)</summary>
    private static JArray MemosToken(BehaviorTreeDocumentAsset document, List<BehaviorNode> nodes)
    {
        var memos = new JArray();
        var written = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < nodes.Count; i++)
        {
            string id = nodes[i].NodeId;
            if (string.IsNullOrEmpty(id) == false && document.TryGetMemo(id, out string text) && string.IsNullOrEmpty(text) == false && written.Add(id))
                memos.Add(BehaviorTreeLayoutFile.MemoToken(id, text));
        }
        return memos;
    }
    #endregion

    #region 읽기
    /// <summary>이 툴이 쓴 글로 보이는가 — 앞쪽에 머리 키가 있다 (GraphView가 붙이기를 켤지 볼 때 — 긴 글을 다 읽지 않는다). 읽을 수 있는지는 TryRead가 정한다. O(1)</summary>
    public static bool LooksLikeClipboard(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        int length = Math.Min(text.Length, HeaderSearchLength);
        int start = 0;
        while (start < length && char.IsWhiteSpace(text[start]))
            start++;
        return start < length && text[start] == '{' && text.IndexOf("\"" + HeaderKey + "\"", start, length - start, StringComparison.Ordinal) >= 0;
    }

    /// <summary>
    /// 글 → 붙일 것 (노드는 registry로 새로 만든다). 이 툴이 쓴 글이 아니거나 (머리 없음 · 다른 판) 키가 틀리거나 값이 맞지 않으면 false와 까닭 (줄 위치가 있으면 함께).
    /// 카드 (loose)와 줄 (row) 가운데 하나만 있어야 하고, 줄 글에는 카드 자리 (origin · cards)가 없어야 한다. O(글 길이)
    /// </summary>
    public static bool TryRead(string text, BehaviorNodeRegistry registry, out BehaviorClipboardData data, out string error)
    {
        data = null;
        if (LooksLikeClipboard(text) == false)
        {
            error = "Behavior Tree Editor에서 복사한 글이 아님";
            return false;
        }
        try
        {
            JObject root = BehaviorTreeLayoutFile.ObjectOf(BehaviorTreeLayoutFile.ReadToken(text), "클립보드");
            var result = new BehaviorClipboardData();
            bool hasHeader = false, hasLoose = false, hasRow = false, hasCardPlaces = false;
            foreach (JProperty property in root.Properties())
            {
                switch (property.Name)
                {
                    case HeaderKey:
                        if (IsKnownVersion(property.Value) == false)
                            throw new JsonException($"모르는 판 {property.Value.ToString(Formatting.None)}{BehaviorTreeLayoutFile.LineOf(property)} — 판 {FormatVersion}만 읽는다");
                        hasHeader = true;
                        break;
                    case OriginKey:
                        result.Origin = BehaviorTreeLayoutFile.ReadPoint(property.Value, OriginKey);
                        hasCardPlaces = true;
                        break;
                    case KeysKey:
                        ReadKeys(property.Value, registry, result.Keys);
                        break;
                    case LooseKey:
                        BehaviorTreeLayoutFile.ReadLoose(property.Value, registry, result.Tops);
                        hasLoose = true;
                        break;
                    case CardsKey:
                        BehaviorTreeLayoutFile.ReadCards(property.Value, result.Placements);
                        hasCardPlaces = true;
                        break;
                    case MemosKey:
                        BehaviorTreeLayoutFile.ReadMemos(property.Value, result.Memos);
                        break;
                    case RowKey:
                        result.Row = ReadRow(property.Value, registry);
                        hasRow = true;
                        break;
                    default:
                        throw new JsonException($"모르는 키 '{property.Name}'{BehaviorTreeLayoutFile.LineOf(property)}");
                }
            }
            if (hasHeader == false)
                throw new JsonException($"머리 '{HeaderKey}'가 없음");
            if (hasLoose == hasRow)
                throw new JsonException($"카드 ({LooseKey})와 줄 ({RowKey}) 가운데 하나만 있어야 한다");
            if (hasLoose && result.Tops.Count == 0)
                throw new JsonException("붙일 카드가 없음");
            if (hasRow && hasCardPlaces)
                throw new JsonException($"줄 ({RowKey})에는 {OriginKey} · {CardsKey}가 없어야 한다");
            data = result;
            error = null;
            return true;
        }
        catch (JsonException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    /// <summary>머리 값이 이 판인가 — 정수 FormatVersion (실수 · 글 · long 밖의 정수는 아님)</summary>
    private static bool IsKnownVersion(JToken token)
    {
        return token is JValue value && value.Value is long version && version == FormatVersion;
    }

    /// <summary>키 정의 — 트리 JSON의 blackboard와 같은 읽기 (BehaviorTreeLoader.ParseAsset). 같은 이름이 둘이면 읽기 실패</summary>
    private static void ReadKeys(JToken token, BehaviorNodeRegistry registry, List<BehaviorBlackboardKey> into)
    {
        BehaviorTreeLayoutFile.ArrayOf(token, KeysKey);
        string wrapped = new JObject { [TreeKeysKey] = token.DeepClone() }.ToString(Formatting.None);
        BehaviorTreeAsset tree = BehaviorTreeLoader.ParseAsset(wrapped, registry, out string error);
        if (tree == null)
            throw new JsonException($"{KeysKey}{BehaviorTreeLayoutFile.LineOf(token)}: {error}");
        var names = new HashSet<string>(StringComparer.Ordinal);
        BehaviorBlackboardKey[] keys = tree.Keys ?? Array.Empty<BehaviorBlackboardKey>();
        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i] == null)
                continue;
            if (names.Add(keys[i].Name ?? "") == false)
                throw new JsonException($"{KeysKey}: 키 '{keys[i].Name}'가 둘{BehaviorTreeLayoutFile.LineOf(token)}");
            into.Add(keys[i]);
        }
    }

    /// <summary>줄 하나 — 그 줄만 단 노드를 읽어 줄을 꺼낸다. Decorator · Service가 모두 합쳐 하나가 아니거나 자식이 있으면 읽기 실패</summary>
    private static BehaviorAuxiliaryNode ReadRow(JToken token, BehaviorNodeRegistry registry)
    {
        BehaviorMainNode holder = BehaviorTreeLayoutFile.ReadNode(token, RowKey, registry);
        int decorators = holder.Decorators?.Length ?? 0;
        int services = holder.Services?.Length ?? 0;
        bool hasChildren = holder is BehaviorComposite composite && composite.Children != null && composite.Children.Length > 0;
        BehaviorAuxiliaryNode row = decorators == 1 && services == 0 ? holder.Decorators[0]
            : services == 1 && decorators == 0 ? (BehaviorAuxiliaryNode)holder.Services[0]
            : null;
        if (row == null || hasChildren)
            throw new JsonException($"{RowKey}: 줄 하나만 단 노드여야 한다 (Decorator {decorators} · Service {services}{(hasChildren ? " · 자식 있음" : "")}){BehaviorTreeLayoutFile.LineOf(token)}");
        return row;
    }
    #endregion

    #region 붙이기
    /// <summary>
    /// 카드를 이어지지 않은 노드로 붙인다 — topLeft (그래프 좌표)가 복사한 카드들의 왼쪽 위 자리, 카드 사이 자리 · 선은 그대로 (자리가 없던 카드는 topLeft).
    /// 이름표는 겹치면 새로 (메모 · 카드 자리가 따라감), 붙일 트리에 없는 키는 정의째 더한다 (addedKeys — 더한 키, clashedKeys — 같은 이름 · 다른 형식이라 그대로 둔 키).
    /// 붙인 Main 노드를 반환한다 (맨 위 노드 차례, 그 아래는 깊이 우선). O(붙일 노드 수 + 문서의 노드 수)
    /// </summary>
    public static List<BehaviorMainNode> PasteCards(BehaviorTreeDocumentAsset document, BehaviorClipboardData data, Vector2 topLeft, List<string> addedKeys, List<string> clashedKeys)
    {
        var incoming = new List<BehaviorNode>();
        var subtree = new List<BehaviorNode>();
        for (int i = 0; i < data.Tops.Count; i++)
        {
            BehaviorTreeEditing.CollectSubtreeNodes(data.Tops[i], subtree);
            incoming.AddRange(subtree);
        }
        var offsets = new Dictionary<string, Vector2>(StringComparer.Ordinal);
        for (int i = 0; i < data.Placements.Count; i++)
            offsets[data.Placements[i].NodeId] = data.Placements[i].Position;
        var offsetOf = new Dictionary<BehaviorMainNode, Vector2>(); // 이름표를 새로 정하기 전에 — 옛 이름표로 찾는다
        for (int i = 0; i < incoming.Count; i++)
        {
            if (incoming[i] is BehaviorMainNode main && offsetOf.ContainsKey(main) == false)
                offsetOf.Add(main, main.NodeId != null && offsets.TryGetValue(main.NodeId, out Vector2 offset) ? offset : Vector2.zero);
        }

        Dictionary<string, string> ids = BehaviorNodeIds.AssignIncoming(document, incoming);
        BehaviorBlackboardEditing.AddMissingKeys(document.Tree, data.Keys, addedKeys, clashedKeys);

        var tops = new HashSet<BehaviorMainNode>(data.Tops);
        var pasted = new List<BehaviorMainNode>();
        var seen = new HashSet<BehaviorMainNode>();
        for (int i = 0; i < incoming.Count; i++)
        {
            if (incoming[i] is BehaviorMainNode main == false || seen.Add(main) == false)
                continue;
            pasted.Add(main);
            Vector2 position = topLeft + offsetOf[main];
            if (tops.Contains(main))
                BehaviorTreeEditing.AddLoose(document, main, position);
            else
                document.SetPosition(main.NodeId, position);
        }
        ApplyMemos(document, data, ids);
        return pasted;
    }

    /// <summary>
    /// 줄을 owner의 그 종류 목록 (Decorators · Services)의 index 자리에 붙인다 — index가 음수거나 목록 끝 밖이면 끝에. 이름표는 겹치면 새로 (메모가 따라감),
    /// 붙일 트리에 없는 키는 정의째 더한다. 붙인 줄을 반환한다. O(문서의 노드 수 + 목록 길이)
    /// </summary>
    public static BehaviorAuxiliaryNode PasteRow(BehaviorTreeDocumentAsset document, BehaviorClipboardData data, BehaviorMainNode owner, int index,
                                                 List<string> addedKeys, List<string> clashedKeys)
    {
        BehaviorAuxiliaryNode row = data.Row;
        Dictionary<string, string> ids = BehaviorNodeIds.AssignIncoming(document, new BehaviorNode[] { row });
        BehaviorBlackboardEditing.AddMissingKeys(document.Tree, data.Keys, addedKeys, clashedKeys);
        if (row is BehaviorDecorator decorator)
            BehaviorTreeEditing.AddDecorator(owner, decorator);
        else
            BehaviorTreeEditing.AddService(owner, (BehaviorService)row);
        int last = BehaviorTreeEditing.IndexOfAuxiliary(owner, row, out _);
        if (index >= 0 && index < last)
            BehaviorTreeEditing.MoveAuxiliary(owner, row, index - last);
        ApplyMemos(document, data, ids);
        return row;
    }

    /// <summary>복사한 메모를 지금 이름표로 (옛 이름표 → 지금 이름표 — 들어온 노드의 것만)</summary>
    private static void ApplyMemos(BehaviorTreeDocumentAsset document, BehaviorClipboardData data, Dictionary<string, string> ids)
    {
        for (int i = 0; i < data.Memos.Count; i++)
        {
            if (ids.TryGetValue(data.Memos[i].NodeId, out string id))
                document.SetMemo(id, data.Memos[i].Text);
        }
    }
    #endregion
}