using System;
using System.Collections.Generic;

/// <summary>
/// 게임 상태(플래그, 카운터, 일차) 구역이며 진행 파일에 들어간다. 모든 상태 키의 값을 키 이름으로 저장하고,
/// 불러올 때는 기본값으로 돌린 뒤 저장된 키만 입힌다. 정의에서 지운 키는 건너뛰고 새로 생긴 키는 기본값이다.
/// </summary>
public sealed class GameStateSaveSection : SaveSection<GameStateSaveData>
{
    public override string Key => "gameState";
    public override ESaveFile SaveFile => ESaveFile.Progress;
    public override int Version => 1;

    public override void ResetToDefault()
    {
        GameStateManager state = Managers.GameState;
        if (state.IsLoaded)
            state.ResetAllToDefault();
    }

    protected override GameStateSaveData CaptureData()
    {
        GameStateManager state = Managers.GameState;
        if (state.IsLoaded == false)
            throw new InvalidOperationException("상태 정의를 읽기 전이다");

        var data = new GameStateSaveData();
        for (int i = 0; i < state.KeyCount; i++)
        {
            // 정의가 없는 칸은 저장 대상이 아니다.
            string key = state.GetKeyAt(i);
            if (key != null)
                data.Values[key] = state.GetValueAt(i);
        }
        return data;
    }

    protected override void RestoreData(GameStateSaveData data)
    {
        GameStateManager state = Managers.GameState;
        if (state.IsLoaded == false)
            throw new InvalidOperationException("상태 정의를 읽기 전이다 — DataManager.InitData 뒤에 진행 파일을 불러와야 한다");

        state.ResetAllToDefault();
        // 정의에서 지워져 입히지 못한 키의 수다.
        int skipped = 0;
        foreach (KeyValuePair<string, int> pair in data.Values)
        {
            if (state.TrySetByKey(pair.Key, pair.Value) == false)
                skipped++;
        }
        if (skipped > 0)
            LogPrinter.LogWarning($"[GameStateSaveSection] 정의에 없는 키 {skipped}개는 건너뜀");
    }
}
