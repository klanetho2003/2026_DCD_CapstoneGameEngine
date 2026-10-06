using Data;
using Newtonsoft.Json;
using System.Collections.Generic;
using UnityEngine;
using static Define;

public readonly struct SpawnInfo
{
    public readonly EObjectType ObjectType;
    public readonly int DataId;
    public readonly Vector2Int Cell;

    [JsonConstructor]
    public SpawnInfo(EObjectType type, int dataId, Vector2Int cell)
    {
        ObjectType = type;
        DataId = dataId;
        Cell = cell;
    }

    public static SpawnInfo From(Data.SpawnData s) // casting
    {
        return new SpawnInfo(s.ObjectType, s.DataId, new Vector2Int(s.CellX, s.CellY));
    }
}

public class StageSpawnData
{
    /// <summary>웨이브와 무관하게 존속. 진입마다 리스폰.</summary>
    public readonly List<SpawnInfo> AlwaysSpawn = new();

    /// <summary>Waves[i] = i번 웨이브의 몬스터 스폰 목록.</summary>
    public readonly List<List<SpawnInfo>> Waves = new();
}

/// <summary>
/// 한 스테이지의 런타임 — StageData(JSON) 소비.
///
/// 재진입 규칙:
/// - 진입할 때마다 AlwaysSpawn 리스폰
/// - 클리어한 스테이지: 웨이브 없음
/// - 미클리어 스테이지: 기록된 웨이브를 처음부터 다시 시작
///
/// 진행도의 원본은 StageProgress. 이 클래스는 StageRecord를 참조만 하고 복사하지 않는다.
/// </summary>
public class StageRunner
{
    private readonly StageData _data;
    private readonly StageSpawnData _spawnData; // AlwaysSpawn/Waves의 런타임 원본 (JSON에서 1회 변환)
    private readonly StageRecord _record;
    private readonly WaveRunner _waveRunner = new();
    private readonly List<CreatureBase> _persistentSpawns = new(); // NPC 포함 — CombatCreature로 제한하지 않는다
    private readonly List<CombatCreature> _waveSpawns = new();

    /// <summary>스테이지 설정. 저작 시 StageAuthoring에 입력한 값.</summary>
    public StageSettingsData Settings { get { return _data.Settings; } }
    public EZoneType ZoneType { get { return _data.Settings.ZoneType; } }

    public int StageKey { get; }
    public bool IsActive { get; private set; }

    /// <summary>진행도에서 파생. 저장 필드가 아니다.</summary>
    public bool AllWavesCleared { get { return _record.Cleared; } }
    public int SavedWaveIndex { get { return _record.WaveIndex; } }

    public bool HasPlayerSpawn { get { return _data.HasPlayerSpawn; } }
    public SpawnInfo PlayerSpawnInfo { get { return _data.PlayerSpawnInfo; } }

    private readonly string _logContext;  // "Stage {Key}" — 스폰 에러 로그용

    public StageRunner(int key, StageData data, StageRecord record)
    {
        data.Settings ??= new StageSettingsData(); // JSON에 "Settings": null이 명시된 경우만 해당

        StageKey = key;
        _data = data;
        _record = record;
        _logContext = $"Stage {key}";

        _spawnData = BuildRunnerData(data);

        // 메서드 그룹 delegate는 생성 시 1회만 만들어진다
        _waveRunner.SetInfo(_spawnData, SpawnWave, OnAllWavesCleared, OnWaveStarted);
    }

    #region Data 변환
    /// <summary>StageData(JSON) >> 런타임 StageSpawnData. 생성 시 1회.</summary>
    private static StageSpawnData BuildRunnerData(StageData data)
    {
        var result = new StageSpawnData();

        for (int i = 0; i < data.AlwaysSpawn.Count; i++)
            result.AlwaysSpawn.Add(SpawnInfo.From(data.AlwaysSpawn[i]));

        for (int w = 0; w < data.Waves.Count; w++)
        {
            var list = new List<SpawnInfo>(data.Waves[w].Count);
            for (int i = 0; i < data.Waves[w].Count; i++)
                list.Add(SpawnInfo.From(data.Waves[w][i]));
            result.Waves.Add(list);
        }
        return result;
    }
    #endregion

    #region 진행도 기록 (WaveRunner 콜백)
    private void OnWaveStarted(int waveIndex)
    {
        // 이 웨이브 도중에 나가면 재진입 시 이 웨이브를 처음부터
        _record.WaveIndex = waveIndex;
    }

    private void OnAllWavesCleared()
    {
        _record.Cleared = true;
        _record.WaveIndex = _spawnData.Waves.Count;
        LogPrinter.Log($"[Stage {StageKey}] 전 웨이브 클리어");
    }
    #endregion

    #region Load / Unload
    public void Load()
    {
        if (IsActive)
            return;
        IsActive = true;

        SpawnPersistent(); // 클리어 여부와 무관하게 매 진입마다

        if (_record.Cleared)
        {
            LogPrinter.Log($"[Stage {StageKey}] 클리어된 스테이지 진입 — 웨이브 없음");
            return;
        }

        LogPrinter.Log($"[Stage {StageKey}] 진입 — 웨이브 {_record.WaveIndex + 1}/{_spawnData.Waves.Count}부터");
        _waveRunner.Begin(_record.WaveIndex); // 웨이브 수 이상이면 WaveRunner가 클리어로 처리
    }

    public void Unload()
    {
        if (IsActive == false)
            return;
        IsActive = false;

        _waveRunner.Stop();
        CreatureSpawnHelper.DespawnAll(_waveSpawns);
        CreatureSpawnHelper.DespawnAll(_persistentSpawns);
    }
    #endregion

    #region Spawn
    private void SpawnPersistent()
    {
        _persistentSpawns.Clear();
        List<SpawnInfo> infos = _spawnData.AlwaysSpawn;
        for (int i = 0; i < infos.Count; i++)
        {
            CreatureBase creature = CreatureSpawnHelper.Spawn(infos[i], _logContext);
            if (creature != null)
                _persistentSpawns.Add(creature);
        }
    }

    /// <summary>WaveRunner가 호출. 이전 웨이브는 전멸 상태이므로 목록만 비운다.</summary>
    private IReadOnlyList<CombatCreature> SpawnWave(IReadOnlyList<SpawnInfo> infos)
    {
        _waveSpawns.Clear();
        for (int i = 0; i < infos.Count; i++)
        {
            SpawnInfo info = infos[i];
            if (info.ObjectType != EObjectType.Monster)
            {
                LogPrinter.LogError($"[{_logContext}] 웨이브에 {info.ObjectType}(DataId={info.DataId}) — Monster만 허용, 무시");
                continue;
            }

            if (CreatureSpawnHelper.Spawn(info, _logContext) is CombatCreature combat)
                _waveSpawns.Add(combat);
        }
        return _waveSpawns;
    }
    #endregion
}