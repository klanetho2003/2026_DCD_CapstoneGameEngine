using System.Collections.Generic;

/// <summary>Stage 하나의 기록이다. 점령 타일 수는 정산한 라운드의 합이다 (클리어한 판은 3라운드, 실패한 판은 끝까지 정산한 라운드만).</summary>
public readonly struct StageRecord
{
    /// <summary>이 기록의 판이 Stage를 클리어했는지다.</summary>
    public bool Cleared { get; }
    /// <summary>이 기록의 판에서 플레이어와 몬스터가 점령한 총 타일 수다.</summary>
    public TerritoryScore Score { get; }

    /// <param name="cleared">이 판이 Stage를 클리어했는지다.</param>
    /// <param name="score">이 판에서 정산한 라운드의 점령 타일 합이다.</param>
    public StageRecord(bool cleared, TerritoryScore score)
    {
        Cleared = cleared;
        Score = score;
    }

    /// <summary>other보다 좋은 판인지다. 클리어한 판이 먼저이고 클리어 여부가 같으면 플레이어 타일이 많은 판이며, 똑같으면 좋지 않다.</summary>
    /// <param name="other">비교할 기존 기록이다.</param>
    public bool IsBetterThan(StageRecord other)
    {
        if (Cleared != other.Cleared)
            return Cleared;
        return Score.PlayerTiles > other.Score.PlayerTiles;
    }
}

/// <summary>
/// Stage마다(StageId) 최고 기록을 갖는다. 판이 끝나면 StageManager가 결과를 넣고, 진행 파일(StageRecordSaveSection)이 이 표를 저장하고 불러온다.
/// 넣은 판이 기존 기록보다 좋을 때만 바꾼다 — 한 번 클리어하면 클리어로 남고 타일 수는 그 기록의 판 값이다. 조회 · 넣기 O(1).
/// </summary>
public sealed class StageRecordBook
{
    private readonly Dictionary<int, StageRecord> _records = new Dictionary<int, StageRecord>();

    /// <summary>기록이 있는 Stage의 기록이며 차례는 정해지지 않는다.</summary>
    public IReadOnlyDictionary<int, StageRecord> Entries => _records;

    /// <summary>Stage의 기록을 찾는다. 한 번도 끝낸 적 없으면 false다.</summary>
    /// <param name="stageId">찾을 Stage의 StageId다.</param>
    /// <param name="record">찾은 기록이다.</param>
    public bool TryGet(int stageId, out StageRecord record)
    {
        return _records.TryGetValue(stageId, out record);
    }

    /// <summary>한 판의 결과를 넣는다. 기록이 없거나 기존 기록보다 좋으면 바꾸고 true다.</summary>
    /// <param name="stageId">끝난 Stage의 StageId다.</param>
    /// <param name="record">끝난 판의 결과다.</param>
    public bool Report(int stageId, StageRecord record)
    {
        if (_records.TryGetValue(stageId, out StageRecord current) && record.IsBetterThan(current) == false)
            return false;
        _records[stageId] = record;
        return true;
    }

    /// <summary>불러온 기록을 비교 없이 넣는다.</summary>
    /// <param name="stageId">기록의 StageId다.</param>
    /// <param name="record">불러온 기록이다.</param>
    public void Set(int stageId, StageRecord record)
    {
        _records[stageId] = record;
    }

    public void Clear()
    {
        _records.Clear();
    }
}
