using UnityEngine;

/// <summary>
/// 파일로 생성하여 Phase 내 ApplyEffectPhaseSO 리스트에 조립.
/// 
/// 구현체 예시:
/// - DamageEffectSO: 대상에 데미지
/// - HealEffectSO: HP 회복
/// - StatBuffEffectSO: StatModifier 추가
/// - KnockbackEffectSO: 넉백
/// - ProjectileSpawnEffectSO: 투사체 생성
/// 
/// in 매개변수로 struct copy 회피.
/// </summary>

// [CreateAssetMenu(menuName = "Data/Skill Effect/Skill Cast Effect/..", fileName = "EXname_")]

public abstract class SkillEffectSO : ScriptableObject
{
    public abstract void Apply(in SkillExecutionContext context);
}