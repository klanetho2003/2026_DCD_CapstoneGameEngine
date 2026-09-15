using UnityEngine;
using static Define;

public interface IDamageable
{
    bool IsDead { get; }
    void OnDamaged(DamageInfo damageInfo);
}

public struct DamageInfo
{
    // Hitbox
    public CombatCreature Attacker;         // 공격자
    public float Amount;                    // 베이스 + 크리티컬 적용 후 (방어력 차감 전)
    public EDamageType Type;                // Physical / Magical / True
    public EElement Element;                // None / Fire / Ice / ...
    //public EBodyPart BodyPart;              // 피격 부위 (머리/몸통/다리 등 - Hurtbox 레이아웃에 따라 달라짐)
    public bool IsCritical;                 // 크리티컬 적중 여부
    public float CriticalMultiplier;        // 적용된 크리티컬 배율 (UI 표기 - 로그용)


    public Vector3 HitPoint;                // 피격 위치 (이펙트 - UI용)
    public Vector3 HitDirection;            // 넉백 방향 (정규화)
    public float KnockbackForce;            // 넉백 강도 - Hitbox에 필드 추가
    public float HitStopDuration;           // 히트스톱 시간 - TODO
    public float HitStunDuration;           // 피격 경직 시간 - TODO

    public string HitVfxKey;
    public float CameraShakeStrength;

    // public List<EStatusEffect> StatusEffects;    // 화상/슬로우/스턴 등 상태이상
    // public float LifeStealRatio;                 // 흡혈 비율
    // public IDamageModifier[] Modifiers;          // 장비/버프 효과
}
