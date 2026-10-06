using UnityEngine;
using static LogPrinter;

/// <summary>
/// Setting not Apply
/// Skill 시전 시 Hitbox에 SkillInstance 정보를 주입.
/// 실제 활성화(Collider 켜기)는 Animation Event "OnAttackHitboxOn"이 담당.
/// 
/// CastEffect로 분류 — 시전 즉시 적용.
/// </summary>

public struct HitboxInfo
{
    public readonly Quaternion LookDirection;

    public HitboxInfo(Quaternion lookDirection)
    {
        LookDirection = lookDirection;
    }
}

[CreateAssetMenu(menuName = "Data/Skill Effect/Hitbox Activation", fileName = "OnCastHitboxActivation_")]
public class OnEffectHitboxActivationSO : SkillEffectSO
{
    [Tooltip("활성화할 Hitbox의 HitboxId (Hitbox.HitboxId).")]
    public string HitboxId = "Write_Id_Here";

    [Tooltip("활성화 시 Hitbox가 바라보는 방향이 Mouse. (False일 경우 Animation 전면)")]
    public bool IsLookMouse = true;

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

        // Default
        float rotation = MathUtil.GetZRotationFromDirection(context.Caster.LookDirection);
        var lookDirection = Quaternion.Euler(0f, 0f, rotation);

        // Hitbox 방향 (To Do. 분기문으로 교체할지 생각)
        if (context.Caster is Villager villager)
        {
            float zPointerRotation = MathUtil.GetZRotationFromDirection(villager.UserAim.PointerDirection);
            var pointerLook = Quaternion.Euler(0f, 0f, zPointerRotation);

            float zInputRotation = MathUtil.GetZRotationFromDirection(villager.StateMachine.CurrentInputHandler.InputDirection);
            var inputLook = Quaternion.Euler(0f, 0f, zInputRotation);

            lookDirection = IsLookMouse? pointerLook : inputLook;
        }

        // Caster에 현재 활성화될 Hitbox 등록 — Animation Event가 사용
        context.Caster.SetActiveHitbox(hitbox, new HitboxInfo(lookDirection));

        LogPrinter.Log($"[OnCastEffectHitboxActivationSO] {context.Caster.gameObject.name} — HitboxId {HitboxId} 등록 (Animation Event 대기)");
    }
}