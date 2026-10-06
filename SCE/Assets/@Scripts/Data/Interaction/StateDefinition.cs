using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using UnityEngine.Scripting;
using static Define;

// 저장 형식 규칙 — 툴로 다시 저장해도 손으로 쓴 파일과 같은 모양이 되도록
// - JsonProperty의 Order: 필드 순서 고정. Newtonsoft는 기본적으로 필드를 먼저, 속성을 나중에 쓰므로
//   지정하지 않으면 속성인 ruleKey가 규칙의 맨 뒤로 간다.
// - ShouldSerialize{멤버 이름}(): Newtonsoft가 저장할 때만 부르는 규칙. false면 그 항목을 파일에 쓰지 않는다.
//   읽기에는 영향 없음.

/// <summary>상태 키 1개의 선언. 기본값과 초기화 정책을 함께 가진다.</summary>
[Serializable]
public sealed class StateKeyDefinition
{
    [JsonProperty("key", Order = 0)]
    public string Key;

    [JsonProperty("default", Order = 1)]
    public int Default;

    /// <summary>항상 저장한다 — reset을 생략한 파일도 툴로 저장하면 None이 명시된다.</summary>
    [JsonProperty("reset", Order = 2), JsonConverter(typeof(StringEnumConverter))]
    public EStateResetPolicy Reset = EStateResetPolicy.None;

    /// <summary>EveryNDays 전용. 1 이상.</summary>
    [JsonProperty("interval", Order = 3)]
    public int Interval;

    [JsonProperty("desc", Order = 4)]
    public string Description = "";

    public bool ShouldSerializeInterval() { return Interval != 0; }
    public bool ShouldSerializeDescription() { return string.IsNullOrEmpty(Description) == false; }
}

/// <summary>규칙의 선행 조건 1개 — 모두 참이어야 ops가 실행된다.</summary>
[Serializable]
public sealed class StateRequireDefinition
{
    [JsonProperty("key", Order = 0)]
    public string Key;

    [JsonProperty("cmp", Order = 1), JsonConverter(typeof(StringEnumConverter))]
    public EComparison Comparison = EComparison.GreaterOrEqual;

    [JsonProperty("value", Order = 2)]
    public int Value;
}

/// <summary>규칙의 값 변경 1개.</summary>
[Serializable]
public sealed class StateOpDefinition
{
    [JsonProperty("key", Order = 0)]
    public string Key;

    [JsonProperty("op", Order = 1), JsonConverter(typeof(StringEnumConverter))]
    public EStateOp Op = EStateOp.Set;

    [JsonProperty("value", Order = 2)]
    public int Value;
}



/// <summary>
/// 사건 → 값 변경 규칙 1개.
/// 같은 이벤트의 규칙은 선언 순서대로 실행되며, 앞 규칙이 바꾼 값을 뒤 규칙의 require가 본다.
/// </summary>
[Serializable]
public sealed class StateRuleDefinition
{
    [JsonProperty("event", Order = 0), JsonConverter(typeof(StringEnumConverter))]
    public EGameEventType Event;

    /// <summary>
    /// ruleKey 지정 여부. false = 생략 = 해당 종류의 모든 이벤트에 반응.
    /// Unity 직렬화는 int?를 저장하지 못해 bool + int 두 필드로 나눴다 (에디터 툴 바인딩용).
    /// JSON에는 직접 쓰지 않고 아래 RuleKey 속성을 거친다.
    /// </summary>
    [JsonIgnore]
    public bool HasRuleKey;

    /// <summary>HasRuleKey가 true일 때만 의미가 있다. 0도 유효한 값이다 (생략과 다름).</summary>
    [JsonIgnore]
    public int RuleKeyValue;

    /// <summary>
    /// JSON의 ruleKey 창구. 생략하면 null이며, 값은 위 두 필드에만 저장된다.
    /// 런타임은 로드 시 AnyKey 플래그로 변환하므로 nullable을 쓰지 않는다.
    /// [Preserve]: Newtonsoft만 리플렉션으로 호출하는 속성이라 IL2CPP 코드 제거 단계에서 지워지지 않게 한다.
    /// </summary>
    [Preserve] // 빌드 시 지워지지 않도록 강제한다.
    [JsonProperty("ruleKey", Order = 1, NullValueHandling = NullValueHandling.Ignore)]
    public int? RuleKey
    {
        get { return HasRuleKey ? RuleKeyValue : (int?)null; }
        set
        {
            HasRuleKey = value.HasValue;
            RuleKeyValue = value ?? 0;
        }
    }

    [JsonProperty("desc", Order = 2)]
    public string Description = "";

    [JsonProperty("require", Order = 3)]
    public StateRequireDefinition[] Require;

    [JsonProperty("ops", Order = 4)]
    public StateOpDefinition[] Ops;

    public bool ShouldSerializeDescription() { return string.IsNullOrEmpty(Description) == false; }
    public bool ShouldSerializeRequire() { return Require != null && Require.Length > 0; }
}

/// <summary>상태 정의 파일 1개 — 키 선언 + 그 키를 바꾸는 규칙.</summary>
[Serializable]
public sealed class StateDefinitionSet
{
    [JsonProperty("id", Order = 0)]
    public string Id;

    [JsonProperty("keys", Order = 1)]
    public StateKeyDefinition[] Keys;

    [JsonProperty("rules", Order = 2)]
    public StateRuleDefinition[] Rules;
}