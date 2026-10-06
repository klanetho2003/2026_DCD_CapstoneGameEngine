using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using UnityEngine;
using static Define;

/// <summary>조건 평가의 단일 구현. InteractionComponent와 스폰 요청 해석기가 공유한다.</summary>
public static class InteractionEvaluator
{
    public static bool AllConditions(InteractionCondition[] conditions, in InteractionContext ctx)
    {
        for (int i = 0; i < conditions.Length; i++)
        {
            if (conditions[i].Evaluate(in ctx) == false)
                return false; // early-out — 배열 순서가 곧 평가 순서
        }
        return true;
    }
}

#region Distance
[Serializable]
[InteractionNode("Distance", "거리", "촉발 주체와 NPC의 거리가 Range 이하")]
public sealed class DistanceCondition : InteractionCondition
{
    [JsonProperty("range")]
    public float Range = 2f;

    public override bool Evaluate(in InteractionContext ctx)
    {
        if (ctx.Instigator == null)
            return false;

        // sqrMagnitude: 제곱근 없음. Range*Range는 곱셈 1회라 캐싱 불필요 (public 필드라 로드 후 캐시 무효화 지점이 없음)
        Vector3 delta = ctx.Instigator.transform.position - ctx.Owner.transform.position;
        return delta.sqrMagnitude <= Range * Range;
    }
}
#endregion

#region InZone
[Serializable]
[InteractionNode("InZone", "존 안", "촉발 주체가 NPC의 트리거 콜라이더 안에 있음")]
public sealed class InZoneCondition : InteractionCondition
{
    public override bool Evaluate(in InteractionContext ctx)
    {
        return ctx.Source.IsInZone(ctx.Instigator); // Owner 경유 없이 컴포넌트 직접 참조
    }
}
#endregion

#region StatThreshold
[Serializable]
[InteractionNode("StatThreshold", "스탯 조건", "대상의 스탯을 Value와 비교. Percent면 MaxStat 대비 비율(0~1)로 비교")]
public sealed class StatThresholdCondition : InteractionCondition
{
    [JsonProperty("target"), JsonConverter(typeof(StringEnumConverter))]
    public ETargetRef Target = ETargetRef.Owner;

    [JsonProperty("objectId")]
    public int ObjectId;            // Target == ObjectId 일 때만 사용

    [JsonProperty("stat"), JsonConverter(typeof(StringEnumConverter))]
    public EStatType Stat;

    [JsonProperty("op"), JsonConverter(typeof(StringEnumConverter))]
    public EComparison Op = EComparison.LessOrEqual;

    [JsonProperty("value")]
    public float Value;

    [JsonProperty("percent")]
    public bool Percent;

    [JsonProperty("maxStat"), JsonConverter(typeof(StringEnumConverter))]
    public EStatType MaxStat;       // Percent == true 일 때 분모

    [JsonIgnore]
    public override ENodeCost Cost
    {
        get { return Target == ETargetRef.ObjectId ? ENodeCost.Moderate : ENodeCost.Cheap; } // ObjectId는 매니저 조회
    }

    public override bool Evaluate(in InteractionContext ctx)
    {
        CreatureBase target = ctx.Resolve(Target, ObjectId);
        if (target == null)
            return false;

        float current = target.GetStatValue(Stat).Value;
        if (Percent)
        {
            float max = target.GetStatValue(MaxStat).Value;
            if (max <= 0f) return false;
            current /= max;
        }

        return Util.Evaluate(current, Op, Value);
    }
}
#endregion

#region Not
[Serializable]
[InteractionNode("Not", "부정", "안쪽 조건의 반대")]
public sealed class NotCondition : InteractionCondition, IInteractionNodeLoadHook
{
    [JsonProperty("inner"), SerializeReference]
    public InteractionCondition Inner;

    [JsonIgnore]
    public override ENodeCost Cost { get { return Inner != null ? Inner.Cost : ENodeCost.Cheap; } }

    [JsonIgnore]
    public override bool ReadsGameState { get { return Inner != null && Inner.ReadsGameState; } }

    [JsonIgnore]
    public override bool IsContextFree { get { return Inner != null && Inner.IsContextFree; } }

    /// <summary>중첩 노드의 로드 훅을 대신 전달 — Not(StateValue)의 키도 로드 시 검증된다.</summary>
    public void OnLoad(StateKeyRegistry stateKeys, string where, List<string> errors)
    {
        if (Inner is IInteractionNodeLoadHook hook)
            hook.OnLoad(stateKeys, where + ".inner", errors);
    }

    public override bool Evaluate(in InteractionContext ctx)
    {
        return Inner != null && Inner.Evaluate(in ctx) == false;
    }
}
#endregion