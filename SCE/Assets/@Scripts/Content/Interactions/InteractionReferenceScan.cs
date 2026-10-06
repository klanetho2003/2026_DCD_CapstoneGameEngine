using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static Define;

/// <summary>상호작용이 상태 키를 읽는 위치 1곳 (StateValue 조건).</summary>
public readonly struct InteractionKeyRead
{
    public readonly string FilePath;
    public readonly string SetId;
    public readonly int InteractionIndex;
    public readonly string InteractionId;
    public readonly int ConditionIndex;  // Conditions 배열의 순번 (묶음 조건 안쪽이면 그 묶음의 순번)
    public readonly bool IsNested;       // Not 같은 묶음 조건 안쪽에 있는가
    public readonly string Key;
    public readonly EComparison Op;
    public readonly int Value;

    public InteractionKeyRead(string filePath, string setId, int interactionIndex, string interactionId,
                              int conditionIndex, bool isNested, string key, EComparison op, int value)
    {
        FilePath = filePath;
        SetId = setId;
        InteractionIndex = interactionIndex;
        InteractionId = interactionId;
        ConditionIndex = conditionIndex;
        IsNested = isNested;
        Key = key;
        Op = op;
        Value = value;
    }
}

/// <summary>상호작용이 신호를 내는 위치 1곳 (RaiseSignal 효과). 받는 쪽은 상태 규칙의 event: Signal + ruleKey.</summary>
public readonly struct InteractionSignalRaise
{
    public readonly string FilePath;
    public readonly string SetId;
    public readonly int InteractionIndex;
    public readonly string InteractionId;
    public readonly int EffectIndex;
    public readonly int SignalId;

    public InteractionSignalRaise(string filePath, string setId, int interactionIndex, string interactionId, int effectIndex, int signalId)
    {
        FilePath = filePath;
        SetId = setId;
        InteractionIndex = interactionIndex;
        InteractionId = interactionId;
        EffectIndex = effectIndex;
        SignalId = signalId;
    }
}

/// <summary>상호작용 폴더를 훑은 결과.</summary>
public sealed class InteractionReferenceScan
{
    public string Folder = "";
    public bool FolderExists;
    public readonly List<InteractionKeyRead> KeyReads = new List<InteractionKeyRead>();
    public readonly List<InteractionSignalRaise> SignalRaises = new List<InteractionSignalRaise>();
    public readonly List<string> UnreadableFiles = new List<string>(); // "파일 이름: 사유" — 이 파일들의 참조는 알 수 없다

    public void Clear()
    {
        Folder = "";
        FolderExists = false;
        KeyReads.Clear();
        SignalRaises.Clear();
        UnreadableFiles.Clear();
    }
}

/// <summary>
/// 상호작용 JSON에서 상태 시스템과 닿는 지점을 수집한다 — 읽기 전용, 상호작용 파일은 절대 쓰지 않는다.
/// 묶음 노드(Not 등) 안쪽은 필드 타입을 보고 내려가므로, 새 묶음 노드(And/Or 등)가 생겨도 이 코드를 고치지 않아도 된다.
/// </summary>
public static class InteractionReferenceScanner
{
    private const int MaxDepth = 16; // 잘못된 순환 참조에 대한 안전장치

    private static readonly Dictionary<Type, FieldInfo[]> s_nodeFields = new Dictionary<Type, FieldInfo[]>();

    private struct Owner
    {
        public string FilePath;
        public string SetId;
        public int InteractionIndex;
        public string InteractionId;
    }

    /// <summary>폴더 바로 아래의 JSON을 모두 읽어 수집한다. 파싱하지 못한 파일은 UnreadableFiles에 남긴다.</summary>
    public static void ScanFolder(string folder, InteractionReferenceScan result)
    {
        result.Clear();
        result.Folder = folder ?? "";
        result.FolderExists = string.IsNullOrEmpty(folder) == false && Directory.Exists(folder);
        if (result.FolderExists == false)
            return;

        string[] files = Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < files.Length; i++)
        {
            string path = files[i].Replace('\\', '/');
            string name = Path.GetFileName(path);

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                result.UnreadableFiles.Add($"{name}: {e.Message}");
                continue;
            }

            InteractionSetDefinition set = InteractionLoader.ParseRaw(text, out string error);
            if (set == null)
            {
                result.UnreadableFiles.Add($"{name}: {error ?? "빈 JSON"}");
                continue;
            }

            Collect(set, path, result);
        }
    }

    /// <summary>Set 하나에서 수집한다 (파일 IO 없음 — 테스트는 이 함수를 직접 부른다).</summary>
    public static void Collect(InteractionSetDefinition set, string filePath, InteractionReferenceScan result)
    {
        if (set == null || set.Interactions == null)
            return;

        for (int i = 0; i < set.Interactions.Length; i++)
        {
            InteractionDefinition definition = set.Interactions[i];
            if (definition == null)
                continue;

            var owner = new Owner { FilePath = filePath, SetId = set.Id, InteractionIndex = i, InteractionId = definition.Id };

            if (definition.Conditions != null)
            {
                for (int c = 0; c < definition.Conditions.Length; c++)
                    Visit(definition.Conditions[c], in owner, c, 0, result);
            }
            if (definition.EffectPrototypes != null)
            {
                for (int e = 0; e < definition.EffectPrototypes.Length; e++)
                    Visit(definition.EffectPrototypes[e], in owner, e, 0, result);
            }
        }
    }

    private static void Visit(object node, in Owner owner, int topIndex, int depth, InteractionReferenceScan result)
    {
        if (node == null || depth > MaxDepth)
            return;

        if (node is StateValueCondition stateValue)
        {
            result.KeyReads.Add(new InteractionKeyRead(owner.FilePath, owner.SetId, owner.InteractionIndex, owner.InteractionId,
                topIndex, depth > 0, stateValue.Key, stateValue.Op, stateValue.Value));
        }
        else if (node is RaiseSignalEffect raiseSignal)
        {
            result.SignalRaises.Add(new InteractionSignalRaise(owner.FilePath, owner.SetId, owner.InteractionIndex, owner.InteractionId,
                topIndex, raiseSignal.SignalId));
        }

        // 안쪽에 노드를 품은 필드(단일 또는 배열·목록)를 따라 내려간다
        FieldInfo[] fields = NodeFields(node.GetType());
        for (int i = 0; i < fields.Length; i++)
        {
            object value = fields[i].GetValue(node);
            if (value is InteractionCondition || value is InteractionEffect)
            {
                Visit(value, in owner, topIndex, depth + 1, result);
            }
            else if (value is IEnumerable children)
            {
                foreach (object child in children)
                    Visit(child, in owner, topIndex, depth + 1, result);
            }
        }
    }

    /// <summary>타입별로 "노드를 품은 필드"만 골라 캐시한다 — 리플렉션은 타입마다 한 번.</summary>
    private static FieldInfo[] NodeFields(Type type)
    {
        if (s_nodeFields.TryGetValue(type, out FieldInfo[] cached))
            return cached;

        var found = new List<FieldInfo>();
        for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            FieldInfo[] fields = current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int i = 0; i < fields.Length; i++)
            {
                if (HoldsNodes(fields[i].FieldType))
                    found.Add(fields[i]);
            }
        }

        FieldInfo[] result = found.ToArray();
        s_nodeFields[type] = result;
        return result;
    }

    private static bool HoldsNodes(Type fieldType)
    {
        return typeof(InteractionCondition).IsAssignableFrom(fieldType)
            || typeof(InteractionEffect).IsAssignableFrom(fieldType)
            || typeof(IEnumerable<InteractionCondition>).IsAssignableFrom(fieldType)
            || typeof(IEnumerable<InteractionEffect>).IsAssignableFrom(fieldType);
    }
}