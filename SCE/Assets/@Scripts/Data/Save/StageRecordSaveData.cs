using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine.Scripting;

/// <summary>
/// Stage 기록 구역의 저장 형식이며 진행 파일에 들어간다. 값은 StageRecordSaveSection이 적고 받아 쓴다 — 이 형식은 값만 갖는다.
/// 예: { "stages": [ { "stageId": 1, "cleared": true, "playerTiles": 42, "monsterTiles": 18 } ] }
/// </summary>
[Serializable]
[Preserve] // JSON 읽기만 만드는 형식 — 빌드의 코드 줄이기에서 생성자가 지워지지 않게
[JsonObject(MemberSerialization.OptIn)]
public sealed class StageRecordSaveData
{
    /// <summary>Stage마다 하나이며 StageId 차례다.</summary>
    [JsonProperty("stages")]
    public List<StageRecordSaveEntry> Stages = new List<StageRecordSaveEntry>();
}

/// <summary>Stage 하나의 기록이다.</summary>
[Serializable]
[Preserve]
[JsonObject(MemberSerialization.OptIn)]
public sealed class StageRecordSaveEntry
{
    /// <summary>StageCatalog의 StageId다.</summary>
    [JsonProperty("stageId")]
    public int StageId;

    /// <summary>Stage를 클리어했는지다.</summary>
    [JsonProperty("cleared")]
    public bool Cleared;

    /// <summary>플레이어가 점령한 총 타일 수다.</summary>
    [JsonProperty("playerTiles")]
    public int PlayerTiles;

    /// <summary>몬스터가 점령한 총 타일 수다.</summary>
    [JsonProperty("monsterTiles")]
    public int MonsterTiles;
}
