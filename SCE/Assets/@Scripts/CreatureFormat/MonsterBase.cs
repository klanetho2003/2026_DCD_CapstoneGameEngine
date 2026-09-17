using Data;
using UnityEngine;
using static Define;

public class MonsterBase : CombatCreature
{
    public MonsterData Data { get; private set; }

    public override Vector2 LookDirection => throw new System.NotImplementedException();

    public override BaseCreatureAnim CreatureAnim => throw new System.NotImplementedException(); // todo monster anim

    public override void SetInfo(int objectID)
    {
        base.SetInfo(objectID);

        Data = GetCreatureData<MonsterData>();
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
}
