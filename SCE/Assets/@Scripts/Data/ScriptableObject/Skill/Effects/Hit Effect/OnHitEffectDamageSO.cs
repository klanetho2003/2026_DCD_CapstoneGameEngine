using UnityEngine;
using static Define;

/// <summary>
/// Hitbox 충돌 시 데미지 적용. DamageCalculator에 위임.
/// HitEffect로 분류 — 충돌 시점에 적용.
/// </summary>
[CreateAssetMenu(menuName = "Data/Skill Effect/Skill Hit Effect/Damage", fileName = "OnHitDamage_")]
public class OnHitEffectDamageSO : SkillHitEffectSO
{
    public EDamageType DamageType = EDamageType.Physical;
    public EElement Element = EElement.None;
    public float damageMultiplier = 1.0f;
    public float FlatDamage = 0f;

    [Tooltip("피격 지점 vfx의 Addressable key. 비우면 재생 안 함.")]
    [SerializeField] private string _hitVfxKey;

    [Tooltip("적중 시 히트스톱 시간(초). 0이면 없음.")]
    [SerializeField] private float _hitStopDuration = 0.04f;

    // OnHitEffectDamageSO에 추가:
    [Tooltip("적중 시 카메라 셰이크 강도. 0=없음.")]
    [SerializeField] private float _cameraShakeStrength = 0.3f;

    public override void ApplyOnHit(SkillInstance skill, Hurtbox hurtbox)
    {
        if (skill == null || hurtbox == null)
            return;

        var damageInfo = DamageCalculator.Calculate(
            skill.Owner,
            damageMultiplier,
            FlatDamage,
            DamageType,
            Element,
            hurtbox,
            hitStopDuration: _hitStopDuration,
            hitVfxKey: _hitVfxKey,
            cameraShakeStrength: _cameraShakeStrength);

        hurtbox.OnHit(damageInfo);
    }
}