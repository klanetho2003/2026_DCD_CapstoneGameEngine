using System.Collections.Generic;

/// <summary>Set 안의 모든 조건, 효과에 로드 훅을 실행한다. 중첩 노드(Not 등)는 노드 스스로 전달한다.</summary>
public static class InteractionNodeLoadHooks
{
    public static bool Run(InteractionSetDefinition set, StateKeyRegistry stateKeys, List<string> errors)
    {
        int errorStart = errors.Count;

        for (int i = 0; i < set.Interactions.Length; i++)
        {
            InteractionDefinition def = set.Interactions[i];
            if (def == null)
                continue;

            for (int c = 0; c < def.Conditions.Length; c++)
            {
                if (def.Conditions[c] is IInteractionNodeLoadHook hook) // 훅이 있을 때만 위치 문자열 생성
                    hook.OnLoad(stateKeys, $"{set.Id}[{i}].conditions[{c}]", errors);
            }

            for (int e = 0; e < def.EffectPrototypes.Length; e++)
            {
                if (def.EffectPrototypes[e] is IInteractionNodeLoadHook hook)
                    hook.OnLoad(stateKeys, $"{set.Id}[{i}].effects[{e}]", errors);
            }
        }

        return errors.Count == errorStart;
    }
}