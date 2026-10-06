using Data;
using UnityEngine;
using static Define;

/// <summary>
/// 스폰 요청 단계에서 효과가 수정하는 요청.
/// 확장 지점: 새 교체 종류가 필요하면 필드를 추가하고, CreatureSpawnHelper가 그 필드를 반영하도록 한다.
/// 해석기가 재사용하는 객체이므로 효과는 참조를 보관하지 말 것.
/// </summary>
public sealed class SpawnRequest
{
    /// <summary>원래 요청된 templateID (읽기 전용)</summary>
    public int RequestedDataId { get; private set; }

    /// <summary>스폰될 셀 (읽기 전용)</summary>
    public Vector2Int Cell { get; private set; }

    /// <summary>실제로 스폰할 templateID — Effect가 바꿀 수 있다</summary>
    public int DataId;

    public void Begin(int requestedDataId, Vector2Int cell)
    {
        RequestedDataId = requestedDataId;
        Cell = cell;
        DataId = requestedDataId;
    }
}

/// <summary>
/// 스폰 요청 단계 해석기. 요청된 templateID의 InteractionSet에서 SpawnRequest 상호작용을 평가해 실제 스폰할 ID를 결정한다.
/// - 1회만 평가 (교체된 ID의 SpawnRequest는 평가하지 않음 — 순환 차단)
/// - 요청 객체 재사용, 컨텍스트는 struct — 할당 0
/// </summary>
public static class SpawnRequestResolver
{
    private const int SpawnRequestBit = 1 << (int)ETriggerType.SpawnRequest;

    private static readonly SpawnRequest s_request = new SpawnRequest();
    private static bool s_isResolving;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static bool Trace = true;
#endif

    public static int Resolve(int requestedDataId, Vector2Int cell, string context)
    {
        if (Managers.Data.CreatureDataDic.TryGetValue(requestedDataId, out CreatureData data) == false)
            return requestedDataId; // 스폰 쪽에서 에러로 드러난다

        if (string.IsNullOrEmpty(data.InteractionSetId))
            return requestedDataId;

        InteractionSetDefinition set = Managers.Interaction.GetSet(data.InteractionSetId);
        if (set == null || (set.TriggerMask & SpawnRequestBit) == 0)
            return requestedDataId; // 대부분의 NPC는 여기서 끝

        if (s_isResolving) // 스폰 요청 효과는 스폰하지 않으므로 정상 경로에서는 발생하지 않는다
        {
            LogPrinter.LogError($"[SpawnRequestResolver] {context}: 해석 중 재진입 — 원래 ID로 스폰");
            return requestedDataId;
        }

        s_isResolving = true;
        s_request.Begin(requestedDataId, cell);
        try
        {
            var ctx = new InteractionContext(null, null, Managers.Object.PossessedTarget,
                ETriggerType.SpawnRequest, Time.time, s_request);

            InteractionDefinition[] defs = set.Interactions;
            for (int i = 0; i < defs.Length; i++)
            {
                InteractionDefinition def = defs[i];
                if (def.Trigger != ETriggerType.SpawnRequest)
                    continue;
                if (InteractionEvaluator.AllConditions(def.Conditions, in ctx) == false)
                    continue;

                // 스폰 요청 효과는 무상태 — 프로토타입 직접 실행. 여러 개가 참이면 선언 순서상 마지막이 적용된다
                InteractionEffect[] effects = def.EffectPrototypes;
                for (int e = 0; e < effects.Length; e++)
                    effects[e].OnActivate(in ctx);
            }
        }
        finally
        {
            s_isResolving = false;
        }

        int resolved = s_request.DataId;
        if (resolved == requestedDataId)
            return requestedDataId;

        if (Managers.Data.CreatureDataDic.TryGetValue(resolved, out CreatureData swapped) == false)
        {
            LogPrinter.LogError($"[SpawnRequestResolver] {context}: 교체 대상 templateID {resolved} 없음 — 원래 {requestedDataId}로 스폰");
            return requestedDataId;
        }
        if (swapped.creatureType != data.creatureType)
        {
            LogPrinter.LogError($"[SpawnRequestResolver] {context}: 종류가 다른 데이터로 교체 불가 ({data.creatureType} {requestedDataId} → {swapped.creatureType} {resolved}) — 원래 ID로 스폰");
            return requestedDataId;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Trace)
            LogPrinter.Log($"[SpawnRequest] {context}: {requestedDataId} → {resolved}");
#endif
        return resolved;
    }
}