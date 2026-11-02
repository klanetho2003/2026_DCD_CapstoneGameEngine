/// <summary>
/// 칸 이동 몸 — 칸 이동 노드가 실행기의 몸 (runner.Agent)에 묻는 창구. 게임은 MonsterBase.
/// 몸이 이 창구가 아니면 칸 이동 Task는 실패하고 칸 이동 조건은 거짓이다.
/// </summary>
public interface IBehaviorTileAgent : IBehaviorAgent
{
    /// <summary>한 칸 출발 · 도착 결과 · 시선 · 통행 조회 (몬스터마다 하나 — 기존 이동 제어기와 같은 규칙)</summary>
    MonsterTileStepper Stepper { get; }

    /// <summary>도착 기록 · 세 바퀴 순환 · 탈출 예고 · BFS 이탈 · 봉쇄 대기 (몬스터마다 하나 — 기존 이동 제어기와 같은 규칙)</summary>
    MonsterEscapeTracker EscapeTracker { get; }
}
