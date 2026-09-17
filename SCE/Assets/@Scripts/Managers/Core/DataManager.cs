using Data;
using Newtonsoft.Json;
using System.Collections.Generic;
using UnityEngine;

public interface ILoader<Key, Value>
{
    Dictionary<Key, Value> MakeDict();
}

public class DataManager
{   
    public bool IsDataLoaded { get; set; } = false; // Temp

    public Dictionary<int, CreatureData> CreatureDataDic = new();
    public Dictionary<int, VillagerData> VillagerDataDic = new();
    public Dictionary<int, MonsterData> MonsterDataDic = new();
    public Dictionary<int, NPCData> NpcDataDic = new();

    public void InitData()
    {
        #region Creature Data
        VillagerDataDic = LoadJson<VillagerDataLoader, int, VillagerData>("VillagerData").MakeDict();
        MonsterDataDic = LoadJson<MonsterDataLoader, int, MonsterData>("MonsterData").MakeDict();
        NpcDataDic = LoadJson<NPCDataLoader, int, NPCData>("NpcData").MakeDict();

        foreach (var pair in VillagerDataDic)
            CreatureDataDic[pair.Key] = pair.Value;
        foreach (var pair in MonsterDataDic)
            CreatureDataDic[pair.Key] = pair.Value;
        foreach (var pair in NpcDataDic)
            CreatureDataDic[pair.Key] = pair.Value;
        #endregion

        // Interaction
        TextAsset textAsset_1 = Managers.Resource.Load<TextAsset>("villager_temp");
        Managers.Interaction.LoadSet(textAsset_1.text);

        TextAsset textAsset_2 = Managers.Resource.Load<TextAsset>("dev_possess_villager");
        Managers.Interaction.LoadSet(textAsset_2.text);
    }

    private Loader LoadJson<Loader, Key, Value>(string path) where Loader : ILoader<Key, Value>
    {
        TextAsset textAsset = Managers.Resource.Load<TextAsset>(path);
        return JsonConvert.DeserializeObject<Loader>(textAsset.text); // Loader형으로 return할 거고, Loader안에는 MakeDict Method가 있음
    }

    public void Clear()
    {

    }
}
