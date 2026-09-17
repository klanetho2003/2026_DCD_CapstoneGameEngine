using System;
using System.Collections.Generic;
using UnityEngine;
using static Define;
using static LogPrinter;

public struct ObjectTypeComparer : IEqualityComparer<EObjectType>
{
    public bool Equals(EObjectType x, EObjectType y) { return x == y; }

    public int GetHashCode(EObjectType obj) { return (int)obj; }
}

public class CreatureFactory
{
    public T Create<T>(int templateID, Vector3 position) where T : CreatureBase
    {
        // Get Prefab Key
        if (Managers.Data.CreatureDataDic.TryGetValue(templateID, out var creatureData) == false)
        {
            LogPrinter.LogError($"[ObjectManager] Invaild TemplateID >> {templateID}");
            return null;
        }

        string prefabKey = creatureData.prefabName;

        // Check
        if (string.IsNullOrEmpty(prefabKey))
        {
            LogError($"[CreatureFactory] invalid prefab path. TemplateID : {templateID}");
            return null;
        }

        // Spawn
        GameObject go = Managers.Resource.Instantiate(prefabKey, pooling: true);
        go.transform.position = position;

        T creature = go.GetComponent<T>();
        if (creature == null)
        {
            LogError($"[CreatureFactory] spawn Faild. Check {prefabKey}`s Component >>> {typeof(T)}");
            Managers.Resource.Destroy(go);
            return null;
        }

        Physics.SyncTransforms();

        return creature;
    }
}

public class ObjectManager
{
    private CreatureFactory _factory = new CreatureFactory();

    public Villager PossessedTarget { get; private set; }

    #region Object Pools
    public HashSet<MonsterBase> Monsters { get; } = new HashSet<MonsterBase>();
    public HashSet<NPC> NPCs { get; } = new HashSet<NPC>();
    public HashSet<Villager> Villagers { get; } = new HashSet<Villager>();
    #endregion

    private Dictionary<EObjectType, Action<CreatureBase>> _registActions;
    private Dictionary<EObjectType, Action<CreatureBase>> _unregistActions;

    public void Init()
    {
        _registActions = new Dictionary<EObjectType, Action<CreatureBase>>(new ObjectTypeComparer())
        {
            { EObjectType.Villager, c => Villagers.Add(c as Villager) },
            { EObjectType.Monster, c => Monsters.Add(c as MonsterBase) },
            { EObjectType.NPC, c => NPCs.Add(c as NPC) },
            

            // Projectile
            // Trap
            //..
        };

        _unregistActions = new Dictionary<EObjectType, Action<CreatureBase>>(new ObjectTypeComparer())
        {
            { EObjectType.Villager, c => Villagers.Remove(c as Villager) },
            { EObjectType.Monster, c => Monsters.Remove(c as MonsterBase) },
            { EObjectType.NPC, c => NPCs.Remove(c as NPC) },
            

            // Projectile
            // Trap
            //..
        };
    }

    #region Spawn & Despawn
    public T Spawn<T>(int templateID, Vector3 position = default) where T : CreatureBase
    {
        T creature = _factory.Create<T>(templateID, position);

        if (creature == null) return null;

        creature.SetInfo(templateID);

        var objectType = creature.CreatureType;

        if (_registActions.TryGetValue(objectType, out var action))
        {
            action?.Invoke(creature);
        }
        else
        {
            Debug.LogWarning($"[ObjectManager] {objectType} spawn event Faild");
        }

        return creature;
    }

    public void Despawn<T>(T creature) where T : CreatureBase
    {
        if (creature == null || creature.gameObject.IsValid() == false)
            return;

        // NPC가 Possession 대상이었으면 
        if (creature is Villager v && v.IsPossessed)
            Managers.Input.Release(v.StateMachine);

        var objectType = creature.CreatureType;

        if (_unregistActions.TryGetValue(objectType, out var action))
            action?.Invoke(creature);

        Managers.Resource.Destroy(creature.gameObject);
    }
    #endregion

    #region Possession
    public void SetPossession(Villager next)
    {
        if (PossessedTarget == next)
            return;

        Villager prev = PossessedTarget;
        PossessedTarget = next;

        prev?.OnUnpossessed();
        next?.OnPossessed();

        next.UserAim.SetInfo(next);
    }
    #endregion
}
