using UnityEngine;
using static LogPrinter;

/// <summary>
/// Setting not Apply
/// Skill 시전 시 Hitbox에 SkillInstance 정보를 주입.
/// 실제 활성화(Collider 켜기)는 Animation Event "OnAttackHitboxOn"이 담당.
/// 
/// CastEffect로 분류 — 시전 즉시 적용.
/// </summary>
[CreateAssetMenu(menuName = "Data/Skill Effect/Look Anim To Aim", fileName = "E_LookAnimToAim_")]
public class EffectLookAnimToAimSO : SkillEffectSO
{

    public override void Apply(in SkillExecutionContext context)
    {
        if (context.Caster is not Villager villager)
            return;

        var handle = villager.UserAim.PointerDirection;
        villager.Anim.RefreshLookAnim(handle);
    }
}