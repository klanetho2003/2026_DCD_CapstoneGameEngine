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
    /// <summary>크리처 데이터 표의 Addressable 주소 — 게임(InitData)과 편집기(EditorGameData)가 함께 쓴다.</summary>
    public const string VillagerDataKey = "PlayerData";
    public const string MonsterDataKey = "MonsterData";
    public const string NpcDataKey = "NpcData";

    /// <summary>사용자가 인터랙션 에디터에서 박스 밀기 카드를 조립할 빈 Set의 Addressable 주소다.</summary>
    public const string PlayerSokobanSetKey = "player_sokoban";

    public bool IsDataLoaded { get; set; } = false; // Temp

    public Dictionary<int, CreatureData> CreatureDataDic = new();
    public Dictionary<int, VillagerData> VillagerDataDic = new();
    public Dictionary<int, MonsterData> MonsterDataDic = new();
    public Dictionary<int, NPCData> NpcDataDic = new();

    /// <summary>캐릭터와 게임 상태 데이터를 읽고 플레이어 박스 밀기 Set을 포함한 상호작용 정의를 등록한다.</summary>
    public void InitData()
    {
        #region Creature Data
        VillagerDataDic = LoadJson<VillagerDataLoader, int, VillagerData>(VillagerDataKey).MakeDict();
        MonsterDataDic = LoadJson<MonsterDataLoader, int, MonsterData>(MonsterDataKey).MakeDict();
        NpcDataDic = LoadJson<NPCDataLoader, int, NPCData>(NpcDataKey).MakeDict();

        foreach (var pair in VillagerDataDic)
            CreatureDataDic[pair.Key] = pair.Value;
        foreach (var pair in MonsterDataDic)
            CreatureDataDic[pair.Key] = pair.Value;
        foreach (var pair in NpcDataDic)
            CreatureDataDic[pair.Key] = pair.Value;
        #endregion

        // Game State
        TextAsset stateCoreJson = Managers.Resource.Load<TextAsset>("state_core");
        Managers.GameState.LoadDefinitions(new[] { stateCoreJson.text /* 여러 파일 가능 */ });

        // Interaction
        TextAsset textAsset_1 = Managers.Resource.Load<TextAsset>("villager_temp");
        Managers.Interaction.LoadSet(textAsset_1.text);

        TextAsset textAsset_2 = Managers.Resource.Load<TextAsset>("dev_possess_villager");
        Managers.Interaction.LoadSet(textAsset_2.text);

        TextAsset textAsset_3 = Managers.Resource.Load<TextAsset>("villager_day");
        Managers.Interaction.LoadSet(textAsset_3.text);

        TextAsset textAsset_4 = Managers.Resource.Load<TextAsset>("merchant");
        Managers.Interaction.LoadSet(textAsset_4.text);

        TextAsset textAsset_5 = Managers.Resource.Load<TextAsset>("villager_mare");
        Managers.Interaction.LoadSet(textAsset_5.text);

        TextAsset textAsset_6 = Managers.Resource.Load<TextAsset>("villager_merchant_mare");
        Managers.Interaction.LoadSet(textAsset_6.text);

        TextAsset textAsset_7 = Managers.Resource.Load<TextAsset>("villager_nola");
        Managers.Interaction.LoadSet(textAsset_7.text);

        // boxSet은 카드 조립 전에는 비어 있으며 에디터에서 저장한 뒤 다음 실행에 로드되는 플레이어 Set이다.
        TextAsset boxSet = Managers.Resource.Load<TextAsset>(PlayerSokobanSetKey);
        if (boxSet != null)
            Managers.Interaction.LoadSet(boxSet.text);
        else
            LogPrinter.LogError($"[DataManager] 박스 밀기 Set 로드 실패: {PlayerSokobanSetKey}. PreLoad 등록을 확인하세요.");
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
