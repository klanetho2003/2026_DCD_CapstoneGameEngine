using System;
using System.Collections.Generic;
using System.IO;

/// <summary>검사 줄의 자리 — 트리 전체 · 트리 설정 · Blackboard 키 · 노드 · 변형</summary>
public enum EBehaviorCheckPlace
{
    Tree,
    Settings,
    Blackboard,
    Node,
    Variant,
}

/// <summary>
/// 검사 줄 하나 — 무게 · 자리 (문서의 노드 · Blackboard 키 차례 · 변형 차례) · 칸 (JSON 키, 없으면 "") · 문장.
/// 변형 자리의 칸은 "name" 또는 변형이 바꾼 키의 이름 (KeyIndex = 트리에 있는 그 키의 차례, 없으면 -1)
/// </summary>
public readonly struct BehaviorTreeCheckItem
{
    public readonly EBehaviorMessageSeverity Severity;
    public readonly EBehaviorCheckPlace Place;
    public readonly BehaviorNode Node;
    public readonly int KeyIndex;
    public readonly int VariantIndex;
    public readonly string Field;
    public readonly string Text;

    public BehaviorTreeCheckItem(EBehaviorMessageSeverity severity, EBehaviorCheckPlace place, BehaviorNode node, int keyIndex, string field, string text)
        : this(severity, place, node, keyIndex, field, text, -1)
    {
    }

    public BehaviorTreeCheckItem(EBehaviorMessageSeverity severity, EBehaviorCheckPlace place, BehaviorNode node, int keyIndex, string field, string text, int variantIndex)
    {
        Severity = severity;
        Place = place;
        Node = node;
        KeyIndex = keyIndex;
        VariantIndex = variantIndex;
        Field = field ?? "";
        Text = text ?? "";
    }

    public bool IsError => Severity == EBehaviorMessageSeverity.Error;
}

/// <summary>
/// 편집 사본 하나의 검사 — 게임과 같은 컴파일 (BehaviorTreeCompiler — 편집 중인 노드가 아니라 저장용 JSON으로 다시 읽은 사본을 컴파일한다: 컴파일은 노드에 결과를 적고 한 번만 받는다)
/// + 변형 검사 (게임이 변형 정의를 만들 때와 같은 규칙 — BehaviorTreeVariants.CheckAll, 같은 사본으로)
/// + 편집기 검사 (저장할 수 없는 값 · 이어지지 않은 노드 · 배치 파일 · 트리 Id 규칙 · 겹침 · Id와 파일 이름). 에러는 저장을 막는다 (창의 저장).
/// 컴파일 문제의 노드는 이름표 (NodeId)로 문서의 노드에 맞춘다 — 문서의 이름표는 비지 않고 겹치지 않는다 (불러올 때 채움 · 이름표 바꾸기가 막음).
/// </summary>
public static class BehaviorTreeChecks
{
    private const string BlackboardPrefix = "blackboard[";
    private const string SettingsPath = "settings";
    private const string VariantPrefix = "variants[";
    private const string VariantValuesSuffix = "].blackboard";

    /// <summary>문서 하나의 검사 줄을 items에 담는다 (먼저 비운다). documents = 창이 연 모든 사본 (트리 Id 겹침). O(노드 수 + 사본 수)</summary>
    public static void Run(BehaviorTreeDocumentAsset document, IReadOnlyList<BehaviorTreeDocumentAsset> documents, List<BehaviorTreeCheckItem> items)
    {
        items.Clear();
        AddCompileItems(document, items);
        AddLooseItems(document, items);
        AddLayoutItems(document, items);
        AddIdItems(document, documents, items);
    }

    /// <summary>에러 수 · 경고 수</summary>
    public static void Count(IReadOnlyList<BehaviorTreeCheckItem> items, out int errors, out int warnings)
    {
        errors = 0;
        warnings = 0;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].IsError)
                errors++;
            else
                warnings++;
        }
    }

    private static void AddCompileItems(BehaviorTreeDocumentAsset document, List<BehaviorTreeCheckItem> items)
    {
        string json = document.SaveJson();
        if (json.StartsWith(BehaviorTreeDocumentAsset.UnsavableMark, StringComparison.Ordinal))
        {
            items.Add(new BehaviorTreeCheckItem(EBehaviorMessageSeverity.Error, EBehaviorCheckPlace.Tree, null, -1, "",
                "저장할 수 없는 값 — " + json.Substring(BehaviorTreeDocumentAsset.UnsavableMark.Length) + " (이 값을 고칠 때까지 컴파일 검사는 하지 않음)"));
            return;
        }

        BehaviorTreeAsset copy = BehaviorTreeLoader.ParseAsset(json, document.Registry, out string error);
        if (copy == null)
        {
            items.Add(new BehaviorTreeCheckItem(EBehaviorMessageSeverity.Error, EBehaviorCheckPlace.Tree, null, -1, "", "저장용 JSON을 다시 읽지 못함 — " + error));
            return;
        }

        var messages = new List<BehaviorCompileMessage>();
        BehaviorTreeCompiler.Compile(copy, document.Registry, messages);
        BehaviorTreeVariants.CheckAll(copy, messages);
        if (messages.Count == 0)
            return;
        Dictionary<string, BehaviorNode> nodesById = NodesById(document);
        for (int i = 0; i < messages.Count; i++)
            items.Add(ToItem(nodesById, document.Tree.Keys, messages[i]));
    }

    /// <summary>문서의 이름표 → 노드 (BehaviorTreeEditing.TryFindNode와 같은 노드 — 문서 차례의 첫 노드, 빈 이름표는 넣지 않는다). O(노드 수)</summary>
    private static Dictionary<string, BehaviorNode> NodesById(BehaviorTreeDocumentAsset document)
    {
        var nodes = new List<BehaviorNode>();
        BehaviorTreeEditing.CollectAllNodes(document, nodes);
        var nodesById = new Dictionary<string, BehaviorNode>(StringComparer.Ordinal);
        for (int i = 0; i < nodes.Count; i++)
        {
            string id = nodes[i].NodeId;
            if (string.IsNullOrEmpty(id) == false && nodesById.ContainsKey(id) == false)
                nodesById.Add(id, nodes[i]);
        }
        return nodesById;
    }

    /// <summary>
    /// 컴파일 · 변형 문제 → 검사 줄. 노드는 이름표로 문서의 노드에, Blackboard는 키 차례, 설정은 트리 설정, 변형은 변형 차례 (바꾼 키의 문제면 트리의 그 키 차례도 — keys),
    /// 그 밖은 트리 (자리 글을 문장 앞에)
    /// </summary>
    private static BehaviorTreeCheckItem ToItem(Dictionary<string, BehaviorNode> nodesById, BehaviorBlackboardKey[] keys, BehaviorCompileMessage message)
    {
        if (message.Node != null && string.IsNullOrEmpty(message.Node.NodeId) == false && nodesById.TryGetValue(message.Node.NodeId, out BehaviorNode node))
            return new BehaviorTreeCheckItem(message.Severity, EBehaviorCheckPlace.Node, node, -1, message.Field, message.Text);
        if (message.Path.StartsWith(BlackboardPrefix, StringComparison.Ordinal)
            && int.TryParse(message.Path.Substring(BlackboardPrefix.Length).TrimEnd(']'), out int keyIndex))
            return new BehaviorTreeCheckItem(message.Severity, EBehaviorCheckPlace.Blackboard, null, keyIndex, message.Field, message.Text);
        if (TryVariantIndex(message.Path, out int variantIndex, out bool isValue))
            return isValue
                ? new BehaviorTreeCheckItem(message.Severity, EBehaviorCheckPlace.Variant, null, KeyIndexOf(keys, message.Field), BehaviorVariantEditing.ValueField(message.Field), message.Text, variantIndex)
                : new BehaviorTreeCheckItem(message.Severity, EBehaviorCheckPlace.Variant, null, -1, message.Field, message.Text, variantIndex);
        if (message.Path == SettingsPath)
            return new BehaviorTreeCheckItem(message.Severity, EBehaviorCheckPlace.Settings, null, -1, message.Field, message.Text);
        string text = message.Path.Length > 0 ? message.Path + ": " + message.Text : message.Text;
        return new BehaviorTreeCheckItem(message.Severity, EBehaviorCheckPlace.Tree, null, -1, message.Field, text);
    }

    /// <summary>"variants[i]" · "variants[i].blackboard"의 변형 차례 — isValue = 바꾼 키 값의 문제 (뒤의 것)</summary>
    private static bool TryVariantIndex(string path, out int index, out bool isValue)
    {
        index = -1;
        isValue = false;
        if (path.StartsWith(VariantPrefix, StringComparison.Ordinal) == false)
            return false;
        string rest = path.Substring(VariantPrefix.Length);
        isValue = rest.EndsWith(VariantValuesSuffix, StringComparison.Ordinal);
        string number = isValue ? rest.Substring(0, rest.Length - VariantValuesSuffix.Length) : rest.TrimEnd(']');
        return int.TryParse(number, out index);
    }

    /// <summary>그 이름의 키 차례 (목록 차례의 첫 키) — 없으면 -1</summary>
    private static int KeyIndexOf(BehaviorBlackboardKey[] keys, string name)
    {
        for (int i = 0; keys != null && i < keys.Length; i++)
        {
            if (keys[i] != null && string.Equals(keys[i].Name, name, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    private static void AddLooseItems(BehaviorTreeDocumentAsset document, List<BehaviorTreeCheckItem> items)
    {
        for (int i = 0; i < document.LooseNodes.Count; i++)
        {
            if (document.LooseNodes[i] != null)
                items.Add(new BehaviorTreeCheckItem(EBehaviorMessageSeverity.Warning, EBehaviorCheckPlace.Node, document.LooseNodes[i], -1, "",
                    "이어지지 않은 노드 — 그 아래 노드와 함께 실행 순서에 들지 않음 (게임은 쓰지 않음)"));
        }
    }

    /// <summary>배치 — 배치 파일에 쓸 수 없는 값 (이어지지 않은 노드 — 에러, 저장을 막는다) · 읽지 못한 배치 파일 (경고 — 저장하면 덮어쓴다)</summary>
    private static void AddLayoutItems(BehaviorTreeDocumentAsset document, List<BehaviorTreeCheckItem> items)
    {
        string layout = document.LayoutJson();
        if (layout.StartsWith(BehaviorTreeDocumentAsset.UnsavableMark, StringComparison.Ordinal))
            items.Add(new BehaviorTreeCheckItem(EBehaviorMessageSeverity.Error, EBehaviorCheckPlace.Tree, null, -1, "",
                "배치 파일에 쓸 수 없는 값 (이어지지 않은 노드) — " + layout.Substring(BehaviorTreeDocumentAsset.UnsavableMark.Length)));
        if (document.LayoutError != null)
            items.Add(new BehaviorTreeCheckItem(EBehaviorMessageSeverity.Warning, EBehaviorCheckPlace.Tree, null, -1, "",
                $"배치 파일을 읽지 못함 — {document.LayoutError} (카드 자리 · 메모 · 이어지지 않은 노드 없이 열었음, 저장하면 그 파일을 덮어씀)"));
    }

    /// <summary>
    /// 트리 Id — 앞뒤 공백 · 파일 이름으로 쓸 수 없는 글자 (파일 이름 = Id), 다른 트리와 겹침 (대소문자를 가리지 않는다 — 파일 이름이 겹친다), Id와 파일 이름이 다름 (저장하면 이름을 바꾼다).
    /// 빈 Id는 컴파일이 알린다
    /// </summary>
    private static void AddIdItems(BehaviorTreeDocumentAsset document, IReadOnlyList<BehaviorTreeDocumentAsset> documents, List<BehaviorTreeCheckItem> items)
    {
        string id = document.Tree.Id;
        if (string.IsNullOrEmpty(id))
            return; // 빈 Id는 컴파일이 알린다
        if (id.Trim() != id)
            items.Add(new BehaviorTreeCheckItem(EBehaviorMessageSeverity.Error, EBehaviorCheckPlace.Tree, null, -1, "id",
                $"Id '{id}'의 앞뒤에 공백이 있음 — 파일 이름 · 주소로 쓰지 않는다"));
        if (EditorJsonFile.CanBeFileName(id) == false)
            items.Add(new BehaviorTreeCheckItem(EBehaviorMessageSeverity.Error, EBehaviorCheckPlace.Tree, null, -1, "id",
                $"Id '{id}'에 파일 이름으로 쓸 수 없는 글자가 있음 — 파일 이름이 Id라서 저장할 수 없음"));
        for (int i = 0; i < documents.Count; i++)
        {
            BehaviorTreeDocumentAsset other = documents[i];
            if (other == null || other == document || string.Equals(other.Tree.Id, id, StringComparison.OrdinalIgnoreCase) == false)
                continue;
            items.Add(new BehaviorTreeCheckItem(EBehaviorMessageSeverity.Error, EBehaviorCheckPlace.Tree, null, -1, "id",
                string.Equals(other.Tree.Id, id, StringComparison.Ordinal)
                    ? $"'{id}'를 다른 파일 ({FileLabel(other)})도 씀 — 게임은 Id (주소)로 트리를 찾으므로 하나만 쓰임"
                    : $"다른 파일 ({FileLabel(other)})의 Id '{other.Tree.Id}'와 대소문자만 다름 — 파일 이름 (= Id)이 겹쳐 저장할 수 없음"));
        }
        if (string.IsNullOrEmpty(document.FilePath) == false && string.Equals(Path.GetFileNameWithoutExtension(document.FilePath), id, StringComparison.Ordinal) == false)
            items.Add(new BehaviorTreeCheckItem(EBehaviorMessageSeverity.Warning, EBehaviorCheckPlace.Tree, null, -1, "id",
                $"Id '{id}'가 파일 이름 ({Path.GetFileName(document.FilePath)})과 다름 — 게임은 지금 파일 이름 (Addressable 주소)으로 찾음. 저장하면 파일 이름을 Id로 바꿈"));
    }

    private static string FileLabel(BehaviorTreeDocumentAsset document)
    {
        return string.IsNullOrEmpty(document.FilePath) ? "저장 전 새 트리" : Path.GetFileName(document.FilePath);
    }
}