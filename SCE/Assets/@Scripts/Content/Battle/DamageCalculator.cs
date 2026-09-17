using UnityEngine;
using static Define;

/// <summary>
/// 데미지 공식 단일 처리
/// 
/// 책임:
/// - Atk * Multiplier + BaseDamage 공식
/// - Crit 굴림 (매 호출마다 — 매 충돌마다 굴림 정책)
/// - Hurtbox 정보(BodyPart, HitPoint) 통합
/// 
/// 의도하지 않은 책임:
/// - Hurtbox에 대한 부위별 배율 적용 (Hurtbox.OnHit 자체 처리)
/// - 방어력 차감 (피격자의 CalculateFinalDamage가 처리)
/// </summary>
public static class DamageCalculator
{
    public static DamageInfo Calculate(
        CombatCreature caster,
        float damageMultiplier,
        float flatDamage,
        EDamageType damageType,
        EElement element,
        Hurtbox hurtbox = null,
        float knockbackForce = 0f,
        float hitStopDuration = 0f,
        float hitStunDuration = 0f,
        string hitVfxKey = null,
        float cameraShakeStrength = 0f)
    {
        if (caster == null)
            return default;

        // 원천 데미지: Caster.Atk * Multiplier + BaseDamage
        float atk = caster.GetStatValue(EStatType.Atk).Value;
        float rawDamage = atk * damageMultiplier + flatDamage;

        // 크리티컬 (매 호출마다 굴림)
        float critRate = caster.GetStatValue(EStatType.CritRate).Value;
        bool isCritical = Random.value < critRate;
        float critMultiplier = isCritical
            ? Mathf.Max(1f, caster.GetStatValue(EStatType.CritDamage).Value)
            : 1f;

        // 위치 정보 (Hurtbox가 있을 때만)
        Vector3 hitPoint = (hurtbox != null)
            ? hurtbox.transform.position
            : Vector3.zero;
        Vector3 hitDirection = (hurtbox != null)
            ? (hurtbox.transform.position - caster.transform.position).normalized
            : Vector3.zero;

        return new DamageInfo
        {
            Attacker = caster,
            Amount = rawDamage * critMultiplier,
            Type = damageType,
            Element = element,
            //BodyPart = hurtbox != null ? hurtbox.BodyPart : EBodyPart.Body,
            IsCritical = isCritical,
            CriticalMultiplier = critMultiplier,
            HitPoint = hitPoint,
            HitDirection = hitDirection,
            KnockbackForce = knockbackForce,
            HitStopDuration = hitStopDuration,
            HitStunDuration = hitStunDuration,
            HitVfxKey = hitVfxKey,
            CameraShakeStrength = cameraShakeStrength,
        };
    }
}