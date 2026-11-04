using System;
using static Define;

/// <summary>현재 라운드가 실제 플레이 중일 때만 상호작용을 허용하는 재사용 조건이다.</summary>
[Serializable, InteractionNode("StagePlaying", "플레이 중", "현재 스테이지가 Playing 상태일 때만 실행한다")]
public sealed class StagePlayingCondition : InteractionCondition
{
    /// <summary>결과 화면이나 선택 화면에서 틱 상호작용이 실행되지 않도록 상태를 조회한다.</summary>
    /// <param name="ctx">이번 상호작용의 소유자와 트리거 정보다.</param>
    public override bool Evaluate(in InteractionContext ctx)
    {
        return Managers.Stage.Phase == EStagePhase.Playing;
    }
}

/// <summary>소유 플레이어의 현재 방향 입력으로 앞 박스를 한 칸 밀 수 있는지 검사한다.</summary>
[Serializable, InteractionNode("CanPushBox", "박스 밀기 가능", "조작 중인 플레이어가 방향키를 누르고 있고 정면 박스의 다음 칸이 비어 있는지 검사한다")]
public sealed class CanPushBoxCondition : InteractionCondition
{
    /// <summary>공유 조건에 박스별 상태를 저장하지 않고 플레이어의 밀기 실행기에서 현재 상황을 읽는다.</summary>
    /// <param name="ctx">Owner가 밀기 인터랙션 Set을 가진 플레이어인 평가 정보다.</param>
    public override bool Evaluate(in InteractionContext ctx)
    {
        // player는 자기 소유 상호작용에서 Instigator가 null이어도 사용할 실제 소유 플레이어다.
        Villager player = ctx.Owner as Villager;
        return player != null && player.BoxPush != null && player.BoxPush.CanPush();
    }
}

/// <summary>박스와 플레이어의 동시 한 칸 이동을 전용 실행기에 한 번 요청한다.</summary>
[Serializable, InteractionNode("PushBox", "박스 한 칸 밀기", "방향 입력의 박스와 플레이어를 함께 한 칸 움직인다. Tick + Fire와 박스 밀기 가능 조건으로 사용한다")]
public sealed class PushBoxEffect : InteractionEffect
{
    /// <summary>조건 평가 이후의 상황을 다시 확인하는 실행기로 위임하고 비동기 이동 상태는 저장하지 않는다.</summary>
    /// <param name="ctx">밀기 동작을 실행할 소유 플레이어의 평가 정보다.</param>
    public override void OnActivate(in InteractionContext ctx)
    {
        // player는 밀기 동작을 보유한 플레이어이며 NPC에 잘못 조립하면 실행하지 않는다.
        Villager player = ctx.Owner as Villager;
        if (player != null && player.BoxPush != null)
            player.BoxPush.TryPush();
    }
}
