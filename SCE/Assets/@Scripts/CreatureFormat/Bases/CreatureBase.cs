using Data;
using System.Collections.Generic;
using UnityEditor.ShaderKeywordFilter;
using UnityEngine;
using static Define;
using static LogPrinter;

public abstract class CreatureBase : InitBase, IStatModifierController
{
    private CreatureData _creatureData;
    public T GetCreatureData<T>() where T : CreatureData { return _creatureData as T; }
    public EObjectType CreatureType { get { return _creatureData.creatureType; } }
    protected CreatureStatDefine _creatureStat;
    protected CreatureStatResource _creatureResource;

    public InteractionComponent Interaction { get; private set; }

    public abstract Vector2 LookDirection { get; }
    public abstract BaseCreatureAnim CreatureAnim { get; }

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
        _creatureData = Managers.Data.CreatureDataDic[objectID];

        InitStat();

        SetupInteraction();
    }

    public virtual void InitStat()
    {
        // Init Define Stat
        _creatureStat.InitDefineStat(_creatureData.StatDatas);

        // Init Resource Stat
        _creatureResource.InitResourcesStat(_creatureData.StatResourceDatas, _creatureStat);
    }

    /// <summary>InteractionComponent가 붙어 있으면 CreatureData.InteractionSetId로 초기화한다.</summary>
    private void SetupInteraction()
    {
        Interaction = GetComponent<InteractionComponent>();

        string setId = _creatureData != null ? _creatureData.InteractionSetId : null;
        bool hasSetId = string.IsNullOrEmpty(setId) == false;

        if (Interaction == null)
        {
            // 데이터는 상호작용을 지정했는데 컴포넌트가 없다 → 프리팹 부착 누락
            if (hasSetId)
                LogPrinter.LogError($"[CreatureBase] InteractionSetId '{setId}' 가 지정됐지만 InteractionComponent 미부착 >> {gameObject.name}");
            return;
        }

        InteractionSetDefinition set = null;
        if (hasSetId)
        {
            set = Managers.Interaction.GetSet(setId);
            if (set == null)
                LogPrinter.LogError($"[CreatureBase] InteractionSet 없음 >> {setId} ({gameObject.name})");
        }

        Interaction.SetInfo(this, set);
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