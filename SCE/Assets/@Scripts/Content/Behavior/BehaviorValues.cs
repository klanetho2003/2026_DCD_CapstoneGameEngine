using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>
/// 노드가 읽거나 쓸 Blackboard 키 고르기 (Unreal의 FBlackboardKeySelector). JSON은 키 이름 글자 ("Target"), 비우면 null.
/// 컴파일 (BehaviorCompileContext.ResolveKey)이 칸 번호를 정한다 — 평가 중에는 칸 번호만 쓴다.
/// </summary>
[Serializable]
[JsonConverter(typeof(BehaviorValueJsonConverter))]
public struct BehaviorKeySelector
{
    public string Name;

    [NonSerialized] internal int SlotPlusOne; // 칸 번호 + 1 (0 = 해석 전 · 고르지 않음)

    /// <summary>키를 골랐는가 (이름이 있음)</summary>
    public bool IsSelected => string.IsNullOrEmpty(Name) == false;

    /// <summary>해석된 칸 번호 — 고르지 않았거나 해석 전이면 -1</summary>
    public int Slot => SlotPlusOne - 1;

    public BehaviorKeySelector(string name)
    {
        Name = name;
        SlotPlusOne = 0;
    }
}

/// <summary>
/// 상수 또는 Blackboard 키 (Unreal의 FValueOrBBKey) — float. JSON은 숫자 또는 {"key": "이름"}.
/// 한 트리를 여러 몬스터가 쓰며 값만 다르게 할 때 키를 고른다.
/// </summary>
[Serializable]
[JsonConverter(typeof(BehaviorValueJsonConverter))]
public struct BehaviorFloat
{
    public float Value;
    public string Key;

    [NonSerialized] internal int KeySlotPlusOne;

    public bool UsesKey => string.IsNullOrEmpty(Key) == false;

    /// <summary>해석된 키 칸 번호 — 키를 고르지 않았거나 해석 전이면 -1</summary>
    public int KeySlot => KeySlotPlusOne - 1;

    /// <summary>지금 값 — 키를 골랐으면 그 몬스터의 Blackboard 값. O(1)</summary>
    public float Get(BehaviorTreeRunner runner)
    {
        return KeySlotPlusOne == 0 ? Value : runner.Blackboard.GetFloat(KeySlotPlusOne - 1);
    }

    public BehaviorFloat(float value)
    {
        Value = value;
        Key = null;
        KeySlotPlusOne = 0;
    }
}

/// <summary>상수 또는 Blackboard 키 — int. JSON은 정수 또는 {"key": "이름"}</summary>
[Serializable]
[JsonConverter(typeof(BehaviorValueJsonConverter))]
public struct BehaviorInt
{
    public int Value;
    public string Key;

    [NonSerialized] internal int KeySlotPlusOne;

    public bool UsesKey => string.IsNullOrEmpty(Key) == false;

    /// <summary>해석된 키 칸 번호 — 키를 고르지 않았거나 해석 전이면 -1</summary>
    public int KeySlot => KeySlotPlusOne - 1;

    public int Get(BehaviorTreeRunner runner)
    {
        return KeySlotPlusOne == 0 ? Value : runner.Blackboard.GetInt(KeySlotPlusOne - 1);
    }

    public BehaviorInt(int value)
    {
        Value = value;
        Key = null;
        KeySlotPlusOne = 0;
    }
}

/// <summary>상수 또는 Blackboard 키 — bool. JSON은 true · false 또는 {"key": "이름"}</summary>
[Serializable]
[JsonConverter(typeof(BehaviorValueJsonConverter))]
public struct BehaviorBool
{
    public bool Value;
    public string Key;

    [NonSerialized] internal int KeySlotPlusOne;

    public bool UsesKey => string.IsNullOrEmpty(Key) == false;

    /// <summary>해석된 키 칸 번호 — 키를 고르지 않았거나 해석 전이면 -1</summary>
    public int KeySlot => KeySlotPlusOne - 1;

    public bool Get(BehaviorTreeRunner runner)
    {
        return KeySlotPlusOne == 0 ? Value : runner.Blackboard.GetBool(KeySlotPlusOne - 1);
    }

    public BehaviorBool(bool value)
    {
        Value = value;
        Key = null;
        KeySlotPlusOne = 0;
    }
}

/// <summary>상수 또는 Blackboard 키 — Vector2. JSON은 [x, y] 또는 {"key": "이름"}</summary>
[Serializable]
[JsonConverter(typeof(BehaviorValueJsonConverter))]
public struct BehaviorVector2
{
    public Vector2 Value;
    public string Key;

    [NonSerialized] internal int KeySlotPlusOne;

    public bool UsesKey => string.IsNullOrEmpty(Key) == false;

    /// <summary>해석된 키 칸 번호 — 키를 고르지 않았거나 해석 전이면 -1</summary>
    public int KeySlot => KeySlotPlusOne - 1;

    public Vector2 Get(BehaviorTreeRunner runner)
    {
        return KeySlotPlusOne == 0 ? Value : runner.Blackboard.GetVector2(KeySlotPlusOne - 1);
    }

    public BehaviorVector2(Vector2 value)
    {
        Value = value;
        Key = null;
        KeySlotPlusOne = 0;
    }
}

/// <summary>
/// 키 고르기 · 값 또는 키 칸의 JSON — 키 고르기는 글자 (없으면 null), 값 또는 키는 값 그대로 또는 {"key": "이름"}.
/// 모르는 모양 (객체에 key 말고 다른 키 · 형식이 다른 값 · 유한하지 않은 수)은 JsonSerializationException — 트리 로드가 실패로 알린다.
/// 아래 Read · Write 도우미는 Behavior JSON의 다른 변환기도 같이 쓴다.
/// </summary>
[Preserve]
public sealed class BehaviorValueJsonConverter : JsonConverter
{
    private const string KeyProperty = "key";

    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(BehaviorKeySelector) || objectType == typeof(BehaviorFloat) || objectType == typeof(BehaviorInt)
            || objectType == typeof(BehaviorBool) || objectType == typeof(BehaviorVector2);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        JToken token = JToken.Load(reader);

        if (objectType == typeof(BehaviorKeySelector))
        {
            if (token.Type == JTokenType.Null)
                return new BehaviorKeySelector(null);
            if (token.Type != JTokenType.String)
                throw new JsonSerializationException($"키 고르기는 글자 또는 null이어야 한다 ({Where(token)})");
            return new BehaviorKeySelector((string)token);
        }

        string key = ReadKey(token);
        if (objectType == typeof(BehaviorFloat))
            return new BehaviorFloat { Key = key, Value = key != null ? 0f : ReadFloat(token) };
        if (objectType == typeof(BehaviorInt))
            return new BehaviorInt { Key = key, Value = key != null ? 0 : ReadInt(token) };
        if (objectType == typeof(BehaviorBool))
            return new BehaviorBool { Key = key, Value = key != null ? false : ReadBool(token) };
        return new BehaviorVector2 { Key = key, Value = key != null ? Vector2.zero : ReadVector2(token) };
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        switch (value)
        {
            case BehaviorKeySelector selector:
                if (selector.IsSelected)
                    writer.WriteValue(selector.Name);
                else
                    writer.WriteNull();
                return;
            case BehaviorFloat floatValue:
                if (floatValue.UsesKey) WriteKey(writer, floatValue.Key); else WriteFloat(writer, floatValue.Value);
                return;
            case BehaviorInt intValue:
                if (intValue.UsesKey) WriteKey(writer, intValue.Key); else writer.WriteValue(intValue.Value);
                return;
            case BehaviorBool boolValue:
                if (boolValue.UsesKey) WriteKey(writer, boolValue.Key); else writer.WriteValue(boolValue.Value);
                return;
            case BehaviorVector2 vectorValue:
                if (vectorValue.UsesKey) WriteKey(writer, vectorValue.Key); else WriteVector2(writer, vectorValue.Value);
                return;
        }
        throw new JsonSerializationException($"모르는 형식 {value?.GetType().Name}");
    }

    /// <summary>{"key": "이름"}이면 이름, 값이면 null. 객체에 다른 키가 있거나 이름이 글자가 아니거나 비면 예외</summary>
    private static string ReadKey(JToken token)
    {
        if (token.Type != JTokenType.Object)
            return null;
        var keyObject = (JObject)token;
        JToken name = keyObject[KeyProperty];
        if (keyObject.Count != 1 || name == null || name.Type != JTokenType.String || string.IsNullOrEmpty((string)name))
            throw new JsonSerializationException($"값 또는 키 칸의 객체는 {{\"{KeyProperty}\": \"이름\"}} 하나여야 한다 ({Where(token)})");
        return (string)name;
    }

    private static void WriteKey(JsonWriter writer, string key)
    {
        writer.WriteStartObject();
        writer.WritePropertyName(KeyProperty);
        writer.WriteValue(key);
        writer.WriteEndObject();
    }

    /// <summary>숫자 — float로 나타낼 수 있는 유한한 수만</summary>
    internal static float ReadFloat(JToken token)
    {
        if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
        {
            double number = (double)token;
            if (double.IsNaN(number) == false && double.IsInfinity(number) == false && Math.Abs(number) <= float.MaxValue)
                return (float)number;
        }
        throw new JsonSerializationException($"유한한 숫자여야 한다 ({Where(token)})");
    }

    /// <summary>정수 — int 범위만 (소수 · 글자는 받지 않는다)</summary>
    internal static int ReadInt(JToken token)
    {
        if (token.Type == JTokenType.Integer && ((JValue)token).Value is long number && number >= int.MinValue && number <= int.MaxValue)
            return (int)number;
        throw new JsonSerializationException($"int 범위의 정수여야 한다 ({Where(token)})");
    }

    internal static bool ReadBool(JToken token)
    {
        if (token.Type != JTokenType.Boolean)
            throw new JsonSerializationException($"true · false여야 한다 ({Where(token)})");
        return (bool)token;
    }

    /// <summary>글자 또는 null</summary>
    internal static string ReadString(JToken token)
    {
        if (token.Type == JTokenType.Null)
            return null;
        if (token.Type != JTokenType.String)
            throw new JsonSerializationException($"글자여야 한다 ({Where(token)})");
        return (string)token;
    }

    /// <summary>[x, y] — 유한한 숫자 둘</summary>
    internal static Vector2 ReadVector2(JToken token)
    {
        if (token.Type != JTokenType.Array || ((JArray)token).Count != 2)
            throw new JsonSerializationException($"[x, y] 모양이어야 한다 ({Where(token)})");
        var array = (JArray)token;
        return new Vector2(ReadFloat(array[0]), ReadFloat(array[1]));
    }

    /// <summary>유한한 수만 쓴다 — NaN · 무한은 다시 읽을 수 없으므로 저장을 막는다</summary>
    internal static void WriteFloat(JsonWriter writer, float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new JsonSerializationException($"유한하지 않은 수 ({value})는 저장할 수 없다 (path '{writer.Path}')");
        writer.WriteValue(value);
    }

    internal static void WriteVector2(JsonWriter writer, Vector2 value)
    {
        writer.WriteStartArray();
        WriteFloat(writer, value.x);
        WriteFloat(writer, value.y);
        writer.WriteEndArray();
    }

    /// <summary>문장에 쓰는 자리 — 원문의 줄 · 칸 (읽은 글에 줄 정보가 없으면 경로)</summary>
    internal static string Where(JToken token)
    {
        var line = (IJsonLineInfo)token;
        return line.HasLineInfo() ? $"line {line.LineNumber}, position {line.LinePosition}" : $"path '{token.Path}'";
    }
}

/// <summary>
/// Behavior JSON의 enum — 정의된 이름 그대로만 (숫자 · 대소문자 다름 · 앞뒤 공백은 받지 않는다). [Flags] enum은 이름을 ", "로 묶을 수 있다 ("A, B").
/// </summary>
[Preserve]
public sealed class BehaviorEnumJsonConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType.IsEnum;
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        JToken token = JToken.Load(reader);
        if (token.Type == JTokenType.String && TryParseExact(objectType, (string)token, out object value))
            return value;
        throw new JsonSerializationException($"{objectType.Name}의 이름이어야 한다: {token.ToString(Formatting.None)} ({BehaviorValueJsonConverter.Where(token)})");
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        writer.WriteValue(value.ToString());
    }

    private static bool TryParseExact(Type type, string text, out object value)
    {
        if (Enum.IsDefined(type, text))
        {
            value = Enum.Parse(type, text);
            return true;
        }
        value = null;
        if (type.IsDefined(typeof(FlagsAttribute), false) == false)
            return false;
        ulong bits = 0;
        string[] names = text.Split(new[] { ", " }, StringSplitOptions.None);
        for (int i = 0; i < names.Length; i++)
        {
            if (Enum.IsDefined(type, names[i]) == false)
                return false;
            bits |= Convert.ToUInt64(Enum.Parse(type, names[i]));
        }
        value = Enum.ToObject(type, bits);
        return true;
    }
}

/// <summary>
/// Behavior JSON의 int · float · bool · string 칸 — 토큰 형식이 맞아야 한다 (소수를 int로 반올림 · "4"를 수로 · 1을 bool로 바꾸지 않는다).
/// float는 유한한 수만 읽고 쓴다.
/// </summary>
[Preserve]
public sealed class BehaviorPrimitiveJsonConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(int) || objectType == typeof(float) || objectType == typeof(bool) || objectType == typeof(string);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        JToken token = JToken.Load(reader);
        if (objectType == typeof(int))
            return BehaviorValueJsonConverter.ReadInt(token);
        if (objectType == typeof(float))
            return BehaviorValueJsonConverter.ReadFloat(token);
        if (objectType == typeof(bool))
            return BehaviorValueJsonConverter.ReadBool(token);
        return BehaviorValueJsonConverter.ReadString(token);
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value is float number)
            BehaviorValueJsonConverter.WriteFloat(writer, number);
        else
            writer.WriteValue(value);
    }
}