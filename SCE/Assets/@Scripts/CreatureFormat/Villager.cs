using Data;
using System;
using Unity.Burst.Intrinsics;
using UnityEngine;
using static Define;

public class Villager : CombatCreature, IMovable
{
    public VillagerData Data { get; private set; }

    public VillagerStateMachine StateMachine { get; private set; }
    public CreatureMovement Movement { get; private set; }
    public VillagerAnim Anim { get; private set; }
    public override BaseCreatureAnim CreatureAnim => Anim;
    public UserAim UserAim { get; private set; }

    public bool IsPossessed { get { return Managers.Object.PossessedTarget == this; } }

    public override Vector2 LookDirection => throw new NotImplementedException();

    // skill
    public SkillSlots SkillSlots { get; private set; }

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        // 1. Component Cache
        StateMachine = GetComponent<VillagerStateMachine>();
        Movement = GetComponent<CreatureMovement>();
        Anim = GetComponent<VillagerAnim>();
        UserAim = GetComponent<UserAim>();

        SkillSlots = new SkillSlots();

        return true;
    }

    public override void SetInfo(int objectID)
    {
        base.SetInfo(objectID);

        Data = GetCreatureData<VillagerData>();

        StateMachine.SetInfo(this);
        Movement.SetInfo(this);
        Anim.SetInfo(this);
        UserAim.SetInfo(this);

        SkillSlots.SetInfo(this);

        ApplyPossessionHurtbox();
    }

    #region Possession
    /// <summary>ObjectManager.SetPossession이 호출하는 콜백, 직접 호출 금지.</summary>
    public void OnPossessed()
    {
        LogPrinter.Log($"Target >> {gameObject.name} Connected!");
        StateMachine.OnPossessed();
        ApplyPossessionHurtbox();
    }

    /// <summary>ObjectManager.SetPossession이 호출하는 콜백, 직접 호출 금지.</summary>
    public void OnUnpossessed()
    {
        LogPrinter.Log($"Target >> {gameObject.name} Unconnected!");
        StateMachine.OnUnpossessed();
        ApplyPossessionHurtbox();
    }

    /// <summary>
    /// 빙의 상태에 따른 피격 판정 전환.
    /// IsValidTarget, Hurtbox까지 끄기 2중 check
    /// </summary>
    private void ApplyPossessionHurtbox()
    {
        if (IsPossessed)
            RevertToDefaultHurtboxLayout();
        else
            SwitchHurtboxLayout("None");
    }
    #endregion

    #region CombatCreature
    /// <summary>
    /// 빙의 중이 아니면 어떤 공격의 대상도 되지 않는다.
    /// </summary>
    public override bool IsValidTarget(EObjectType objType)
    {
        if (IsPossessed == false)
            return false;

        return objType == EObjectType.Monster; // 몬스터의 공격만 유효
    }

    protected override float CalculateFinalDamage(DamageInfo damageInfo)
    {
        // To Do. 방어력·속성 저항 반영. 현재는 원본 데미지 그대로
        return damageInfo.Amount;
    }

    protected override void OnAfterDamageApplied(DamageInfo damageInfo, float finalDamage)
    {
        StateMachine.SetState(EUserInputState.Damaged);
    }

    protected override void OnDie()
    {
        // To Do. 사망 연출. 빙의 중이었다면 빙의 해제가 선행 필요
        LogPrinter.Log($"[Villager] {Data.prefabName} Died");
    }
    #endregion

    #region IMovable
    public float MoveSpeed { get { return GetStatValue(EStatType.MoveSpeed).Value; } }

    public Vector2 CacheMoveDirection { get ; set; }

    public void Move(Vector2 direction, float speedWeight = 1)
    {
        Movement.Move(direction, speedWeight);
    }

    public void ForceMoveFor(Vector2 direction, float duration, float speedWeight = 1, Action onComplete = null)
    {
        throw new NotImplementedException();
    }

    public void ForceMoveTo(Vector2 destination, float speedWeight = 1, Action onComplete = null)
    {
        throw new NotImplementedException();
    }
    public void StopForceMove(Action onComplete = null)
    {
        throw new NotImplementedException();
    }
    #endregion
}
