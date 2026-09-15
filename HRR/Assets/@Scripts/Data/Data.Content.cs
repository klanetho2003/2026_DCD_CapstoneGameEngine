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

        public string hurtboxLayoutKey;

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
    public class MonsterData : CreatureData
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
        [JsonProperty("interactionSetId")]
        public string InteractionSetId;
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
    public class VillagerData : NPCData
    {

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