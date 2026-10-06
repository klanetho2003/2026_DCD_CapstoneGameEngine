using System.Collections.Generic;
using UnityEngine;
using static Define;

/// <summary>
/// Phase에 부품으로써 Aniamtion을 재생을 적용
/// </summary>
[CreateAssetMenu(menuName = "Data/Skill Effect/Play Animation", fileName = "P_PlayAnimation_")]
public class OnEffectPlayAnimationSO : SkillEffectSO
{
    [Tooltip("Animator의 State 이름.")]
    public EUserbleAnimState AnimationStateName;

    public override void Apply(in SkillExecutionContext context)
    {
        if (context.Caster == null)
            return;
        if (AnimationStateName == EUserbleAnimState.None)
            return;
        if (context.Caster.CreatureAnim == null)
            return;
        if (context.Caster.CreatureAnim.AnimationHash.TryGetValue((int)AnimationStateName, out var hash) == false)
            return;
        
        context.Caster.CreatureAnim.PlayState(hash, isForceRestart: true);
    }
}