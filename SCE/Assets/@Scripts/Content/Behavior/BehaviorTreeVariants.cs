using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>
/// 변형 하나 — 트리 JSON "variants"의 한 줄: {"name": 이름, "blackboard": {"키 이름": 값, …}}. 기반 트리 (그 파일)의 노드 · 설정을 그대로 쓰고 Blackboard 키 기본값만 바꾼다.
/// 몬스터 데이터의 트리 Id "기반 Id/이름"이 가리킨다 (BehaviorTreeVariants)
/// </summary>
[Serializable]
[Preserve] // JSON 읽기만 만드는 형식 — 빌드의 코드 줄이기에서 생성자가 지워지지 않게
[JsonObject(MemberSerialization.OptIn)]
public sealed class BehaviorTreeVariant
{
    [JsonProperty("name")] public string Name = "";

    /// <summary>바꾼 키 기본값 — 적힌 차례</summary>
    [JsonProperty("blackboard"), JsonConverter(typeof(BehaviorKeyOverridesJsonConverter))]
    public BehaviorKeyOverride[] Overrides = Array.Empty<BehaviorKeyOverride>();
}

/// <summary>
/// 변형이 바꾼 키 기본값 하나 — "키 이름": 값. Type = 적힌 값의 형식 (true · false = Bool, 정수 = Int, 소수 = Float, [x, y] = Vector2) — 그 형식의 칸만 뜻이 있다.
/// 기반 키와 형식이 맞아야 한다 — 정수는 Float 키에도 맞는다 (BehaviorTreeVariants.ValueError · FloatOf)
/// </summary>
[Serializable]
public struct BehaviorKeyOverride
{
    public string Key;
    public EBehaviorKeyType Type;
    public bool BoolValue;
    public int IntValue;
    public float FloatValue;
    public Vector2 VectorValue;
}

/// <summary>
/// 변형의 "blackboard" — {"키 이름": 값} 객체 ↔ 바꾼 키 기본값 목록 (적힌 차례). 값은 true · false · int 범위의 정수 · 유한한 소수 · [x, y]만 —
/// 그 밖의 모양은 JsonSerializationException (트리 로드가 실패로 알린다). 키가 기반에 있는지 · 형식이 맞는지는 BehaviorTreeVariants가 본다
/// </summary>
[Preserve]
public sealed class BehaviorKeyOverridesJsonConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(BehaviorKeyOverride[]);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        JToken token = JToken.Load(reader);
        if (token.Type != JTokenType.Object)
            throw new JsonSerializationException($"변형의 blackboard는 {{\"키 이름\": 값}} 객체여야 한다 ({BehaviorValueJsonConverter.Where(token)})");
        var values = new List<BehaviorKeyOverride>();
        foreach (JProperty property in ((JObject)token).Properties())
            values.Add(Read(property.Name, property.Value));
        return values.ToArray();
    }

    private static BehaviorKeyOverride Read(string key, JToken token)
    {
        var value = new BehaviorKeyOverride { Key = key };
        switch (token.Type)
        {
            case JTokenType.Boolean:
                value.Type = EBehaviorKeyType.Bool;
                value.BoolValue = BehaviorValueJsonConverter.ReadBool(token);
                break;
            case JTokenType.Integer:
                value.Type = EBehaviorKeyType.Int;
                value.IntValue = BehaviorValueJsonConverter.ReadInt(token);
                break;
            case JTokenType.Float:
                value.Type = EBehaviorKeyType.Float;
                value.FloatValue = BehaviorValueJsonConverter.ReadFloat(token);
                break;
            case JTokenType.Array:
                value.Type = EBehaviorKeyType.Vector2;
                value.VectorValue = BehaviorValueJsonConverter.ReadVector2(token);
                break;
            default:
                throw new JsonSerializationException($"변형의 값은 true · false · 수 · [x, y]여야 한다: '{key}' ({BehaviorValueJsonConverter.Where(token)})");
        }
        return value;
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        var values = (BehaviorKeyOverride[])value ?? Array.Empty<BehaviorKeyOverride>();
        writer.WriteStartObject();
        for (int i = 0; i < values.Length; i++)
        {
            writer.WritePropertyName(values[i].Key ?? "");
            switch (values[i].Type)
            {
                case EBehaviorKeyType.Bool:
                    writer.WriteValue(values[i].BoolValue);
                    break;
                case EBehaviorKeyType.Int:
                    writer.WriteValue(values[i].IntValue);
                    break;
                case EBehaviorKeyType.Float:
                    BehaviorValueJsonConverter.WriteFloat(writer, values[i].FloatValue);
                    break;
                case EBehaviorKeyType.Vector2:
                    BehaviorValueJsonConverter.WriteVector2(writer, values[i].VectorValue);
                    break;
                default:
                    throw new JsonSerializationException($"변형의 값 형식 {values[i].Type}은 쓸 수 없다 ('{values[i].Key}', path '{writer.Path}')");
            }
        }
        writer.WriteEndObject();
    }
}

/// <summary>
/// 변형 트리 — 트리 JSON의 variants (BehaviorTreeVariant). 몬스터 데이터의 트리 Id가 "기반 Id/변형 이름"이면 (트리 Id는 파일 이름이라 '/'가 없다)
/// 기반 정의의 노드 · 칸 배치 · 설정을 함께 쓰고 Blackboard 배치만 따로 만든다 (변형마다 키 정의 복사 하나 — 몬스터는 그 기본값으로 시작한다).
/// 검사는 게임 (변형 정의를 만들 때 — Build)과 편집기 (모든 변형 — CheckAll)가 같이 쓴다. 문제 자리: 이름 = "variants[i]" · name, 키 값 = "variants[i].blackboard" · 키 이름
/// </summary>
public static class BehaviorTreeVariants
{
    /// <summary>기반 Id와 변형 이름 사이 — 트리 Id는 파일 이름이라 이 글자가 없다</summary>
    public const char Separator = '/';

    private const string VariantsField = "variants";
    private const string NameField = "name";
    private const string BlackboardField = "blackboard";
    private const int NamesShown = 10; // 없는 변형 문장에 적는 변형 이름 수

    /// <summary>"기반 Id/변형 이름"이면 첫 '/'에서 나눠 true (변형 이름이 비어도), 아니면 baseId = treeId · variantName = "" · false. O(글 길이)</summary>
    public static bool TrySplit(string treeId, out string baseId, out string variantName)
    {
        int at = string.IsNullOrEmpty(treeId) ? -1 : treeId.IndexOf(Separator);
        if (at < 0)
        {
            baseId = treeId;
            variantName = "";
            return false;
        }
        baseId = treeId.Substring(0, at);
        variantName = treeId.Substring(at + 1);
        return true;
    }

#if UNITY_EDITOR // 편집기만 쓴다 — 플레이어 빌드에서 빠진다
    /// <summary>트리 Id의 기반 부분 — 변형 Id면 '/' 앞, 아니면 그대로</summary>
    public static string BaseIdOf(string treeId)
    {
        TrySplit(treeId, out string baseId, out _);
        return baseId;
    }

    /// <summary>몬스터 데이터의 트리 Id가 그 트리 (기반 그대로 또는 그 트리의 변형)를 가리키는가 — 대소문자까지. O(글 길이)</summary>
    public static bool BelongsTo(string treeId, string baseTreeId)
    {
        if (string.IsNullOrEmpty(treeId) || string.IsNullOrEmpty(baseTreeId) || treeId.StartsWith(baseTreeId, StringComparison.Ordinal) == false)
            return false;
        return treeId.Length == baseTreeId.Length || treeId[baseTreeId.Length] == Separator;
    }

    /// <summary>몬스터 데이터가 변형을 가리키는 이름 — "트리 Id/변형 이름"</summary>
    public static string FullId(string treeId, string variantName)
    {
        return treeId + Separator + variantName;
    }
#endif

    /// <summary>변형 이름으로 쓸 수 없는 까닭 — 비었거나 앞뒤 공백 · '/'. 쓸 수 있으면 null (다른 변형과 겹침은 부르는 쪽이 본다)</summary>
    public static string NameError(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Trim() != name)
            return $"비었거나 앞뒤에 공백이 있음 ('{name}')";
        if (name.IndexOf(Separator) >= 0)
            return $"'{Separator}'를 쓸 수 없음 — 몬스터 데이터의 트리 Id가 '트리 Id{Separator}변형 이름'이라서 ('{name}')";
        return null;
    }

    /// <summary>그 이름의 변형 자리 (대소문자까지, 목록 차례의 첫 변형). 없으면 -1. O(변형 수)</summary>
    public static int IndexOf(BehaviorTreeVariant[] variants, string name)
    {
        if (variants == null || name == null)
            return -1;
        for (int i = 0; i < variants.Length; i++)
        {
            if (variants[i] != null && string.Equals(variants[i].Name, name, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    /// <summary>변형이 그 키의 기본값을 바꿀 수 없는 까닭 — Creature 키 (기본값 없음) · 처음 값 출처가 있는 키 (출처가 정함). 바꿀 수 있으면 null</summary>
    public static string LockedReason(BehaviorBlackboardKey key)
    {
        if (key.Type == EBehaviorKeyType.Creature)
            return "Creature 키는 기본값이 없어 바꿀 수 없음 (비어서 시작한다)";
        if (key.HasSeed)
            return $"처음 값 출처 '{key.Seed}'가 정하는 키 — 기본값을 바꿀 수 없음";
        return null;
    }

    /// <summary>값을 그 키의 기본값으로 쓸 수 없는 까닭 — 바꿀 수 없는 키 (LockedReason) · 형식이 다름 (정수는 Float 키에도 맞음) · 유한하지 않은 수. 쓸 수 있으면 null</summary>
    public static string ValueError(BehaviorBlackboardKey key, in BehaviorKeyOverride value)
    {
        string locked = LockedReason(key);
        if (locked != null)
            return locked;
        if (value.Type != key.Type && (value.Type != EBehaviorKeyType.Int || key.Type != EBehaviorKeyType.Float))
            return $"키는 {key.Type} — 적은 값은 {value.Type}";
        if (key.Type == EBehaviorKeyType.Float && IsFinite(FloatOf(value)) == false)
            return $"유한한 수여야 한다 ({FloatOf(value)})";
        if (key.Type == EBehaviorKeyType.Vector2 && (IsFinite(value.VectorValue.x) == false || IsFinite(value.VectorValue.y) == false))
            return $"유한한 수여야 한다 ({value.VectorValue.x}, {value.VectorValue.y})";
        return null;
    }

    /// <summary>Float 키에 쓸 수 — 정수로 적었으면 그 정수</summary>
    public static float FloatOf(in BehaviorKeyOverride value)
    {
        return value.Type == EBehaviorKeyType.Int ? value.IntValue : value.FloatValue;
    }

#if UNITY_EDITOR // 편집기 검사만 부른다 — 플레이어 빌드에서 빠진다
    /// <summary>
    /// 모든 변형의 검사 (편집기 — 게임은 몬스터가 쓰는 변형만 Build가 본다): 이름 (NameError · 다른 변형과 겹침 — 대소문자를 가리지 않는다) ·
    /// 키 값 (빈 이름 · 같은 키 두 번 · Blackboard에 없는 키 · ValueError). 문제는 모두 에러. O(변형 수 × (변형 수 + 값 수 × 키 수))
    /// </summary>
    public static void CheckAll(BehaviorTreeAsset asset, List<BehaviorCompileMessage> messages)
    {
        BehaviorTreeVariant[] variants = asset?.Variants;
        if (variants == null)
            return;
        for (int i = 0; i < variants.Length; i++)
        {
            BehaviorTreeVariant variant = variants[i];
            if (variant == null)
            {
                Error(messages, PathOf(i), "", "빈 칸 (null)");
                continue;
            }
            string nameError = NameError(variant.Name);
            if (nameError != null)
            {
                Error(messages, PathOf(i), NameField, nameError);
            }
            else
            {
                for (int j = 0; j < i; j++)
                {
                    if (variants[j] != null && string.Equals(variants[j].Name, variant.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        Error(messages, PathOf(i), NameField, $"'{variant.Name}'가 다른 변형 '{variants[j].Name}'와 겹침 (대소문자를 가리지 않는다)");
                        break;
                    }
                }
            }
            CheckValues(asset.Keys, variant, i, messages);
        }
    }
#endif

    /// <summary>
    /// 변형 정의 — 기반 정의 (컴파일된 기반 트리)에서 그 이름의 변형 (대소문자까지)을 찾아 검사하고 (CheckAll과 같은 규칙 — 다른 변형과의 겹침만 빼고)
    /// 키 기본값을 바꾼 Blackboard 배치의 정의를 만든다 (노드 · 칸 배치 · 설정은 기반 정의와 함께 쓴다). 없거나 에러면 null — 문제는 messages에 더한다. O(키 수 + 값 수 × 키 수)
    /// </summary>
    public static BehaviorTreeDefinition Build(BehaviorTreeDefinition baseDefinition, string variantName, List<BehaviorCompileMessage> messages)
    {
        BehaviorTreeAsset asset = baseDefinition.Asset;
        int index = IndexOf(asset.Variants, variantName);
        if (index < 0)
        {
            Error(messages, "", VariantsField, $"변형 '{variantName}' 없음 — 트리 '{asset.Id}'의 변형: {NamesText(asset.Variants)}");
            return null;
        }
        BehaviorTreeVariant variant = asset.Variants[index];
        int start = messages.Count;
        string nameError = NameError(variant.Name);
        if (nameError != null)
            Error(messages, PathOf(index), NameField, nameError);
        CheckValues(asset.Keys, variant, index, messages);
        if (messages.Count > start)
            return null;
        return baseDefinition.WithVariant(baseDefinition.Blackboard.WithDefaults(variant.Overrides), variant.Name);
    }

    /// <summary>변형 하나의 키 값 검사 — 빈 이름 · 같은 키 두 번 · Blackboard에 없는 키 (목록 차례의 첫 키로 본다) · ValueError</summary>
    private static void CheckValues(BehaviorBlackboardKey[] keys, BehaviorTreeVariant variant, int index, List<BehaviorCompileMessage> messages)
    {
        BehaviorKeyOverride[] values = variant.Overrides;
        if (values == null)
        {
            Error(messages, PathOf(index), BlackboardField, "없음 (null)");
            return;
        }
        string path = PathOf(index) + "." + BlackboardField;
        for (int i = 0; i < values.Length; i++)
        {
            string name = values[i].Key ?? "";
            if (name.Length == 0)
            {
                Error(messages, path, "", "빈 키 이름");
                continue;
            }
            if (Repeats(values, i))
            {
                Error(messages, path, name, "같은 키를 두 번 바꿈");
                continue;
            }
            BehaviorBlackboardKey key = FindKey(keys, name);
            string error = key == null ? "Blackboard에 없는 키" : ValueError(key, values[i]);
            if (error != null)
                Error(messages, path, name, error);
        }
    }

    /// <summary>앞에 같은 키 이름의 값이 있는가</summary>
    private static bool Repeats(BehaviorKeyOverride[] values, int index)
    {
        for (int i = 0; i < index; i++)
        {
            if (string.Equals(values[i].Key, values[index].Key, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>그 이름의 키 정의 — 목록 차례의 첫 키 (컴파일된 트리는 이름이 겹치지 않는다). 없으면 null</summary>
    private static BehaviorBlackboardKey FindKey(BehaviorBlackboardKey[] keys, string name)
    {
        if (keys == null)
            return null;
        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i] != null && string.Equals(keys[i].Name, name, StringComparison.Ordinal))
                return keys[i];
        }
        return null;
    }

    /// <summary>"Wolf, Slime" — 앞의 NamesShown개 (넘으면 "외 n개"), 없으면 "없음"</summary>
    private static string NamesText(BehaviorTreeVariant[] variants)
    {
        var text = new StringBuilder();
        int shown = 0;
        int total = 0;
        for (int i = 0; variants != null && i < variants.Length; i++)
        {
            if (variants[i] == null)
                continue;
            total++;
            if (shown == NamesShown)
                continue;
            if (shown > 0)
                text.Append(", ");
            text.Append(variants[i].Name);
            shown++;
        }
        if (total == 0)
            return "없음";
        if (total > shown)
            text.Append(" 외 ").Append(total - shown).Append("개");
        return text.ToString();
    }

    private static string PathOf(int index)
    {
        return VariantsField + "[" + index + "]";
    }

    private static void Error(List<BehaviorCompileMessage> messages, string path, string field, string text)
    {
        messages.Add(new BehaviorCompileMessage(EBehaviorMessageSeverity.Error, path, null, field, text));
    }

    private static bool IsFinite(float value)
    {
        return float.IsNaN(value) == false && float.IsInfinity(value) == false;
    }
}