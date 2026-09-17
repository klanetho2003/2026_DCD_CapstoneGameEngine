using UnityEngine;

/// <summary>
/// Hitbox 충돌 시 적용되는 효과. 다타 히트 시 매 충돌마다 적용.
/// 데미지, 넉백, 출혈, 독 등.
/// </summary>
public abstract class SkillHitEffectSO : ScriptableObject
{
    public abstract void ApplyOnHit(SkillInstance skill, Hurtbox hurtbox);
}