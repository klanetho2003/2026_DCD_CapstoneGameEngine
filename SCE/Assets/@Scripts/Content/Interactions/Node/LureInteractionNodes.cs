using System;
using Newtonsoft.Json;

/// <summary>버튼 · 유인 노드가 보유자(버튼 · 유인 NPC)에 붙은 상태 컴포넌트를 찾는 공용 경로다.</summary>
internal static class LureNodes
{
    /// <summary>보유자의 버튼을 찾는다. 할당 없음 (TryGetComponent).</summary>
    public static bool TryGetButton(in InteractionContext ctx, out GridButton button)
    {
        button = null;
        return ctx.Owner != null && ctx.Owner.TryGetComponent(out button);
    }

    /// <summary>보유자의 유인 객체를 찾는다. 할당 없음 (TryGetComponent).</summary>
    public static bool TryGetLure(in InteractionContext ctx, out GridLure lure)
    {
        lure = null;
        return ctx.Owner != null && ctx.Owner.TryGetComponent(out lure);
    }
}

/// <summary>보유자 버튼의 눌림 상태가 Pressed와 같은지 검사한다. 보유자가 버튼이 아니면 거짓이다.</summary>
[Serializable, InteractionNode("ButtonState", "버튼 상태", "보유자(버튼)의 눌림 상태가 Pressed와 같음. 눌리지 않았을 때만 = Pressed 끔 (GridButton 필요, 없으면 거짓)")]
public sealed class ButtonStateCondition : InteractionCondition
{
    [JsonProperty("pressed")]
    public bool Pressed;

    /// <summary>버튼 상태는 보유자에서 읽고 공유 조건에는 저장하지 않는다.</summary>
    /// <param name="ctx">버튼 칸에 도착한 TileEnter 등의 평가 정보다.</param>
    public override bool Evaluate(in InteractionContext ctx)
    {
        return LureNodes.TryGetButton(in ctx, out GridButton button) && button.IsPressed == Pressed;
    }
}

/// <summary>보유자 버튼을 눌린 상태로 바꾼다.</summary>
[Serializable, InteractionNode("PressButton", "버튼 누르기", "보유자(버튼)를 눌린 상태 · 그림으로 바꾼다. 이번 라운드 동안 눌린 채로 남는다")]
public sealed class PressButtonEffect : InteractionEffect
{
    /// <summary>버튼에 위임하고 효과에는 상태를 두지 않는다.</summary>
    /// <param name="ctx">버튼 칸에 도착한 TileEnter 등의 평가 정보다.</param>
    public override void OnActivate(in InteractionContext ctx)
    {
        if (LureNodes.TryGetButton(in ctx, out GridButton button))
            button.Press();
    }
}

/// <summary>보유자 유인 객체의 활성 상태가 Active와 같은지 검사한다. 보유자가 유인 객체가 아니면 거짓이다.</summary>
[Serializable, InteractionNode("LureState", "유인 상태", "보유자(유인 객체)의 활성 상태가 Active와 같음 (GridLure 필요, 없으면 거짓)")]
public sealed class LureStateCondition : InteractionCondition
{
    [JsonProperty("active")]
    public bool Active = true;

    /// <summary>유인 상태는 보유자에서 읽고 공유 조건에는 저장하지 않는다.</summary>
    /// <param name="ctx">유인 칸에 도착한 TileEnter 등의 평가 정보다.</param>
    public override bool Evaluate(in InteractionContext ctx)
    {
        return LureNodes.TryGetLure(in ctx, out GridLure lure) && lure.IsActive == Active;
    }
}

/// <summary>짝 유인 객체(유인 NPC의 templateID)를 켠다 — 지금 있는 모든 몬스터가 그 칸으로 오기 시작한다.</summary>
[Serializable, InteractionNode("ActivateLure", "유인 켜기", "LureId(유인 NPC의 templateID)인 유인 객체를 켠다. 켜지는 순간 있는 모든 몬스터가 그 칸으로 걸어온다")]
public sealed class ActivateLureEffect : InteractionEffect
{
    [JsonProperty("lureId")]
    public int LureId;

    /// <summary>스폰된 NPC 가운데 LureId인 유인 객체를 모두 켠다. 없으면 경고한다. O(NPC 수), 할당 없음</summary>
    /// <param name="ctx">버튼 등 보유자의 평가 정보다.</param>
    public override void OnActivate(in InteractionContext ctx)
    {
        // found는 LureId에 맞는 유인 객체를 하나라도 켰는지다.
        bool found = false;
        // npc는 지금 스폰된 NPC이며 templateID가 LureId이고 GridLure가 있으면 짝 유인 객체다.
        foreach (NPC npc in Managers.Object.NPCs)
        {
            if (npc != null && npc.TemplateId == LureId && npc.TryGetComponent(out GridLure lure))
            {
                lure.Activate();
                found = true;
            }
        }
        if (!found)
            LogPrinter.LogWarning($"[ActivateLure] 유인 객체 없음 — LureId {LureId}인 NPC가 스폰되지 않았거나 GridLure가 없음");
    }
}

/// <summary>보유자 유인 객체를 끈다 — 이 객체로 오던 몬스터는 모두 원래 행동 트리로 돌아간다.</summary>
[Serializable, InteractionNode("DeactivateLure", "유인 끄기", "보유자(유인 객체)를 끈다. 이 객체로 오던 몬스터는 모두 이동을 그만두고 원래 행동 트리대로 움직인다")]
public sealed class DeactivateLureEffect : InteractionEffect
{
    /// <summary>유인 객체에 위임하고 효과에는 상태를 두지 않는다.</summary>
    /// <param name="ctx">유인 칸에 도착한 TileEnter 등의 평가 정보다.</param>
    public override void OnActivate(in InteractionContext ctx)
    {
        if (LureNodes.TryGetLure(in ctx, out GridLure lure))
            lure.Deactivate();
    }
}
