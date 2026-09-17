using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CastEffect 리스트를 일괄 실행하는 phase
/// </summary>
[CreateAssetMenu(menuName = "Data/Skill/Phase/Apply Effects", fileName = "P_ApplyEffects_")]
public class ApplyEffectsPhaseSO : SkillPhaseSO
{
    public List<SkillEffectSO> Effects = new();

    public override void OnEnter(SkillPhaseContext ctx)
    {
        var context = new SkillExecutionContext { Caster = ctx.Caster, Skill = ctx.Skill };
        for (int i = 0; i < Effects.Count; i++)
            Effects[i]?.Apply(in context);
    }

    public override EPhaseState Tick(SkillPhaseContext ctx, float deltaTime)
    {
        return EPhaseState.Completed;
    }
}