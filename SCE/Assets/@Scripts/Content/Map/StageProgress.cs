using System;
using System.Collections.Generic;

/// <summary>
/// 스테이지 1개의 진행도.
/// </summary>
[Serializable]
public class StageRecord
{
    public bool Cleared;

    /// <summary>진행 중이던(= 다음 진입 시 시작할) 웨이브 번호. 재진입 시 이 웨이브를 처음부터 다시 스폰한다.</summary>
    public int WaveIndex;
}

/// <summary>
/// 현재 맵의 스테이지 진행도. (Dictionary int key는 문자열로 기록 후 복원됨).
/// Stage Key는 맵 안에서만 유일하다 - 맵이 여러 개가 되면 맵 단위로 이 객체를 분리해야 한다.
/// </summary>
[Serializable]
public class StageProgress
{
    /// <summary>이 진행도가 속한 맵. Stage Key는 맵 안에서만 유일하다.</summary>
    public string MapName;

    public bool HasCheckpoint;
    public int CheckpointStageKey;

    public Dictionary<int, StageRecord> Stages = new();

    public void EnsureMap(string mapName)
    {
        if (MapName == mapName)
            return;

        Clear();
        MapName = mapName;
    }

    public StageRecord GetOrCreate(int stageKey)
    {
        if (Stages.TryGetValue(stageKey, out StageRecord record) == false)
        {
            record = new StageRecord();
            Stages.Add(stageKey, record);
        }
        return record;
    }

    public void SetCheckpoint(int stageKey)
    {
        HasCheckpoint = true;
        CheckpointStageKey = stageKey;
    }

    public void Clear()
    {
        HasCheckpoint = false;
        CheckpointStageKey = 0;
        Stages.Clear();
    }
}