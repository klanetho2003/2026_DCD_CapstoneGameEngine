using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using static Define;

/// <summary>트리거 칸(TileExit면 방금 떠난 칸)에 아직 부서지지 않은 부서지는 발판이 있는지 검사한다.</summary>
[Serializable, InteractionNode("OnCrumbleTile", "부서지는 발판", "TileExit 전용 — 방금 떠난 칸에 아직 부서지지 않은 부서지는 발판이 있음")]
public sealed class OnCrumbleTileCondition : InteractionCondition, ITriggerCellNode
{
    /// <summary>발판 상태는 맵에서 읽고 공유 조건에는 저장하지 않는다.</summary>
    /// <param name="ctx">떠난 칸(Cell)을 가진 TileExit 평가 정보다.</param>
    public override bool Evaluate(in InteractionContext ctx)
    {
        return ctx.HasCell && Managers.Map.TryGetCrumbleTile(ctx.Cell, out GridCrumbleTile tile) && !tile.IsBroken;
    }
}

/// <summary>트리거 칸 발판이 밟힌 횟수를 비교한다. 발판이 없거나 이미 부서졌으면 거짓이다.</summary>
[Serializable, InteractionNode("CrumbleTileSteps", "발판 밟은 횟수", "TileExit 전용 — 방금 떠난 칸 발판의 밟은 횟수를 Value와 비교 (발판이 없거나 부서졌으면 거짓)")]
public sealed class CrumbleTileStepsCondition : InteractionCondition, ITriggerCellNode
{
    [JsonProperty("op"), JsonConverter(typeof(StringEnumConverter))]
    public EComparison Op = EComparison.GreaterOrEqual;

    [JsonProperty("value")]
    public int Value = 3;

    /// <summary>발판의 밟은 횟수를 정수 비교한다.</summary>
    /// <param name="ctx">떠난 칸(Cell)을 가진 TileExit 평가 정보다.</param>
    public override bool Evaluate(in InteractionContext ctx)
    {
        return ctx.HasCell && Managers.Map.TryGetCrumbleTile(ctx.Cell, out GridCrumbleTile tile) && !tile.IsBroken &&
            Util.Evaluate(tile.StepCount, Op, Value);
    }
}

/// <summary>트리거 칸 발판을 한 번 밟는다 — 밟은 횟수 +1과 그 횟수의 그림 교체.</summary>
[Serializable, InteractionNode("StepCrumbleTile", "발판 밟기", "TileExit 전용 — 방금 떠난 칸 발판의 밟은 횟수를 1 올리고 그 횟수의 그림으로 바꾼다")]
public sealed class StepCrumbleTileEffect : InteractionEffect, ITriggerCellNode
{
    /// <summary>발판에 위임하고 효과에는 상태를 두지 않는다.</summary>
    /// <param name="ctx">떠난 칸(Cell)을 가진 TileExit 평가 정보다.</param>
    public override void OnActivate(in InteractionContext ctx)
    {
        if (ctx.HasCell && Managers.Map.TryGetCrumbleTile(ctx.Cell, out GridCrumbleTile tile))
            tile.Step();
    }
}

/// <summary>트리거 칸 발판을 부순다 — 그 칸을 이번 라운드의 타일 데이터에서 빼고 점령도 지운다.</summary>
[Serializable, InteractionNode("BreakCrumbleTile", "발판 부수기", "TileExit 전용 — 방금 떠난 칸 발판을 부순다. 그 칸은 이번 라운드 동안 조종 캐릭터 · 몬스터 모두 밟을 수 없고 점령도 지워진다. Break 애니메이션이 있으면 재생 뒤, 없으면 바로 사라진다")]
public sealed class BreakCrumbleTileEffect : InteractionEffect, ITriggerCellNode
{
    /// <summary>발판에 위임하고 효과에는 상태를 두지 않는다.</summary>
    /// <param name="ctx">떠난 칸(Cell)을 가진 TileExit 평가 정보다.</param>
    public override void OnActivate(in InteractionContext ctx)
    {
        if (ctx.HasCell && Managers.Map.TryGetCrumbleTile(ctx.Cell, out GridCrumbleTile tile))
            tile.Break();
    }
}
