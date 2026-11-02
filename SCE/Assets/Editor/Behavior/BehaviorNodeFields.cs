using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;

/// <summary>
/// 노드 칸 (설정 필드)의 반사 정보 — [JsonProperty]가 붙은 public 필드가 저장하는 칸이다 (JSON 키 ↔ 필드). 형식마다 처음 쓸 때 한 번 모아 둔다 (편집기만).
/// 키를 고르는 칸 = 키 고르기 (BehaviorKeySelector.Name) · 값 또는 키 (BehaviorFloat · BehaviorInt · BehaviorBool · BehaviorVector2의 Key) — 키 이름 바꾸기 · 키를 쓰는 노드 찾기가 쓴다.
/// </summary>
public static class BehaviorNodeFields
{
    /// <summary>저장하는 칸 하나 — 필드와 JSON 키</summary>
    public readonly struct Field
    {
        public readonly FieldInfo Info;
        public readonly string JsonKey;

        public Field(FieldInfo info, string jsonKey)
        {
            Info = info;
            JsonKey = jsonKey;
        }
    }

    private static readonly Dictionary<Type, Field[]> s_fields = new Dictionary<Type, Field[]>();

    /// <summary>형식의 저장하는 칸 — 바탕 형식의 칸 먼저, 형식마다 선언 차례 (Unity 직렬화 차례와 같다). 처음 한 번 O(필드 수), 그 뒤 O(1)</summary>
    public static IReadOnlyList<Field> Of(Type type)
    {
        if (s_fields.TryGetValue(type, out Field[] cached))
            return cached;

        var chain = new List<Type>();
        for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
            chain.Add(t);
        var fields = new List<Field>();
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            FieldInfo[] declared = chain[i].GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            for (int f = 0; f < declared.Length; f++)
            {
                var property = declared[f].GetCustomAttribute<JsonPropertyAttribute>();
                if (property != null)
                    fields.Add(new Field(declared[f], property.PropertyName ?? declared[f].Name));
            }
        }
        Field[] result = fields.ToArray();
        s_fields.Add(type, result);
        return result;
    }

    /// <summary>JSON 키의 칸. 없으면 false. O(칸 수)</summary>
    public static bool TryGetField(Type type, string jsonKey, out Field field)
    {
        IReadOnlyList<Field> fields = Of(type);
        for (int i = 0; i < fields.Count; i++)
        {
            if (string.Equals(fields[i].JsonKey, jsonKey, StringComparison.Ordinal))
            {
                field = fields[i];
                return true;
            }
        }
        field = default;
        return false;
    }

    /// <summary>필드 이름 (Unity 직렬화 이름)의 칸. 없으면 false — 저장하지 않는 필드. O(칸 수)</summary>
    public static bool TryGetFieldByName(Type type, string fieldName, out Field field)
    {
        IReadOnlyList<Field> fields = Of(type);
        for (int i = 0; i < fields.Count; i++)
        {
            if (string.Equals(fields[i].Info.Name, fieldName, StringComparison.Ordinal))
            {
                field = fields[i];
                return true;
            }
        }
        field = default;
        return false;
    }

    /// <summary>키를 고르는 칸의 형식인가 — 키 고르기 · 값 또는 키</summary>
    public static bool IsKeyField(Type fieldType)
    {
        return fieldType == typeof(BehaviorKeySelector) || ValueKeyType(fieldType) != 0;
    }

    /// <summary>
    /// 값 또는 키 칸이 받는 키 형식 (BehaviorFloat → Float · BehaviorInt → Int · BehaviorBool → Bool · BehaviorVector2 → Vector2 — 컴파일의 Resolve와 같다).
    /// 다른 형식이면 0 — 키 고르기가 받는 형식은 노드 Compile이 밝힌다 (BehaviorTreeCompiler.CollectKeyFields)
    /// </summary>
    public static EBehaviorKeyTypeMask ValueKeyType(Type fieldType)
    {
        if (fieldType == typeof(BehaviorFloat))
            return EBehaviorKeyTypeMask.Float;
        if (fieldType == typeof(BehaviorInt))
            return EBehaviorKeyTypeMask.Int;
        if (fieldType == typeof(BehaviorBool))
            return EBehaviorKeyTypeMask.Bool;
        if (fieldType == typeof(BehaviorVector2))
            return EBehaviorKeyTypeMask.Vector2;
        return 0;
    }

    /// <summary>키를 고르는 칸이 고른 키 이름 — 고르지 않았으면 "" (값을 쓰는 값 또는 키 칸 포함). 키를 고르는 칸이 아니면 ""</summary>
    public static string GetKeyName(object node, FieldInfo field)
    {
        switch (field.GetValue(node))
        {
            case BehaviorKeySelector selector: return selector.Name ?? "";
            case BehaviorFloat value: return value.Key ?? "";
            case BehaviorInt value: return value.Key ?? "";
            case BehaviorBool value: return value.Key ?? "";
            case BehaviorVector2 value: return value.Key ?? "";
            default: return "";
        }
    }

    /// <summary>키를 고르는 칸의 키 이름을 바꾼다 (구조체 값을 꺼내 고쳐 다시 넣는다). 키를 고르는 칸이 아니면 false</summary>
    public static bool SetKeyName(object node, FieldInfo field, string name)
    {
        object value = field.GetValue(node);
        switch (value)
        {
            case BehaviorKeySelector selector:
                selector.Name = name;
                value = selector;
                break;
            case BehaviorFloat floatValue:
                floatValue.Key = name;
                value = floatValue;
                break;
            case BehaviorInt intValue:
                intValue.Key = name;
                value = intValue;
                break;
            case BehaviorBool boolValue:
                boolValue.Key = name;
                value = boolValue;
                break;
            case BehaviorVector2 vectorValue:
                vectorValue.Key = name;
                value = vectorValue;
                break;
            default:
                return false;
        }
        field.SetValue(node, value);
        return true;
    }
}