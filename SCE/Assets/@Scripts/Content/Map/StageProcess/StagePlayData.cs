using System.Collections.Generic;
using Data;
using static Define;

/// <summary>
/// Stage 한 판의 실행용 데이터. Stage 진입 시 MapData(JSON)에서 1회 변환한다.
/// StageManager와 RoundRunner는 MapData를 직접 읽지 않고 이 객체만 본다.
///
/// 변환 규칙 (라운드가 어떤 데이터를 쓰는지 정하는 유일한 지점 = TryBuild):
/// - 맵의 첫 구역(가장 작은 Key)을 Stage의 구역으로 쓴다
/// - 구역의 Rounds를 Round 값으로 A·B·C 자리에 넣는다. 셋이 정확히 하나씩 있어야 한다
/// - 상시 오브젝트 = MapObjects + 구역의 AlwaysSpawn. Stage 수명 동안 존속한다
/// </summary>
public sealed class StagePlayData
{
    private readonly List<SpawnInfo> _persistent = new();
    private readonly StageSpawnData[] _rounds = new StageSpawnData[ROUND_COUNT]; // 인덱스 = (int)ERound

    public string MapName { get; private set; }
    public int ZoneKey { get; private set; }
    public StageSettingsData Settings { get; private set; }
    public SpawnInfo PlayerSpawn { get; private set; }

    /// <summary>Stage 수명 동안 존속하는 오브젝트.</summary>
    public IReadOnlyList<SpawnInfo> Persistent { get { return _persistent; } }

    /// <summary>그 라운드의 웨이브 목록. WaveRunner가 그대로 소비하도록 기존 StageSpawnData 형식을 쓴다.</summary>
    public StageSpawnData GetRound(ERound round)
    {
        return _rounds[(int)round];
    }

    private StagePlayData() { } // TryBuild로만 만든다

    /// <summary>MapData → StagePlayData. 실패하면 false와 사유. Managers에 의존하지 않는다 — EditMode 테스트 대상.</summary>
    public static bool TryBuild(MapData map, out StagePlayData data, out string error)
    {
        data = null;

        if (map == null)
        {
            error = "MapData가 null";
            return false;
        }
        if (map.Stages == null || map.Stages.Count == 0 || map.Stages[0] == null)
        {
            error = $"'{map.Name}': 구역(StageAuthoring)이 없음";
            return false;
        }

        StageData zone = map.Stages[0]; // Exporter가 Key 오름차순으로 기록한다
        if (map.Stages.Count > 1)
            LogPrinter.LogWarning($"[StagePlayData] '{map.Name}': 구역 {map.Stages.Count}개 — Key {zone.Key}만 사용");

        if (zone.HasPlayerSpawn == false)
        {
            error = $"'{map.Name}': SpawnPoint가 없음 — 플레이어를 스폰할 수 없음";
            return false;
        }
        if (zone.Rounds == null || zone.Rounds.Count == 0)
        {
            error = $"'{map.Name}': 라운드 데이터가 없음 — Stage Prefab에 RoundAuthoring을 배치하고 재Export";
            return false;
        }

        var result = new StagePlayData
        {
            MapName = map.Name,
            ZoneKey = zone.Key,
            Settings = zone.Settings ?? new StageSettingsData(), // JSON에 "Settings": null이 명시된 경우 방어
            PlayerSpawn = zone.PlayerSpawnInfo,
        };

        // JSON에 적힌 순서가 아니라 Round 값으로 자리를 찾는다
        for (int i = 0; i < zone.Rounds.Count; i++)
        {
            RoundData round = zone.Rounds[i];
            if (round == null)
            {
                error = $"'{map.Name}': Rounds[{i}]가 비어 있음";
                return false;
            }

            int index = (int)round.Round;
            if ((uint)index >= (uint)ROUND_COUNT)
            {
                error = $"'{map.Name}': Round 값 {index}이(가) 범위 밖";
                return false;
            }
            if (result._rounds[index] != null)
            {
                error = $"'{map.Name}': Round {round.Round} 중복";
                return false;
            }
            result._rounds[index] = ConvertRound(round);
        }

        for (int i = 0; i < ROUND_COUNT; i++)
        {
            if (result._rounds[i] == null)
            {
                error = $"'{map.Name}': Round {(ERound)i} 없음";
                return false;
            }
        }

        AppendSpawns(result._persistent, map.MapObjects);
        AppendSpawns(result._persistent, zone.AlwaysSpawn);

        data = result;
        error = null;
        return true;
    }

    private static StageSpawnData ConvertRound(RoundData round)
    {
        var result = new StageSpawnData();
        if (round.Waves == null)
            return result;

        for (int w = 0; w < round.Waves.Count; w++)
        {
            List<SpawnData> source = round.Waves[w];
            var wave = new List<SpawnInfo>(source != null ? source.Count : 0);
            AppendSpawns(wave, source);
            result.Waves.Add(wave); // 빈 웨이브도 자리를 유지한다 — WaveRunner가 건너뛴다
        }
        return result;
    }

    private static void AppendSpawns(List<SpawnInfo> into, List<SpawnData> source)
    {
        if (source == null)
            return;
        for (int i = 0; i < source.Count; i++)
            into.Add(SpawnInfo.From(source[i]));
    }
}