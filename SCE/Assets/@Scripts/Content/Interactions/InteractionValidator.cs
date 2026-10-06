using System.Collections.Generic;
using static Define;

/// <summary>
/// Validation Check
/// 정의의 정합성 검사. errors는 로드 거부, warnings는 로드 허용 + 툴 표시.
/// 로더와 에디터 툴이 같은 규칙을 쓴다.
/// </summary>
public static class InteractionValidator
{
    private static readonly HashSet<string> s_idScratch = new HashSet<string>();

    public static bool Validate(InteractionSetDefinition set, List<string> errors, List<string> warnings)
    {
        int errorStart = errors.Count;

        if (set == null)
        {
            errors.Add("Set이 null");
            return false;
        }
        if (string.IsNullOrEmpty(set.Id))
            errors.Add("Set.Id 비어 있음");

        s_idScratch.Clear();
        InteractionDefinition[] list = set.Interactions;
        for (int i = 0; i < list.Length; i++)
        {
            InteractionDefinition def = list[i];
            string where = $"{set.Id}[{i}]";

            if (def == null)
            {
                errors.Add($"{where}: null");
                continue;
            }

            if (string.IsNullOrEmpty(def.Id))
                errors.Add($"{where}: Id 비어 있음");
            else if (s_idScratch.Add(def.Id) == false)
                errors.Add($"{where}: Id 중복 '{def.Id}'");

            if ((uint)def.Trigger >= (uint)ETriggerType.Count)
                errors.Add($"{where}: Trigger 범위 밖 {def.Trigger}");
            if (def.MaxActivations < 0)
                errors.Add($"{where}: MaxActivations 음수");
            if (def.Cooldown < 0f)
                errors.Add($"{where}: Cooldown 음수");

            for (int c = 0; c < def.Conditions.Length; c++)
            {
                if (def.Conditions[c] == null)
                    errors.Add($"{where}: Conditions[{c}] null");
            }

            for (int e = 0; e < def.EffectPrototypes.Length; e++)
            {
                if (def.EffectPrototypes[e] == null)
                    errors.Add($"{where}: Effects[{e}] null");
            }
                

            // ── 경고: 동작은 하지만 의도와 다를 가능성이 높은 조합
            if (def.EffectPrototypes.Length == 0)
                warnings.Add($"{where}: 효과 없음");

            if (def.Mode == EActivationMode.Latched)
            {
                for (int e = 0; e < def.EffectPrototypes.Length; e++)
                {
                    InteractionEffect fx = def.EffectPrototypes[e];
                    if (fx != null && fx.SupportsDeactivate == false)
                        warnings.Add($"{where}: Latched 모드인데 {fx.GetType().Name} 은 Deactivate 미지원 — 켜진 뒤 꺼지지 않음");
                }
            }
            else if (def.Trigger == ETriggerType.Tick && def.Cooldown <= 0f && def.MaxActivations == 0)
            {
                warnings.Add($"{where}: Fire + Tick + 쿨다운 0 + 무제한 — 조건이 참인 동안 매 프레임 실행됨");
            }

            if (def.Trigger == ETriggerType.StateChanged)
            {
                bool readsState = false;
                for (int c = 0; c < def.Conditions.Length; c++)
                {
                    if (def.Conditions[c] != null && def.Conditions[c].ReadsGameState)
                    {
                        readsState = true;
                        break;
                    }
                }
                if (readsState == false)
                    warnings.Add($"{where}: StateChanged인데 상태 조건 없음 — 관계없는 상태가 바뀌어도 평가됨");

                if (def.Mode == EActivationMode.Fire && def.MaxActivations == 0 && def.Cooldown <= 0f)
                    warnings.Add($"{where}: StateChanged + Fire + 무제한 — 조건이 참인 동안 상태가 바뀔 때마다 반복 실행. MaxActivations 1 또는 Latched 권장");
            }

            // 비용 순서: 비싼 조건이 싼 조건보다 앞이면 경고
            ENodeCost maxSoFar = ENodeCost.Cheap;
            for (int c = 0; c < def.Conditions.Length; c++)
            {
                InteractionCondition cond = def.Conditions[c];
                if (cond == null)
                    continue;
                if (cond.Cost < maxSoFar)
                {
                    warnings.Add($"{where}: Conditions[{c}] {cond.GetType().Name}({cond.Cost}) 가 더 비싼 조건 뒤에 있음 — 순서를 바꾸면 early-out 이득");
                    break;
                }
                if (cond.Cost > maxSoFar)
                    maxSoFar = cond.Cost;
            }

            // ── 스폰 단계 규칙
            bool isSpawnRequest = def.Trigger == ETriggerType.SpawnRequest;

            for (int e = 0; e < def.EffectPrototypes.Length; e++)
            {
                InteractionEffect fx = def.EffectPrototypes[e];
                if (fx == null) continue;

                bool spawnPhaseEffect = fx.Phase == EEffectPhase.SpawnRequest;
                if (isSpawnRequest && spawnPhaseEffect == false)
                    errors.Add($"{where}: SpawnRequest에는 스폰 요청 효과만 — {fx.GetType().Name}은 스폰된 인스턴스가 필요");
                else if (isSpawnRequest == false && spawnPhaseEffect)
                    errors.Add($"{where}: {fx.GetType().Name}은 SpawnRequest 트리거 전용");
            }

            if (isSpawnRequest)
            {
                for (int c = 0; c < def.Conditions.Length; c++)
                {
                    InteractionCondition cond = def.Conditions[c];
                    if (cond != null && cond.IsContextFree == false)
                        errors.Add($"{where}: SpawnRequest 조건은 상태 조건만 — {cond.GetType().Name}은 스폰 전에 평가할 수 없음");
                }

                if (def.Mode != EActivationMode.Fire)
                    errors.Add($"{where}: SpawnRequest는 Fire만 허용");
                if (def.MaxActivations != 0 || def.Cooldown > 0f)
                    warnings.Add($"{where}: SpawnRequest에서는 MaxActivations·Cooldown이 적용되지 않음 (NPC별 런타임 상태 없음)");
            }

            if (def.Trigger == ETriggerType.Spawned && def.Mode == EActivationMode.Latched)
                warnings.Add($"{where}: Spawned는 스폰 시 1회 평가 — Latched는 디스폰 때만 해제됨");
        }

        return errors.Count == errorStart;
    }
}