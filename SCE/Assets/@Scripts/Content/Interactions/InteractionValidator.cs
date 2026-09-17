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
        }

        return errors.Count == errorStart;
    }
}