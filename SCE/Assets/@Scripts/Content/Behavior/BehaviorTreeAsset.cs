using System;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>
/// 트리 JSON 하나 = 트리 하나 — 읽은 그대로의 데이터 (컴파일 전). 편집기는 이것을 고쳐 저장하고, 게임은 컴파일해 BehaviorTreeDefinition으로 쓴다.
/// 주소 (Addressable) = Id. 루트는 Composite여야 한다 (컴파일이 본다).
/// 변형 (variants)은 이 트리의 노드 · 설정을 그대로 쓰고 Blackboard 키 기본값만 바꾼다 — 몬스터 데이터의 트리 Id "Id/변형 이름" (BehaviorTreeVariants).
/// </summary>
[Serializable]
[Preserve] // JSON 읽기만 만드는 형식 — 빌드의 코드 줄이기에서 생성자가 지워지지 않게
[JsonObject(MemberSerialization.OptIn)]
public sealed class BehaviorTreeAsset
{
    [JsonProperty("id")] public string Id = "";

    [JsonProperty("settings")] public BehaviorTreeSettings Settings = new BehaviorTreeSettings();

    [JsonProperty("blackboard")] public BehaviorBlackboardKey[] Keys = Array.Empty<BehaviorBlackboardKey>();

    [JsonProperty("variants")] public BehaviorTreeVariant[] Variants = Array.Empty<BehaviorTreeVariant>();

    [SerializeReference, JsonProperty("root")] public BehaviorMainNode Root;

#if UNITY_EDITOR // 편집기 · 시험만 쓴다 (JSON 쓰기) — 플레이어 빌드에서 빠진다
    /// <summary>JSON 쓰기 — 변형이 없으면 "variants" 칸을 쓰지 않는다 (변형 없는 트리 파일은 전과 같은 글). Newtonsoft의 ShouldSerialize 규칙</summary>
    public bool ShouldSerializeVariants()
    {
        return Variants != null && Variants.Length > 0;
    }
#endif
}

/// <summary>트리의 평가 주기 · 거리 LOD — 조종 캐릭터와의 거리로 평가 간격을 늘리거나 평가하지 않는다</summary>
[Serializable]
[Preserve]
[JsonObject(MemberSerialization.OptIn)]
public sealed class BehaviorTreeSettings
{
    /// <summary>평가 간격 (초). 0이면 매 프레임</summary>
    [JsonProperty("tickInterval")] public float TickInterval = 0.1f;

    /// <summary>이 거리 안에서는 평가 간격 그대로, 밖에서는 간격 × FarIntervalMultiplier. 0이면 거리를 보지 않는다</summary>
    [JsonProperty("nearDistance")] public float NearDistance;

    /// <summary>NearDistance 밖의 평가 간격 배수 — 1 이상</summary>
    [JsonProperty("farIntervalMultiplier")] public float FarIntervalMultiplier = 3f;

    /// <summary>이 거리 밖이면 평가하지 않는다 (진행 중 Task도 멈춤 — 이동은 몸이 이어 감). 0이면 늘 평가</summary>
    [JsonProperty("activationDistance")] public float ActivationDistance;
}