using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;
using static Define;

namespace Data
{
    #region Creature Data
    [Serializable]
    public class CreatureData
    {
        public int templateID;

        public EObjectType creatureType;
        public string prefabName;

        // InteractionSetDefinition.Id. 비어 있으면 상호작용 없음. (보스가 싸움에서 지고 말을 걸 수도 있지 않을까)
        public string InteractionSetId;

        public List<StatData> StatDatas;
        public List<StatResourceData> StatResourceDatas;
    }

    [Serializable]
    public class StatData
    {
        public EStatType sType;
        public float sValue;
    }
    [Serializable]
    public class StatResourceData
    {
        public EStatType sType;
        public EStatResourceType rType;
        public float rValue;
    }

    [Serializable]
    public class CombatCreatureData : CreatureData
    {
        public string hurtboxLayoutKey;

        public List<int> SkillIDs;
    }

    [Serializable]
    public class MonsterData : CombatCreatureData
    {

    }

    [Serializable]
    public class MonsterDataLoader : ILoader<int, MonsterData>
    {
        public List<MonsterData> Datas = new List<MonsterData>();
        public Dictionary<int, MonsterData> MakeDict()
        {
            Dictionary<int, MonsterData> dict = new Dictionary<int, MonsterData>();
            foreach (MonsterData data in Datas)
                dict.Add(data.templateID, data);

            return dict;
        }
    }

    [Serializable]
    public class NPCData : CreatureData
    {
        
    }

    [Serializable]
    public class NPCDataLoader : ILoader<int, NPCData>
    {
        public List<NPCData> Datas = new List<NPCData>();
        public Dictionary<int, NPCData> MakeDict()
        {
            Dictionary<int, NPCData> dict = new Dictionary<int, NPCData>();
            foreach (NPCData data in Datas)
                dict.Add(data.templateID, data);

            return dict;
        }
    }

    [Serializable]
    public class VillagerData : CombatCreatureData
    {
        public string skillSlotLayoutKey;
    }

    [Serializable]
    public class VillagerDataLoader : ILoader<int, VillagerData>
    {
        public List<VillagerData> Datas = new List<VillagerData>();
        public Dictionary<int, VillagerData> MakeDict()
        {
            Dictionary<int, VillagerData> dict = new Dictionary<int, VillagerData>();
            foreach (VillagerData data in Datas)
                dict.Add(data.templateID, data);

            return dict;
        }
    }
    #endregion
}