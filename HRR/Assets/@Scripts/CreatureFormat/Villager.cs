using Data;
using System;
using Unity.Burst.Intrinsics;
using UnityEngine;
using static Define;

public class Villager : NPC, IMovable
{
    public VillagerStateMachine StateMachine { get; private set; }
    public CreatureMovement Movement { get; private set; }
    public VillagerAnim Anim { get; private set; }

    public bool IsPossessed { get { return Managers.Object.PossessedTarget == this; } }

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        // 1. Component Cache
        StateMachine = GetComponent<VillagerStateMachine>();
        Movement = GetComponent<CreatureMovement>();
        Anim = GetComponent<VillagerAnim>();

        return true;
    }

    public override void SetInfo(int objectID)
    {
        base.SetInfo(objectID);

        StateMachine.SetInfo(this);
        Movement.SetInfo(this);
        Anim.SetInfo(this);
    }

    /// <summary>ObjectManager.SetPossession이 호출하는 콜백, 직접 호출 금지.</summary>
    public void OnPossessed()
    {
        LogPrinter.Log($"Target >> {gameObject.name} Connected!");
        StateMachine.OnPossessed();
    }

    /// <summary>ObjectManager.SetPossession이 호출하는 콜백, 직접 호출 금지.</summary>
    public void OnUnpossessed()
    {
        LogPrinter.Log($"Target >> {gameObject.name} Unconnected!");
        StateMachine.OnUnpossessed();
    }

    #region IMovable
    public float MoveSpeed { get { return GetStatValue(EStatType.MoveSpeed).Value; } }

    public Vector2 CacheMoveDirection { get ; set; }

    public void ForceMoveFor(Vector3 direction, float duration, float speedWeight = 1, Action onComplete = null)
    {
        throw new NotImplementedException();
    }

    public void ForceMoveTo(Vector3 destination, float speedWeight = 1, Action onComplete = null)
    {
        throw new NotImplementedException();
    }
    public void StopForceMove(Action onComplete = null)
    {
        throw new NotImplementedException();
    }

    public void Move(Vector3 direction, float speedWeight = 1)
    {
        Movement.Move(direction, speedWeight);
    }
    #endregion
}
