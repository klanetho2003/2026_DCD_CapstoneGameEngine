using System;
using System.Collections.Generic;
using System.IO;
using Data;
using Newtonsoft.Json;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using static Define;

/// <summary>
/// 게임 데이터 표를 게임과 같은 Addressable 주소(DataManager.*DataKey)와 로더로 읽는다 — 행동 트리 편집기의 쓰는 몬스터 목록이 쓴다.
/// 문제는 문장으로 모아 반환한다. 파일 IO를 하는 얇은 층이다.
/// </summary>
public static class EditorGameData
{
    /// <summary>크리처 표의 주소 — 게임(DataManager.InitData)이 읽는 차례 그대로</summary>
    private static readonly string[] s_dataKeys = { DataManager.VillagerDataKey, DataManager.MonsterDataKey, DataManager.NpcDataKey };

    #region Addressable
    /// <summary>
    /// 주소 → Addressable 항목. Addressable 설정이 없으면 null. 폴더째 등록한 항목 안의 에셋까지 본다.
    /// 같은 주소가 둘이면 먼저 나온 쪽 — ResourceManager도 하나만 보관한다. O(등록된 에셋 수)
    /// </summary>
    public static Dictionary<string, AddressableAssetEntry> BuildAddressIndex()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            return null;

        var entries = new List<AddressableAssetEntry>();
        settings.GetAllAssets(entries, false);
        var index = new Dictionary<string, AddressableAssetEntry>(entries.Count, StringComparer.Ordinal);
        for (int i = 0; i < entries.Count; i++)
            index.TryAdd(entries[i].address, entries[i]);
        return index;
    }
    #endregion

    #region 데이터 표
    /// <summary>
    /// 크리처 세 표 — 게임(DataManager.InitData)과 같은 주소 · 로더. 게임은 Villager → Monster → NPC 차례로 CreatureDataDic을 덮어써 같은 Id면 뒤 표가 이기므로,
    /// 거꾸로 넣어 먼저 넣은 행이 이기게 합친다. 하나라도 읽지 못하면 null — 근거가 불완전하면 "쓰는 몬스터 없음"이라고 단정하지 않는다. 모든 표를 읽어 문제를 한 번에 모은다.
    /// </summary>
    public static MergedCreatureTable ReadCreatureTable(Dictionary<string, AddressableAssetEntry> addresses, List<string> problems)
    {
        var table = new MergedCreatureTable();
        bool ok = true;
        ok &= ReadCreatureRows<NPCDataLoader>(DataManager.NpcDataKey, EObjectType.NPC, loader => loader.Datas, addresses, table, problems);
        ok &= ReadCreatureRows<MonsterDataLoader>(DataManager.MonsterDataKey, EObjectType.Monster, loader => loader.Datas, addresses, table, problems);
        ok &= ReadCreatureRows<VillagerDataLoader>(DataManager.VillagerDataKey, EObjectType.Villager, loader => loader.Datas, addresses, table, problems);
        return ok ? table : null;
    }

    /// <summary>표 하나를 읽어 행을 합친다 — 빈 행은 건너뛴다. 같은 표 안의 중복과 다른 표와의 중복은 게임에서 일어나는 일과 함께 problems에 남긴다.</summary>
    private static bool ReadCreatureRows<TLoader>(string address, EObjectType kind, Func<TLoader, IEnumerable<CreatureData>> rowsOf,
        Dictionary<string, AddressableAssetEntry> addresses, MergedCreatureTable into, List<string> problems)
    {
        if (addresses == null || addresses.TryGetValue(address, out AddressableAssetEntry entry) == false)
        {
            problems.Add($"Addressable 주소 '{address}' 없음 — 크리처 데이터를 읽지 못함");
            return false;
        }

        TLoader loader;
        try
        {
            loader = JsonConvert.DeserializeObject<TLoader>(File.ReadAllText(entry.AssetPath));
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException)
        {
            problems.Add($"'{entry.AssetPath}' 읽기 실패 — {e.Message}");
            return false;
        }

        IEnumerable<CreatureData> rows = loader != null ? rowsOf(loader) : null;
        if (rows == null)
            return true;
        foreach (CreatureData row in rows)
        {
            if (row == null || into.TryAdd(row, kind, out EObjectType existing))
                continue;
            problems.Add(existing == kind
                ? $"{address}: templateID {row.templateID} 중복 — 게임은 이 표를 읽지 못함 (MakeDict)"
                : $"{address}: templateID {row.templateID} — {existing} 데이터에도 있음. 게임은 {existing} 표의 행을 씀 (뒤 표가 덮어씀)");
        }
        return true;
    }
    #endregion

    #region 바뀌었는지 보기
    /// <summary>데이터 표 파일 경로 (주소가 등록된 것만). 다시 읽을지 정하는 서명(EditorJsonFolder.FilesSignature)에 쓴다 — 주소 등록이 바뀐 것은 서명에 잡히지 않는다.</summary>
    public static void CollectDataFiles(Dictionary<string, AddressableAssetEntry> addresses, List<string> into)
    {
        into.Clear();
        if (addresses == null)
            return;

        for (int i = 0; i < s_dataKeys.Length; i++)
        {
            if (addresses.TryGetValue(s_dataKeys[i], out AddressableAssetEntry entry))
                into.Add(entry.AssetPath);
        }
    }
    #endregion
}
