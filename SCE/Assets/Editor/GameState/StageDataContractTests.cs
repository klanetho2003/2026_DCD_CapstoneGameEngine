using System.Collections.Generic;
using Data;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;
using static Define;

public class StagePlayDataTests
{
    #region 테스트 데이터
    private static SpawnData MakeSpawn(EObjectType type, int dataId, int x, int y)
    {
        return new SpawnData { ObjectType = type, DataId = dataId, CellX = x, CellY = y };
    }

    /// <param name="waveSizes">웨이브마다 넣을 몬스터 수. 개수가 곧 웨이브 수</param>
    private static RoundData MakeRound(ERound round, params int[] waveSizes)
    {
        var data = new RoundData { Round = round };
        for (int w = 0; w < waveSizes.Length; w++)
        {
            var wave = new List<SpawnData>();
            for (int i = 0; i < waveSizes[w]; i++)
                wave.Add(MakeSpawn(EObjectType.Monster, 200 + i, i, w));
            data.Waves.Add(wave);
        }
        return data;
    }

    private static StageData MakeZone(params RoundData[] rounds)
    {
        var zone = new StageData
        {
            Key = 0,
            Settings = new StageSettingsData(),
            HasPlayerSpawn = true,
            PlayerSpawnInfo = new SpawnInfo(EObjectType.Villager, 100, new Vector2Int(3, 4)),
        };
        zone.AlwaysSpawn.Add(MakeSpawn(EObjectType.NPC, 300, 1, 1));
        for (int i = 0; i < rounds.Length; i++)
            zone.Rounds.Add(rounds[i]);
        return zone;
    }

    private static MapData MakeMap(StageData zone)
    {
        return new MapData
        {
            Name = "TestStage",
            Stages = new List<StageData> { zone },
            MapObjects = new List<SpawnData> { MakeSpawn(EObjectType.NPC, 301, 2, 2) },
        };
    }

    private static StageData MakeValidZone()
    {
        return MakeZone(MakeRound(ERound.A, 2, 1), MakeRound(ERound.B, 3), MakeRound(ERound.C, 1));
    }
    #endregion

    [Test]
    public void RoundCount_MatchesEnumMemberCount()
    {
        Assert.AreEqual(System.Enum.GetValues(typeof(ERound)).Length, ROUND_COUNT);
    }

    [Test]
    public void TryBuild_ValidMap_ConvertsRoundsAndPersistent()
    {
        bool ok = StagePlayData.TryBuild(MakeMap(MakeValidZone()), out StagePlayData data, out string error);

        Assert.IsTrue(ok, error);
        Assert.AreEqual(2, data.GetRound(ERound.A).Waves.Count);
        Assert.AreEqual(2, data.GetRound(ERound.A).Waves[0].Count);
        Assert.AreEqual(1, data.GetRound(ERound.A).Waves[1].Count);
        Assert.AreEqual(3, data.GetRound(ERound.B).Waves[0].Count);
        Assert.AreEqual(1, data.GetRound(ERound.C).Waves[0].Count);

        Assert.AreEqual(100, data.PlayerSpawn.DataId);
        Assert.AreEqual(new Vector2Int(3, 4), data.PlayerSpawn.Cell);
        Assert.AreEqual(2, data.Persistent.Count); // MapObjects 1 + AlwaysSpawn 1
    }

    [Test]
    public void TryBuild_RoundsOutOfOrder_PlacedByRoundValue()
    {
        StageData zone = MakeZone(MakeRound(ERound.C, 1, 1, 1), MakeRound(ERound.A, 1), MakeRound(ERound.B, 1, 1));

        Assert.IsTrue(StagePlayData.TryBuild(MakeMap(zone), out StagePlayData data, out string error), error);
        Assert.AreEqual(1, data.GetRound(ERound.A).Waves.Count);
        Assert.AreEqual(2, data.GetRound(ERound.B).Waves.Count);
        Assert.AreEqual(3, data.GetRound(ERound.C).Waves.Count);
    }

    [Test]
    public void TryBuild_NoSpawnPoint_Fails()
    {
        StageData zone = MakeValidZone();
        zone.HasPlayerSpawn = false;

        Assert.IsFalse(StagePlayData.TryBuild(MakeMap(zone), out StagePlayData data, out string error));
        Assert.IsNull(data);
        Assert.IsNotNull(error);
    }

    [Test]
    public void TryBuild_NoRounds_Fails()
    {
        Assert.IsFalse(StagePlayData.TryBuild(MakeMap(MakeZone()), out StagePlayData data, out string error));
        Assert.IsNull(data);
        Assert.IsNotNull(error);
    }

    [Test]
    public void TryBuild_MissingRound_Fails()
    {
        StageData zone = MakeZone(MakeRound(ERound.A, 1), MakeRound(ERound.B, 1));

        Assert.IsFalse(StagePlayData.TryBuild(MakeMap(zone), out _, out string error));
        StringAssert.Contains("C", error);
    }

    [Test]
    public void TryBuild_DuplicateRound_Fails()
    {
        StageData zone = MakeZone(MakeRound(ERound.A, 1), MakeRound(ERound.A, 1), MakeRound(ERound.C, 1));

        Assert.IsFalse(StagePlayData.TryBuild(MakeMap(zone), out _, out string error));
        StringAssert.Contains("중복", error);
    }

    [Test]
    public void TryBuild_FromExportedJsonShape_ReadsRoundNames()
    {
        // Exporter는 enum을 문자열로 기록하고, 런타임은 StringEnumConverter 없이 읽는다 — 그 경로를 그대로 확인
        const string json = @"{
            'Name': 'JsonStage',
            'Stages': [ {
                'Key': 0,
                'HasPlayerSpawn': true,
                'PlayerSpawnInfo': { 'ObjectType': 'Villager', 'DataId': 100, 'Cell': { 'x': 3, 'y': 4 } },
                'AlwaysSpawn': [],
                'Waves': [],
                'Rounds': [
                    { 'Round': 'A', 'Waves': [ [ { 'ObjectType': 'Monster', 'DataId': 201, 'CellX': 5, 'CellY': 7 } ] ] },
                    { 'Round': 'B', 'Waves': [ [] ] },
                    { 'Round': 'C', 'Waves': [] }
                ]
            } ]
        }";

        MapData map = JsonConvert.DeserializeObject<MapData>(json);
        Assert.IsTrue(StagePlayData.TryBuild(map, out StagePlayData data, out string error), error);

        SpawnInfo first = data.GetRound(ERound.A).Waves[0][0];
        Assert.AreEqual(EObjectType.Monster, first.ObjectType);
        Assert.AreEqual(201, first.DataId);
        Assert.AreEqual(new Vector2Int(5, 7), first.Cell);

        Assert.AreEqual(1, data.GetRound(ERound.B).Waves.Count);
        Assert.AreEqual(0, data.GetRound(ERound.C).Waves.Count);
        Assert.AreEqual(100, data.PlayerSpawn.DataId);
        Assert.AreEqual(new Vector2Int(3, 4), data.PlayerSpawn.Cell);
    }
}

public class StageCatalogTests
{
    private readonly List<StageInfoData> _stages = new();
    private readonly List<string> _errors = new();

    [SetUp]
    public void SetUp()
    {
        _stages.Clear();
        _errors.Clear();
    }

    [Test]
    public void TryParse_ThreeStages_KeepsJsonOrder()
    {
        const string json = @"{ 'Stages': [
            { 'StageId': 1, 'DisplayName': 'A', 'MapPrefabKey': 'P1', 'MapDataKey': 'D1' },
            { 'StageId': 2, 'DisplayName': 'B', 'MapPrefabKey': 'P2', 'MapDataKey': 'D2' },
            { 'StageId': 3, 'DisplayName': 'C', 'MapPrefabKey': 'P3', 'MapDataKey': 'D3' } ] }";

        Assert.IsTrue(StageCatalog.TryParse(json, _stages, _errors));
        Assert.AreEqual(3, _stages.Count);
        Assert.AreEqual(0, _errors.Count);
        Assert.AreEqual(1, _stages[0].StageId);
        Assert.AreEqual("P3", _stages[2].MapPrefabKey);
    }

    [Test]
    public void TryParse_DuplicateStageId_KeepsFirst()
    {
        const string json = @"{ 'Stages': [
            { 'StageId': 1, 'DisplayName': 'First', 'MapPrefabKey': 'P1', 'MapDataKey': 'D1' },
            { 'StageId': 1, 'DisplayName': 'Second', 'MapPrefabKey': 'P2', 'MapDataKey': 'D2' } ] }";

        Assert.IsTrue(StageCatalog.TryParse(json, _stages, _errors));
        Assert.AreEqual(1, _stages.Count);
        Assert.AreEqual("First", _stages[0].DisplayName);
        Assert.AreEqual(1, _errors.Count);
    }

    [Test]
    public void TryParse_EmptyKey_SkipsRow()
    {
        const string json = @"{ 'Stages': [
            { 'StageId': 1, 'DisplayName': 'NoPrefab', 'MapPrefabKey': '', 'MapDataKey': 'D1' },
            { 'StageId': 2, 'DisplayName': 'Ok', 'MapPrefabKey': 'P2', 'MapDataKey': 'D2' } ] }";

        Assert.IsTrue(StageCatalog.TryParse(json, _stages, _errors));
        Assert.AreEqual(1, _stages.Count);
        Assert.AreEqual(2, _stages[0].StageId);
        Assert.AreEqual(1, _errors.Count);
    }

    [Test]
    public void TryParse_EmptyDisplayName_FallsBackToMapDataKey()
    {
        const string json = @"{ 'Stages': [ { 'StageId': 1, 'MapPrefabKey': 'P1', 'MapDataKey': 'D1' } ] }";

        Assert.IsTrue(StageCatalog.TryParse(json, _stages, _errors));
        Assert.AreEqual("D1", _stages[0].DisplayName);
    }

    [Test]
    public void TryParse_Malformed_Fails()
    {
        Assert.IsFalse(StageCatalog.TryParse("{ 'Stages': [ ", _stages, _errors));
        Assert.AreEqual(0, _stages.Count);
        Assert.GreaterOrEqual(_errors.Count, 1);
    }

    [Test]
    public void TryParse_NoValidStage_Fails()
    {
        Assert.IsFalse(StageCatalog.TryParse("{ 'Stages': [] }", _stages, _errors));
        Assert.GreaterOrEqual(_errors.Count, 1);
    }
}