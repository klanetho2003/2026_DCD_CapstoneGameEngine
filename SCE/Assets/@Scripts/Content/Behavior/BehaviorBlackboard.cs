using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>
/// Blackboard 키 하나의 정의 — 트리 JSON "blackboard" 목록의 한 줄: {"name", "type", "default"?, "seed"?}.
/// default는 형식에 맞는 값 (Bool: true · false, Int: 정수, Float: 숫자, Vector2: [x, y], Creature: 없음).
/// seed는 처음 값의 출처 이름 — 에이전트가 해석한다 (IBehaviorAgent.TryGetIntSeed, Int 키만). 해석하지 못하면 default.
/// 처음 상태: Bool · Int · Float는 값 있음, Vector2는 default가 (0, 0)이면 없음 (순찰 점처럼 '아직 없음'으로 시작), Creature는 없음.
/// </summary>
[Serializable]
[JsonConverter(typeof(BehaviorBlackboardKeyJsonConverter))]
public sealed class BehaviorBlackboardKey
{
    public string Name = "";
    public EBehaviorKeyType Type;
    public bool DefaultBool;
    public int DefaultInt;
    public float DefaultFloat;
    public Vector2 DefaultVector2;
    public string Seed = "";

    public bool HasSeed => string.IsNullOrEmpty(Seed) == false;
}

/// <summary>Blackboard 키 정의의 JSON — 형식에 맞는 default만 읽고 쓴다. 모르는 칸 · 형식이 다른 default · 유한하지 않은 수는 예외</summary>
[Preserve]
public sealed class BehaviorBlackboardKeyJsonConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(BehaviorBlackboardKey);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;
        JToken token = JToken.Load(reader);
        if (token.Type != JTokenType.Object)
            throw new JsonSerializationException($"Blackboard 키는 객체여야 한다 ({BehaviorValueJsonConverter.Where(token)})");
        var keyObject = (JObject)token;
        var key = new BehaviorBlackboardKey();
        bool hasType = false;
        foreach (JProperty property in keyObject.Properties())
        {
            switch (property.Name)
            {
                case "name":
                    key.Name = BehaviorValueJsonConverter.ReadString(property.Value) ?? "";
                    break;
                case "type":
                    string typeName = BehaviorValueJsonConverter.ReadString(property.Value);
                    if (typeName == null || Enum.IsDefined(typeof(EBehaviorKeyType), typeName) == false) // 정의된 이름 그대로만 (숫자 · 대소문자 다름 · 쉼표 묶음은 받지 않는다)
                        throw new JsonSerializationException($"모르는 키 형식 '{typeName}' ({BehaviorValueJsonConverter.Where(property.Value)})");
                    key.Type = (EBehaviorKeyType)Enum.Parse(typeof(EBehaviorKeyType), typeName);
                    hasType = true;
                    break;
                case "default":
                case "seed":
                    break; // 형식을 안 뒤에 읽는다
                default:
                    throw new JsonSerializationException($"Blackboard 키에 모르는 칸 '{property.Name}' ({BehaviorValueJsonConverter.Where(property)})");
            }
        }
        if (hasType == false)
            throw new JsonSerializationException($"Blackboard 키 '{key.Name}'에 type이 없음 ({BehaviorValueJsonConverter.Where(keyObject)})");
        if (keyObject.TryGetValue("seed", out JToken seed))
            key.Seed = BehaviorValueJsonConverter.ReadString(seed) ?? "";
        if (keyObject.TryGetValue("default", out JToken value) && value.Type != JTokenType.Null)
        {
            switch (key.Type)
            {
                case EBehaviorKeyType.Bool: key.DefaultBool = BehaviorValueJsonConverter.ReadBool(value); break;
                case EBehaviorKeyType.Int: key.DefaultInt = BehaviorValueJsonConverter.ReadInt(value); break;
                case EBehaviorKeyType.Float: key.DefaultFloat = BehaviorValueJsonConverter.ReadFloat(value); break;
                case EBehaviorKeyType.Vector2: key.DefaultVector2 = BehaviorValueJsonConverter.ReadVector2(value); break;
                default: throw new JsonSerializationException($"Creature 키에는 default를 쓸 수 없다 ({BehaviorValueJsonConverter.Where(value)})");
            }
        }
        return key;
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        var key = (BehaviorBlackboardKey)value;
        writer.WriteStartObject();
        writer.WritePropertyName("name");
        writer.WriteValue(key.Name);
        writer.WritePropertyName("type");
        writer.WriteValue(key.Type.ToString());
        switch (key.Type) // 형식의 처음 값과 다를 때만 쓴다
        {
            case EBehaviorKeyType.Bool when key.DefaultBool:
                writer.WritePropertyName("default");
                writer.WriteValue(true);
                break;
            case EBehaviorKeyType.Int when key.DefaultInt != 0:
                writer.WritePropertyName("default");
                writer.WriteValue(key.DefaultInt);
                break;
            case EBehaviorKeyType.Float when key.DefaultFloat != 0f:
                writer.WritePropertyName("default");
                BehaviorValueJsonConverter.WriteFloat(writer, key.DefaultFloat);
                break;
            case EBehaviorKeyType.Vector2 when key.DefaultVector2 != Vector2.zero:
                writer.WritePropertyName("default");
                BehaviorValueJsonConverter.WriteVector2(writer, key.DefaultVector2);
                break;
        }
        if (key.HasSeed)
        {
            writer.WritePropertyName("seed");
            writer.WriteValue(key.Seed);
        }
        writer.WriteEndObject();
    }
}

/// <summary>
/// 트리 하나의 Blackboard 배치 — 키 이름 → 칸 번호 (목록 차례). 컴파일이 키 정의를 복사해 만들고 그 트리를 쓰는 모든 몬스터가 공유한다.
/// 이름으로 찾기는 컴파일 때만 — 평가 중에는 칸 번호만 쓴다.
/// </summary>
public sealed class BehaviorBlackboardLayout
{
    private readonly BehaviorBlackboardKey[] _keys;
    private readonly EBehaviorKeyType[] _types;
    private readonly Dictionary<string, int> _slotsByName;
    private readonly bool[] _seedWarned; // 출처를 해석하지 못한 경고를 키마다 한 번만 (편집기)

    public int KeyCount => _keys.Length;

    /// <summary>크리처 키가 있는가 — 없으면 몬스터마다 참조 배열을 만들지 않는다</summary>
    public bool HasCreatureKeys { get; }

    internal BehaviorBlackboardLayout(BehaviorBlackboardKey[] keys)
    {
        _keys = new BehaviorBlackboardKey[keys.Length];
        _types = new EBehaviorKeyType[keys.Length];
        _slotsByName = new Dictionary<string, int>(keys.Length, StringComparer.Ordinal);
        _seedWarned = new bool[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            BehaviorBlackboardKey key = keys[i];
            _keys[i] = new BehaviorBlackboardKey
            {
                Name = key.Name,
                Type = key.Type,
                DefaultBool = key.DefaultBool,
                DefaultInt = key.DefaultInt,
                DefaultFloat = key.DefaultFloat,
                DefaultVector2 = key.DefaultVector2,
                Seed = key.Seed,
            };
            _types[i] = key.Type;
            _slotsByName[key.Name] = i;
            if (key.Type == EBehaviorKeyType.Creature)
                HasCreatureKeys = true;
        }
    }

    /// <summary>
    /// 변형의 배치 — 이 배치와 같은 키 (이름 · 형식 · 차례 · 출처)에 values의 기본값을 입힌 새 배치 (BehaviorTreeVariants.Build — 검사를 지난 값만 넘긴다).
    /// 없는 키의 값은 건너뛴다. O(키 수 + 값 수)
    /// </summary>
    internal BehaviorBlackboardLayout WithDefaults(BehaviorKeyOverride[] values)
    {
        var layout = new BehaviorBlackboardLayout(_keys);
        for (int i = 0; i < values.Length; i++)
        {
            if (layout.TryGetSlot(values[i].Key, out int slot) == false)
                continue;
            BehaviorBlackboardKey key = layout._keys[slot];
            switch (key.Type)
            {
                case EBehaviorKeyType.Bool:
                    key.DefaultBool = values[i].BoolValue;
                    break;
                case EBehaviorKeyType.Int:
                    key.DefaultInt = values[i].IntValue;
                    break;
                case EBehaviorKeyType.Float:
                    key.DefaultFloat = BehaviorTreeVariants.FloatOf(values[i]);
                    break;
                case EBehaviorKeyType.Vector2:
                    key.DefaultVector2 = values[i].VectorValue;
                    break;
            }
        }
        return layout;
    }

    /// <summary>칸의 키 정의 (복사본 — 바꾸지 않는다)</summary>
    public BehaviorBlackboardKey GetKey(int slot)
    {
        return _keys[slot];
    }

    public EBehaviorKeyType TypeOf(int slot)
    {
        return _types[slot];
    }

    public bool TryGetSlot(string name, out int slot)
    {
        if (name == null)
        {
            slot = -1;
            return false;
        }
        return _slotsByName.TryGetValue(name, out slot);
    }

    /// <summary>이 키의 출처 경고를 처음 내는가 — 처음이면 true (다음부터 false)</summary>
    internal bool MarkSeedWarned(int slot)
    {
        if (_seedWarned[slot])
            return false;
        _seedWarned[slot] = true;
        return true;
    }
}

/// <summary>
/// Blackboard 칸 하나 (16바이트) — 값은 형식마다 같은 자리를 겹쳐 쓴다 (Bool · Int · 크리처의 스폰 번호 = Int, Float · Vector2.x = Float, Vector2.y = Y).
/// Version은 값이 실제로 바뀐 횟수 (같은 값을 쓰면 그대로), HasValue는 값이 있는가 (지우면 0).
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public struct BehaviorBlackboardValue
{
    [FieldOffset(0)] public int Int;
    [FieldOffset(0)] public float Float;
    [FieldOffset(4)] public float Y;
    [FieldOffset(8)] public int Version;
    [FieldOffset(12)] public int HasValue;
}

/// <summary>
/// 몬스터 하나의 Blackboard 값 — 배치 (공유)의 칸 번호로 읽고 쓴다. 같은 배치로 다시 묶으면 배열을 그대로 쓴다.
/// 크리처 키는 참조와 스폰 번호를 함께 두고, 읽을 때마다 쓸 수 있는가를 한 곳에서 본다 (IsUsable) — 쓸 수 없으면 지우고 null.
/// 형식이 맞는 칸만 부른다 (컴파일이 맞춰 둔다). O(1) · 할당 없음.
/// </summary>
public sealed class BehaviorBlackboard
{
    private static readonly CreatureBase[] s_noCreatures = Array.Empty<CreatureBase>();

    private BehaviorBlackboardLayout _layout;
    private BehaviorBlackboardValue[] _values = Array.Empty<BehaviorBlackboardValue>();
    private CreatureBase[] _creatures = s_noCreatures;

    public BehaviorBlackboardLayout Layout => _layout;

    /// <summary>배치를 묶는다 — 같은 배치면 배열을 그대로 쓴다. 값은 ResetToDefaults가 채운다</summary>
    public void Bind(BehaviorBlackboardLayout layout)
    {
        if (ReferenceEquals(layout, _layout))
            return;
        _layout = layout;
        int count = layout != null ? layout.KeyCount : 0;
        if (_values.Length != count)
            _values = count > 0 ? new BehaviorBlackboardValue[count] : Array.Empty<BehaviorBlackboardValue>();
        bool creatures = layout != null && layout.HasCreatureKeys;
        if (creatures && _creatures.Length != count)
            _creatures = new CreatureBase[count];
        else if (creatures == false)
            _creatures = s_noCreatures;
    }

    /// <summary>모든 키를 처음 값으로 — 기본값, 출처가 있으면 에이전트에게 묻는다. 판 번호는 0. O(키 수)</summary>
    public void ResetToDefaults(IBehaviorAgent agent)
    {
        Array.Clear(_creatures, 0, _creatures.Length);
        for (int slot = 0; slot < _values.Length; slot++)
        {
            BehaviorBlackboardKey key = _layout.GetKey(slot);
            ref BehaviorBlackboardValue value = ref _values[slot];
            value = default;
            switch (key.Type)
            {
                case EBehaviorKeyType.Bool:
                    value.Int = key.DefaultBool ? 1 : 0;
                    value.HasValue = 1;
                    break;
                case EBehaviorKeyType.Int:
                    value.Int = key.DefaultInt;
                    if (key.HasSeed)
                    {
                        if (agent != null && agent.TryGetIntSeed(key.Seed, out int seeded))
                            value.Int = seeded;
                        else
                            WarnSeed(slot, key);
                    }
                    value.HasValue = 1;
                    break;
                case EBehaviorKeyType.Float:
                    value.Float = key.DefaultFloat;
                    value.HasValue = 1;
                    break;
                case EBehaviorKeyType.Vector2:
                    value.Float = key.DefaultVector2.x;
                    value.Y = key.DefaultVector2.y;
                    value.HasValue = key.DefaultVector2 != Vector2.zero ? 1 : 0;
                    break;
                case EBehaviorKeyType.Creature:
                    break;
            }
        }
    }

    #region 읽기
    public bool GetBool(int slot)
    {
        return _values[slot].Int != 0;
    }

    public int GetInt(int slot)
    {
        return _values[slot].Int;
    }

    public float GetFloat(int slot)
    {
        return _values[slot].Float;
    }

    public Vector2 GetVector2(int slot)
    {
        ref BehaviorBlackboardValue value = ref _values[slot];
        return new Vector2(value.Float, value.Y);
    }

    /// <summary>크리처 — 쓸 수 없게 됐으면 (없어짐 · 꺼짐 · 다른 생애 · 죽음) 지우고 null</summary>
    public CreatureBase GetCreature(int slot)
    {
        CreatureBase creature = _creatures[slot];
        if (ReferenceEquals(creature, null)) // 없어진 개체 (Unity의 == null)는 아래 확인이 지운다
            return null;
        if (IsUsable(creature, _values[slot].Int))
            return creature;
        Clear(slot);
        return null;
    }

    /// <summary>값이 있는가 — 크리처는 쓸 수 있는 크리처가 있는가, Vector2는 지우지 않았는가, 그 밖은 늘 참</summary>
    public bool HasValue(int slot)
    {
        if (_layout.TypeOf(slot) == EBehaviorKeyType.Creature)
            return ReferenceEquals(GetCreature(slot), null) == false;
        return _values[slot].HasValue != 0;
    }

    /// <summary>값이 실제로 바뀐 횟수 — 지켜보는 조건이 'On Value Change'에 쓴다</summary>
    public int GetVersion(int slot)
    {
        if (_layout.TypeOf(slot) == EBehaviorKeyType.Creature)
            GetCreature(slot); // 쓸 수 없게 된 크리처는 지우기가 판 번호를 올린다
        return _values[slot].Version;
    }

#if UNITY_EDITOR
    /// <summary>
    /// 디버거 (편집기만) — 크리처 키를 지우지 않고 본다 (GetCreature · HasValue · GetVersion은 쓸 수 없게 된 대상을 지운다). 쓸 수 있으면 그 크리처, 아니면 null.
    /// stored = 칸에 크리처가 적혀 있다 (쓸 수 없게 되었어도 — 다음 읽기가 지운다). O(1)
    /// </summary>
    public CreatureBase PeekCreature(int slot, out bool stored)
    {
        CreatureBase creature = (uint)slot < (uint)_creatures.Length ? _creatures[slot] : null;
        stored = ReferenceEquals(creature, null) == false;
        return stored && IsUsable(creature, _values[slot].Int) ? creature : null;
    }

    /// <summary>디버거 (편집기만) — 값이 실제로 바뀐 횟수 (크리처 키도 지우지 않고). O(1)</summary>
    public int PeekVersion(int slot)
    {
        return _values[slot].Version;
    }

    /// <summary>
    /// 디버거 (편집기만) — 값 글 (지우지 않고): Bool "true" · "false", Int "3", Float "1.5", Vector2 "(1, 2)" · "없음", 크리처 이름 · "없음" (쓸 수 없게 되었으면 "없음 — 쓸 수 없게 됨").
    /// 형식이 맞는 칸만 (배치의 형식으로 고른다). O(1)
    /// </summary>
    public string DebugValueText(int slot)
    {
        switch (_layout.TypeOf(slot))
        {
            case EBehaviorKeyType.Bool:
                return GetBool(slot) ? "true" : "false";
            case EBehaviorKeyType.Int:
                return GetInt(slot).ToString(System.Globalization.CultureInfo.InvariantCulture);
            case EBehaviorKeyType.Float:
                return BehaviorDebugText.Number(GetFloat(slot));
            case EBehaviorKeyType.Vector2:
                return _values[slot].HasValue != 0 ? BehaviorDebugText.Point(GetVector2(slot)) : "없음";
            default:
                CreatureBase creature = PeekCreature(slot, out bool stored);
                if (creature != null)
                    return creature.name;
                return stored ? "없음 — 쓸 수 없게 됨" : "없음";
        }
    }
#endif
    #endregion

    #region 쓰기 — 같은 값이면 판 번호 그대로
    public void SetBool(int slot, bool value)
    {
        SetInt(slot, value ? 1 : 0);
    }

    public void SetInt(int slot, int value)
    {
        ref BehaviorBlackboardValue current = ref _values[slot];
        if (current.HasValue != 0 && current.Int == value)
            return;
        current.Int = value;
        current.HasValue = 1;
        current.Version++;
    }

    public void SetFloat(int slot, float value)
    {
        ref BehaviorBlackboardValue current = ref _values[slot];
        if (current.HasValue != 0 && current.Float.Equals(value))
            return;
        current.Float = value;
        current.HasValue = 1;
        current.Version++;
    }

    public void SetVector2(int slot, Vector2 value)
    {
        ref BehaviorBlackboardValue current = ref _values[slot];
        if (current.HasValue != 0 && current.Float.Equals(value.x) && current.Y.Equals(value.y))
            return;
        current.Float = value.x;
        current.Y = value.y;
        current.HasValue = 1;
        current.Version++;
    }

    /// <summary>크리처를 둔다 — null이면 지운다. 스폰 번호를 함께 두어 다른 생애를 같은 대상으로 보지 않는다</summary>
    public void SetCreature(int slot, CreatureBase creature)
    {
        if (creature == null)
        {
            Clear(slot);
            return;
        }
        ref BehaviorBlackboardValue current = ref _values[slot];
        if (current.HasValue != 0 && ReferenceEquals(_creatures[slot], creature) && current.Int == creature.SpawnSerial)
            return;
        _creatures[slot] = creature;
        current.Int = creature.SpawnSerial;
        current.HasValue = 1;
        current.Version++;
    }

    /// <summary>
    /// 지운다 — Vector2 · Creature는 '없음', Bool · Int · Float는 키의 기본값 (늘 값이 있는 형식). 바뀌지 않으면 판 번호 그대로
    /// </summary>
    public void Clear(int slot)
    {
        BehaviorBlackboardKey key = _layout.GetKey(slot);
        switch (key.Type)
        {
            case EBehaviorKeyType.Bool:
                SetBool(slot, key.DefaultBool);
                return;
            case EBehaviorKeyType.Int:
                SetInt(slot, key.DefaultInt);
                return;
            case EBehaviorKeyType.Float:
                SetFloat(slot, key.DefaultFloat);
                return;
        }
        ref BehaviorBlackboardValue current = ref _values[slot];
        if (_creatures.Length > slot)
            _creatures[slot] = null;
        if (current.HasValue == 0)
            return;
        current.HasValue = 0;
        current.Version++;
    }
    #endregion

    /// <summary>크리처를 쓸 수 있는가 — 살아 있는 개체 (IsValid) · 같은 생애 (스폰 번호) · 죽지 않음</summary>
    public static bool IsUsable(CreatureBase creature, int spawnSerial)
    {
        if (creature.IsValid() == false || creature.SpawnSerial != spawnSerial)
            return false;
        if (creature is CombatCreature combat && combat.IsDead)
            return false;
        return true;
    }

    /// <summary>출처를 해석하지 못함 — 키마다 한 번 편집기 경고</summary>
    private void WarnSeed(int slot, BehaviorBlackboardKey key)
    {
#if UNITY_EDITOR
        if (_layout.MarkSeedWarned(slot))
            LogPrinter.LogWarning($"[BehaviorBlackboard] 키 '{key.Name}'의 처음 값 출처 '{key.Seed}'를 몸이 모름 — 기본값 {key.DefaultInt}");
#endif
    }
}