using Data;
using System.Collections.Generic;
using UnityEngine;
using static Define;
using static LogPrinter;

public abstract class CreatureBase : InitBase, IStatModifierController
{
    public CreatureData CreatureData { get; private set; }
    public EObjectType CreatureType { get { return CreatureData.creatureType; } }
    protected CreatureStatDefine _creatureStat;
    protected CreatureStatResource _creatureResource;

    public abstract Vector3 LookDirection { get; }

    #region Tags
    public ECreatureTag CurrentTags { get; private set; }
    public bool HasTag(ECreatureTag tag) { return (CurrentTags & tag) != 0; } // CurrentTags |= (Attacking | Invincible | Healing)
    public void AddTag(ECreatureTag tag) { CurrentTags |= tag; }
    public void RemoveTag(ECreatureTag tag) { CurrentTags &= ~tag; }
    public void ClearTags() { CurrentTags = ECreatureTag.None; }
    #endregion

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        _creatureStat = new CreatureStatDefine();
        _creatureResource = new CreatureStatResource();

        return true;
    }

    public virtual void SetInfo(int objectID)
    {
        CreatureData = Managers.Data.CreatureDataDic[objectID];

        InitStat();
    }

    public virtual void InitStat()
    {
        // Init Define Stat
        _creatureStat.InitDefineStat(CreatureData.StatDatas);

        // Init Resource Stat
        _creatureResource.InitResourcesStat(CreatureData.StatResourceDatas, _creatureStat);
    }

    public StatResourceValue GetResourceStat(EStatResourceType resourceStatType) { return _creatureResource.GetResource(resourceStatType); }

    #region IStatModifierController
    /// <summary>
    /// StatDefineValue 객체 자체에 접근. UI 표시(Modifiers 리스트 조회) 등에 사용.
    /// </summary>
    public StatDefineValue GetStatValue(EStatType type) { return _creatureStat.GetStatValue(type); }

    /// <summary>
    /// 모디파이어 추가. ex, AddStatModifier(EStatType.Def, new StatModifier(0.3f, EStatModType.PercentAdd, source: armor));
    /// </summary>
    public void AddStatModifier(EStatType type, StatModifier modifier) { _creatureStat.AddModifier(type, modifier); }

    public bool RemoveStatModifier(EStatType type, StatModifier modifier) { return _creatureStat.RemoveModifier(type, modifier); }

    /// <summary>
    /// 특정 source의 모든 modifier를 일괄 제거.
    /// 장비 해제, 버프 종료 시 호출.
    /// </summary>
    public int ClearStatModifiersFromSource(object source) { return _creatureStat.RemoveModifiersFromSource(source); }
    #endregion
}