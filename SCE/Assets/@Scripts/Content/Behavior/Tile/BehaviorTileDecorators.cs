using System;

/// <summary>
/// 탈출 차례 — 몸의 탈출 추적기에서 예고가 끝났거나 탈출 · 봉쇄 대기 중이다 (MonsterEscapeTracker.IsEscapeDue). 몸이 칸 이동 몸이 아니면 거짓.
/// LowerPriority로 지켜보면 예고가 끝나는 평가에 순찰 가지를 끊는다 (진행 중인 한 칸은 도착까지 미룬다)
/// </summary>
[Serializable]
[BehaviorNode("TileEscapeDue", "탈출 차례", "탈출 예고가 끝났거나 탈출 · 봉쇄 대기 중이다", Category = "Tile")]
public sealed class BehaviorTileEscapeDueDecorator : BehaviorDecorator
{
    protected override bool CalculateRawConditionValue(BehaviorTreeRunner runner)
    {
        var agent = runner.Agent as IBehaviorTileAgent;
        return agent != null && agent.EscapeTracker.IsEscapeDue;
    }
}
