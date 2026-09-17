using UnityEngine;
using static LogPrinter;

/// <summary>
/// Setting not Apply
/// Skill 시전 시 Hitbox에 SkillInstance 정보를 주입.
/// 실제 활성화(Collider 켜기)는 Animation Event "OnAttackHitboxOn"이 담당.
/// 
/// CastEffect로 분류 — 시전 즉시 적용.
/// </summary>
[CreateAssetMenu(menuName = "Data/Skill Effect/Hitbox Activation", fileName = "OnCastHitboxActivation_")]
public class OnCastEffectHitboxActivationSO : SkillEffectSO
{
    [Tooltip("활성화할 Hitbox의 HitboxId (Hitbox.HitboxId).")]
    public int HitboxId = 0;

    public override void Apply(in SkillExecutionContext context)
    {
        if (context.Caster == null)
            return;

        var hitbox = context.Caster.GetHitbox(HitboxId);
        if (hitbox == null)
        {
            LogPrinter.LogWarning(this, $"[OnCastEffectHitboxActivationSO] HitboxId {HitboxId}를 {context.Caster.gameObject.name}에서 찾을 수 없습니다.");
            return;
        }

        // Caster에 현재 활성화될 Hitbox 등록 — Animation Event가 사용
        context.Caster.SetActiveHitbox(hitbox);

        LogPrinter.Log($"[OnCastEffectHitboxActivationSO] {context.Caster.gameObject.name} — HitboxId {HitboxId} 등록 (Animation Event 대기)");
    }
}