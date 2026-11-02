using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>배치 파일에서 읽은 것 — ROOT 카드 자리 · 카드 자리 · 메모 · 이어지지 않은 부분 트리의 맨 위 노드 (아래 노드와 함께)</summary>
public sealed class BehaviorTreeLayoutData
{
    public Vector2 RootCardPosition;
    public readonly List<BehaviorCardPlacement> Placements = new List<BehaviorCardPlacement>();
    public readonly List<BehaviorNodeMemo> Memos = new List<BehaviorNodeMemo>();
    public readonly List<BehaviorMainNode> LooseNodes = new List<BehaviorMainNode>();
}

/// <summary>
/// 편집기 배치 파일 — 트리 하나의 ROOT 카드 자리 · 카드 자리 · 메모 · 이어지지 않은 노드. 자리는 트리 폴더 아래 Editor/{트리 파일 이름}.layout.json —
/// 게임은 읽지 않는다 (Editor 폴더 — Addressables · 빌드에 들지 않음).
/// 키는 고정: "rootCard" {x, y} · "cards" [{id, x, y}] · "memos" [{id, text}] · "loose" [노드 — 트리 JSON과 같은 형식]. 모르는 키 · 같은 이름표 둘 · 유한하지 않은 자리는 읽기 실패.
/// 쓰는 차례는 문서 차례 (카드 = Main 노드 차례, 메모 = 모든 노드 차례) — 같은 배치는 늘 같은 글이 되어 미저장 판정에도 쓴다.
/// 이어지지 않은 노드는 트리 JSON과 같은 변환 (BehaviorTreeLoader.Serialize · ParseAsset — 등록소의 JSON 설정)을 쓴다: 그 노드를 루트로 둔 임시 트리로 감싼다.
/// 자리 · 카드 자리 · 메모 · 노드의 쓰기 · 읽기 조각 (internal)은 그래프 클립보드 (BehaviorTreeClipboard)도 같은 형식 · 같은 엄격함으로 쓴다.
/// </summary>
public static class BehaviorTreeLayoutFile
{
    public const string FolderName = "Editor";
    public const string Extension = ".layout.json";

    private const string RootCardKey = "rootCard";
    private const string CardsKey = "cards";
    private const string MemosKey = "memos";
    private const string LooseKey = "loose";
    private const string IdKey = "id";
    private const string XKey = "x";
    private const string YKey = "y";
    private const string TextKey = "text";
    private const string WrapperRootKey = "root"; // 임시 트리의 루트 칸 (트리 JSON의 "root")

    /// <summary>같은 객체 안의 같은 칸 이름은 에러 · 줄 정보를 남긴다 (트리 JSON 읽기와 같다)</summary>
    private static readonly JsonLoadSettings s_loadSettings = new JsonLoadSettings
    {
        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
        LineInfoHandling = LineInfoHandling.Load,
    };

    /// <summary>트리 파일의 배치 파일 경로 — {트리 파일의 폴더}/Editor/{트리 파일 이름 (확장자 없이)}.layout.json. 구분자는 '/'</summary>
    public static string PathFor(string treeFilePath)
    {
        string folder = (Path.GetDirectoryName(treeFilePath) ?? "").Replace('\\', '/');
        return $"{folder}/{FolderName}/{Path.GetFileNameWithoutExtension(treeFilePath)}{Extension}";
    }

    /// <summary>배치 파일 폴더 — 트리 폴더 아래 Editor</summary>
    public static string FolderFor(string treeFolder)
    {
        return $"{treeFolder}/{FolderName}";
    }

    #region 쓰기
    /// <summary>문서의 배치 → 글 (들여쓰기). 자리 · 메모가 없는 노드는 넣지 않는다. 이어지지 않은 노드에 저장할 수 없는 값이 있으면 JsonException. O(노드 수)</summary>
    public static string Write(BehaviorTreeDocumentAsset document)
    {
        return Write(document, null);
    }

    /// <summary>
    /// 저장할 수 없는 값이 있는 이어지지 않은 노드 (그 아래 노드와 함께)는 빼고 쓴다 — 창을 다시 만들 때 나머지 배치라도 넘기게.
    /// 뺀 노드는 skipped에 ("이름표: 까닭", 먼저 비운다). O(노드 수)
    /// </summary>
    public static string WriteSkippingUnsavable(BehaviorTreeDocumentAsset document, List<string> skipped)
    {
        skipped.Clear();
        return Write(document, skipped);
    }

    /// <summary>skipped가 null이면 저장할 수 없는 값에서 JsonException, 아니면 그 이어지지 않은 노드를 빼고 skipped에 남긴다</summary>
    private static string Write(BehaviorTreeDocumentAsset document, List<string> skipped)
    {
        var cards = new JArray();
        var mains = new List<BehaviorMainNode>();
        BehaviorTreeEditing.CollectMainNodes(document, mains);
        for (int i = 0; i < mains.Count; i++)
        {
            string id = mains[i].NodeId;
            if (document.TryGetPosition(id, out Vector2 position))
                cards.Add(CardToken(id, position));
        }

        var memos = new JArray();
        var nodes = new List<BehaviorNode>();
        BehaviorTreeEditing.CollectAllNodes(document, nodes);
        for (int i = 0; i < nodes.Count; i++)
        {
            string id = nodes[i].NodeId;
            if (document.TryGetMemo(id, out string text) && string.IsNullOrEmpty(text) == false)
                memos.Add(MemoToken(id, text));
        }

        var loose = new JArray();
        for (int i = 0; i < document.LooseNodes.Count; i++)
        {
            BehaviorMainNode node = document.LooseNodes[i];
            if (node == null)
                continue;
            if (skipped == null)
            {
                loose.Add(NodeToken(node, document.Registry));
                continue;
            }
            try
            {
                loose.Add(NodeToken(node, document.Registry));
            }
            catch (JsonException exception)
            {
                skipped.Add($"{node.NodeId}: {exception.Message}");
            }
        }

        var root = new JObject
        {
            [RootCardKey] = PointToken(document.RootCardPosition),
            [CardsKey] = cards,
            [MemosKey] = memos,
            [LooseKey] = loose,
        };
        return root.ToString(Formatting.Indented);
    }

    /// <summary>자리 {x, y}</summary>
    internal static JObject PointToken(Vector2 point)
    {
        return new JObject { [XKey] = point.x, [YKey] = point.y };
    }

    /// <summary>카드 자리 {id, x, y}</summary>
    internal static JObject CardToken(string nodeId, Vector2 position)
    {
        return new JObject { [IdKey] = nodeId, [XKey] = position.x, [YKey] = position.y };
    }

    /// <summary>메모 {id, text}</summary>
    internal static JObject MemoToken(string nodeId, string text)
    {
        return new JObject { [IdKey] = nodeId, [TextKey] = text };
    }

    /// <summary>노드 하나 (아래 노드와 함께) → 트리 JSON과 같은 형식의 토큰 — 그 노드를 루트로 둔 임시 트리를 저장해 루트 칸만 꺼낸다</summary>
    internal static JToken NodeToken(BehaviorMainNode node, BehaviorNodeRegistry registry)
    {
        string json = BehaviorTreeLoader.Serialize(new BehaviorTreeAsset { Root = node }, registry);
        return ReadToken(json)[WrapperRootKey];
    }

    /// <summary>글 → 토큰 — 날짜처럼 보이는 글도 글 그대로 (DateParseHandling.None), 같은 객체의 같은 칸 이름은 에러, 줄 정보. 뒤에 다른 글이 있으면 JsonReaderException</summary>
    internal static JToken ReadToken(string json)
    {
        using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None })
        {
            JToken token = JToken.ReadFrom(reader, s_loadSettings);
            while (reader.Read()) { } // 뒤에는 주석만
            return token;
        }
    }
    #endregion

    #region 읽기
    /// <summary>글 → 배치. 키가 틀리거나 값이 맞지 않으면 null과 까닭 (줄 위치가 있으면 함께). 이어지지 않은 노드는 registry로 만든다. O(글 길이)</summary>
    public static bool TryRead(string json, BehaviorNodeRegistry registry, out BehaviorTreeLayoutData data, out string error)
    {
        data = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "빈 글";
            return false;
        }
        try
        {
            JToken token = ReadToken(json);
            if (token is JObject root == false)
                throw new JsonException($"배치 파일은 객체여야 한다 ({token.Type})");

            var result = new BehaviorTreeLayoutData();
            foreach (JProperty property in root.Properties())
            {
                switch (property.Name)
                {
                    case RootCardKey:
                        result.RootCardPosition = ReadPoint(property.Value, RootCardKey);
                        break;
                    case CardsKey:
                        ReadCards(property.Value, result.Placements);
                        break;
                    case MemosKey:
                        ReadMemos(property.Value, result.Memos);
                        break;
                    case LooseKey:
                        ReadLoose(property.Value, registry, result.LooseNodes);
                        break;
                    default:
                        throw new JsonException($"모르는 키 '{property.Name}'{LineOf(property)}");
                }
            }
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

    /// <summary>{x, y} — 두 칸 모두 유한한 수</summary>
    internal static Vector2 ReadPoint(JToken token, string where)
    {
        JObject point = ObjectOf(token, where);
        float x = 0f, y = 0f;
        bool hasX = false, hasY = false;
        foreach (JProperty property in point.Properties())
        {
            switch (property.Name)
            {
                case XKey:
                    x = NumberOf(property.Value, where + ".x");
                    hasX = true;
                    break;
                case YKey:
                    y = NumberOf(property.Value, where + ".y");
                    hasY = true;
                    break;
                default:
                    throw new JsonException($"{where}: 모르는 키 '{property.Name}'{LineOf(property)}");
            }
        }
        if (hasX == false || hasY == false)
            throw new JsonException($"{where}: x · y가 모두 있어야 한다{LineOf(point)}");
        return new Vector2(x, y);
    }

    internal static void ReadCards(JToken token, List<BehaviorCardPlacement> into)
    {
        JArray cards = ArrayOf(token, CardsKey);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < cards.Count; i++)
        {
            string where = $"{CardsKey}[{i}]";
            JObject card = ObjectOf(cards[i], where);
            string id = null;
            float x = 0f, y = 0f;
            bool hasX = false, hasY = false;
            foreach (JProperty property in card.Properties())
            {
                switch (property.Name)
                {
                    case IdKey:
                        id = TextOf(property.Value, where + ".id");
                        break;
                    case XKey:
                        x = NumberOf(property.Value, where + ".x");
                        hasX = true;
                        break;
                    case YKey:
                        y = NumberOf(property.Value, where + ".y");
                        hasY = true;
                        break;
                    default:
                        throw new JsonException($"{where}: 모르는 키 '{property.Name}'{LineOf(property)}");
                }
            }
            if (string.IsNullOrEmpty(id) || hasX == false || hasY == false)
                throw new JsonException($"{where}: id · x · y가 모두 있어야 한다{LineOf(card)}");
            if (ids.Add(id) == false)
                throw new JsonException($"{where}: 이름표 '{id}'의 자리가 둘{LineOf(card)}");
            into.Add(new BehaviorCardPlacement(id, new Vector2(x, y)));
        }
    }

    internal static void ReadMemos(JToken token, List<BehaviorNodeMemo> into)
    {
        JArray memos = ArrayOf(token, MemosKey);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < memos.Count; i++)
        {
            string where = $"{MemosKey}[{i}]";
            JObject memo = ObjectOf(memos[i], where);
            string id = null, text = null;
            foreach (JProperty property in memo.Properties())
            {
                switch (property.Name)
                {
                    case IdKey:
                        id = TextOf(property.Value, where + ".id");
                        break;
                    case TextKey:
                        text = TextOf(property.Value, where + ".text");
                        break;
                    default:
                        throw new JsonException($"{where}: 모르는 키 '{property.Name}'{LineOf(property)}");
                }
            }
            if (string.IsNullOrEmpty(id) || text == null)
                throw new JsonException($"{where}: id · text가 모두 있어야 한다{LineOf(memo)}");
            if (ids.Add(id) == false)
                throw new JsonException($"{where}: 이름표 '{id}'의 메모가 둘{LineOf(memo)}");
            if (text.Length > 0)
                into.Add(new BehaviorNodeMemo(id, text));
        }
    }

    /// <summary>노드마다 트리 JSON과 같은 읽기 (ReadNode)</summary>
    internal static void ReadLoose(JToken token, BehaviorNodeRegistry registry, List<BehaviorMainNode> into)
    {
        JArray loose = ArrayOf(token, LooseKey);
        for (int i = 0; i < loose.Count; i++)
            into.Add(ReadNode(loose[i], $"{LooseKey}[{i}]", registry));
    }

    /// <summary>노드 하나 (아래 노드와 함께) — 트리 JSON과 같은 읽기 (BehaviorTreeLoader.ParseAsset): 그 노드를 루트로 둔 임시 트리로 감싼다. 새 객체를 만든다</summary>
    internal static BehaviorMainNode ReadNode(JToken token, string where, BehaviorNodeRegistry registry)
    {
        ObjectOf(token, where);
        string wrapped = new JObject { [WrapperRootKey] = token.DeepClone() }.ToString(Formatting.None);
        BehaviorTreeAsset tree = BehaviorTreeLoader.ParseAsset(wrapped, registry, out string error);
        if (tree == null)
            throw new JsonException($"{where}{LineOf(token)}: {error}");
        if (tree.Root == null)
            throw new JsonException($"{where}: 빈 노드{LineOf(token)}");
        return tree.Root;
    }

    internal static JObject ObjectOf(JToken token, string where)
    {
        if (token is JObject value)
            return value;
        throw new JsonException($"{where}: 객체여야 한다 ({token?.Type}){LineOf(token)}");
    }

    internal static JArray ArrayOf(JToken token, string where)
    {
        if (token is JArray value)
            return value;
        throw new JsonException($"{where}: 배열이어야 한다 ({token?.Type}){LineOf(token)}");
    }

    private static string TextOf(JToken token, string where)
    {
        if (token != null && token.Type == JTokenType.String)
            return (string)token;
        throw new JsonException($"{where}: 글이어야 한다 ({token?.Type}){LineOf(token)}");
    }

    /// <summary>유한한 수 (정수 · 실수) — float로</summary>
    private static float NumberOf(JToken token, string where)
    {
        if (token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float))
        {
            var number = (float)(double)token; // float 범위 밖이면 무한대 — 아래에서 거른다
            if (float.IsNaN(number) == false && float.IsInfinity(number) == false)
                return number;
        }
        throw new JsonException($"{where}: 유한한 수여야 한다 ({token?.Type}){LineOf(token)}");
    }

    internal static string LineOf(JToken token)
    {
        return token is IJsonLineInfo info && info.HasLineInfo() ? $" (줄 {info.LineNumber})" : "";
    }
    #endregion
}