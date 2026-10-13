using System.Collections.Generic;
using Data;
using Newtonsoft.Json;
using UnityEngine;
using static Define;

/// <summary>스폰 1건의 런타임 형태. SpawnData(JSON) 또는 StageData.PlayerSpawnInfo에서 온다.</summary>
public readonly struct SpawnInfo
{
    public readonly EObjectType ObjectType;
    public readonly int DataId;
    public readonly Vector2Int Cell;

    /// <summary>
    /// Newtonsoft는 생성자 매개변수를 JSON 속성과 이름으로 맞춘다 (대소문자 무시).
    /// 매개변수 이름이 필드 이름과 다르면 그 값은 기본값으로 남고, 필드가 readonly라 생성 뒤에도 채워지지 않는다.
    /// 그래서 매개변수 이름 = 필드 이름(소문자 시작)을 지킨다.
    /// </summary>
    [JsonConstructor]
    public SpawnInfo(EObjectType objectType, int dataId, Vector2Int cell)
    {
        ObjectType = objectType;
        DataId = dataId;
        Cell = cell;
    }

    public static SpawnInfo From(SpawnData s)
    {
        return new SpawnInfo(s.ObjectType, s.DataId, new Vector2Int(s.CellX, s.CellY));
    }
}

/// <summary>라운드 1개의 웨이브 목록. WaveRunner가 그대로 소비한다.</summary>
public class StageSpawnData
{
    /// <summary>Waves[i] = i번 웨이브의 몬스터 스폰 목록.</summary>
    public readonly List<List<SpawnInfo>> Waves = new();
}