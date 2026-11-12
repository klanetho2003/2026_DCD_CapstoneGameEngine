using System.Collections.Generic;

/// <summary>
/// Stage 기록 구역이며 진행 파일에 들어간다. Stage마다 클리어 여부와 플레이어 · 몬스터의 총 점령 타일 수만 저장한다.
/// 기록의 원본은 StageManager.Records이며 이 구역은 그 표와 저장 형식 사이만 옮긴다.
/// </summary>
public sealed class StageRecordSaveSection : SaveSection<StageRecordSaveData>
{
    public override string Key => "stageRecords";
    public override ESaveFile SaveFile => ESaveFile.Progress;
    public override int Version => 1;

    public override void ResetToDefault()
    {
        Managers.Stage.Records.Clear();
    }

    protected override StageRecordSaveData CaptureData()
    {
        var data = new StageRecordSaveData();
        foreach (KeyValuePair<int, StageRecord> pair in Managers.Stage.Records.Entries)
        {
            StageRecord record = pair.Value;
            data.Stages.Add(new StageRecordSaveEntry
            {
                StageId = pair.Key,
                Cleared = record.Cleared,
                PlayerTiles = record.Score.PlayerTiles,
                MonsterTiles = record.Score.MonsterTiles,
            });
        }
        // 같은 기록이면 같은 글이 되도록 StageId 차례로 쓴다
        data.Stages.Sort((a, b) => a.StageId.CompareTo(b.StageId));
        return data;
    }

    protected override void RestoreData(StageRecordSaveData data)
    {
        StageRecordBook book = Managers.Stage.Records;
        book.Clear();
        for (int i = 0; i < data.Stages.Count; i++)
        {
            StageRecordSaveEntry entry = data.Stages[i];
            if (entry == null)
                continue;
            book.Set(entry.StageId, new StageRecord(entry.Cleared, new TerritoryScore(entry.PlayerTiles, entry.MonsterTiles)));
        }
    }
}
