using Data;
using System;
using UnityEngine;
using static Define;

public class MonsterBase : CombatCreature, IMovable
{
    public MonsterData Data { get; private set; }

    public override Vector2 LookDirection => throw new System.NotImplementedException();

    public override BaseCreatureAnim CreatureAnim => throw new System.NotImplementedException(); // todo monster anim

    public CreatureMovement Movement { get; private set; }

    public float MoveSpeed => throw new NotImplementedException();

    public Vector2 CacheMoveDirection { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        Movement = GetComponent<CreatureMovement>();

        return true;
    }

    public override void SetInfo(int objectID)
    {
        base.SetInfo(objectID);

        Data = GetCreatureData<MonsterData>();
        Movement.SetInfo(this);
    }

    #region Combat Creature
    public override bool IsValidTarget(EObjectType objType)
    {
        return objType == EObjectType.Villager;
    }

    protected override float CalculateFinalDamage(DamageInfo damageInfo)
    {
        return damageInfo.Amount;
    }

    protected override void OnAfterDamageApplied(DamageInfo damageInfo, float finalDamage)
    {
        //throw new System.NotImplementedException();
    }

    protected override void OnDie()
    {
        LogPrinter.Log($"[MonsterBase] {Data.prefabName} Died");
    }
    #endregion

    #region Movable

    public void Move(Vector2 direction, float speedWeight = 1)
    {
        Movement.Move(direction, speedWeight);
    }

    public void ForceMoveTo(Vector2 destination, float speedWeight = 1, Action onComplete = null)
    {
        Movement.ForceMoveTo(destination, speedWeight, onComplete);
    }

    public void ForceMoveFor(Vector2 direction, float duration, float speedWeight = 1, Action onComplete = null)
    {
        throw new NotImplementedException();
    }

    public void StopForceMove(Action onComplete = null)
    {
        throw new NotImplementedException();
    }
    #endregion
}
