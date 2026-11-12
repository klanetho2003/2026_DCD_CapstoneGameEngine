using System.Collections.Generic;

/// <summary>
/// 클리어한 Stage 기록 (행성 해금용).
/// 지금은 게임을 켜 둔 동안만 유지된다 — 저장 파일 연결은 아직 안 함.
/// 규칙: 첫 Stage는 처음부터 열림, 그 다음 Stage는 바로 앞 Stage를 클리어하면 열림.
/// </summary>
public static class PlanetProgress
{
    private static readonly HashSet<int> s_clearedStageIds = new HashSet<int>();

    public static void MarkCleared(int stageId)
    {
        s_clearedStageIds.Add(stageId);
    }

    public static bool IsCleared(int stageId)
    {
        return s_clearedStageIds.Contains(stageId);
    }
}
