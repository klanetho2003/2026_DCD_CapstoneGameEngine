using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;
using static Define;

/// <summary>파싱 최종 결과물. NPC 1종이 참조하는 상호작용 묶음 = Lyra의 AbilitySet. NPCData.InteractionSetId로 조회.</summary>
[Serializable]
public sealed class InteractionSetDefinition
{
    [JsonProperty("id")]
    public string Id;

    [JsonProperty("interactions")]
    public InteractionDefinition[] Interactions;
}

/// <summary>
/// 상호작용 1개의 불변 정의 = GAS의 Ability.
/// JSON DTO이자 에디터 SO 미러의 직렬화 단위. 로드 후 공유되며 수정하지 않는다.
/// </summary>
[Serializable]
public sealed class InteractionDefinition
{
    [JsonProperty("id")]
    public string Id;

    [JsonProperty("trigger"), JsonConverter(typeof(StringEnumConverter))]
    public ETriggerType Trigger;

    [JsonProperty("mode"), JsonConverter(typeof(StringEnumConverter))]
    public EActivationMode Mode;

    [JsonProperty("maxActivations")]
    public int MaxActivations;       // 0 = 무제한

    [JsonProperty("cooldown")]
    public float Cooldown;           // 초. 0 = 없음

    /// <summary>전부 참이어야 통과. 배열 순서 = 평가 순서. 첫 false에서 중단하므로 싼 조건을 앞에.</summary>
    [JsonProperty("conditions"), SerializeReference]
    public InteractionCondition[] Conditions;

    /// <summary>프로토타입. 실행은 InteractionRuntime의 복제본이 한다.</summary>
    [JsonProperty("effects"), SerializeReference]
    public InteractionEffect[] EffectPrototypes;
}

/// <summary>
/// NPC 1개 * Interaction 1개의 런타임 상태. NPC 스폰 시 1회 할당, 풀 반환 시 Reset.
/// 정의는 공유, 상태와 효과 인스턴스만 NPC별.
/// </summary>
public sealed class InteractionRuntime
{
    public readonly InteractionDefinition Definition;
    public readonly InteractionEffect[] Effects;

    public bool IsActive; // Latched 전용
    public int ActivationCount;
    public float CooldownUntil;

    public InteractionRuntime(InteractionDefinition definition)
    {
        // 1. Definition (그리고 그 안의 Conditions)은 참조로 공유한다.
        Definition = definition;

        // 2. 반면 Effects는 프로토타입을 보고 새로 복제본으로 instance를 만든다 (독립/개별)
        InteractionEffect[] prototypes = definition.EffectPrototypes;
        Effects = new InteractionEffect[prototypes.Length];
        for (int i = 0; i < prototypes.Length; i++)
            Effects[i] = prototypes[i].CreateInstance();
    }

    public void Reset()
    {
        IsActive = false;
        ActivationCount = 0;
        CooldownUntil = 0f;
    }
}