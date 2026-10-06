using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using static Define;

#region StateValue
/// <summary>
/// 상태 키 비교. 핸들은 로드 훅에서 바인딩되고 평가 시 배열 1회 읽기.
/// 캐시 필드(_handle, _boundGeneration)는 파라미터(Key)에서 결정론적으로 유도되므로 공유해도 안전 — 조건 무상태 규칙의 예외.
/// </summary>
[Serializable]
[InteractionNode("StateValue", "상태 값", "선언된 상태 키의 값을 Value와 비교 (플래그: Equal 1)")]
public sealed class StateValueCondition : InteractionCondition, IInteractionNodeLoadHook
{
    [JsonProperty("key")]
    public string Key = "";

    [JsonProperty("op"), JsonConverter(typeof(StringEnumConverter))]
    public EComparison Op = EComparison.Equal;

    [JsonProperty("value")]
    public int Value = 1;

    private StateHandle _handle;
    private int _boundGeneration = -1;

    [JsonIgnore] public override bool ReadsGameState { get { return true; } }
    [JsonIgnore] public override bool IsContextFree { get { return true; } }

    public void OnLoad(StateKeyRegistry stateKeys, string where, List<string> errors)
    {
        if (stateKeys == null)
        {
            errors.Add($"{where}: 상태 정의 로드 전 — GameState.LoadDefinitions를 먼저 호출할 것");
            return;
        }
        if (stateKeys.TryGetHandle(Key, out _handle) == false)
        {
            errors.Add($"{where}: 선언되지 않은 상태 키 '{Key}'");
            return;
        }
        _boundGeneration = stateKeys.Generation;
    }

    public override bool Evaluate(in InteractionContext ctx)
    {
        GameStateManager state = ctx.State;
        if (state == null)
            return false;

        StateKeyRegistry registry = state.Registry;
        if (_boundGeneration != registry.Generation) // 상태 정의가 다시 로드됨 — 드묾
        {
            registry.TryGetHandle(Key, out _handle);
            _boundGeneration = registry.Generation;
        }

        return _handle.IsValid && Util.Evaluate(state.Get(_handle), Op, Value);
    }
}
#endregion

#region Day
[Serializable]
[InteractionNode("Day", "일차", "현재 일차를 Value와 비교. 범위는 Day 조건 2개로 (예: ≥ 3 그리고 ≤ 5)")]
public sealed class DayCondition : InteractionCondition
{
    [JsonProperty("op"), JsonConverter(typeof(StringEnumConverter))]
    public EComparison Op = EComparison.Equal;

    [JsonProperty("value")]
    public int Value = 1;

    [JsonIgnore] public override bool ReadsGameState { get { return true; } }
    [JsonIgnore] public override bool IsContextFree { get { return true; } }

    public override bool Evaluate(in InteractionContext ctx)
    {
        return ctx.State != null && Util.Evaluate(ctx.State.CurrentDay, Op, Value);
    }
}
#endregion

#region DayPeriod
[Serializable]
[InteractionNode("DayPeriod", "주기 일차", "Start일차부터 Interval일마다 참. 예: Start 1, Interval 7 → 1, 8, 15일차")]
public sealed class DayPeriodCondition : InteractionCondition, IInteractionNodeLoadHook
{
    [JsonProperty("start")]
    public int Start = 1;

    [JsonProperty("interval")]
    public int Interval = 7;

    [JsonIgnore] public override bool ReadsGameState { get { return true; } }
    [JsonIgnore] public override bool IsContextFree { get { return true; } }

    public void OnLoad(StateKeyRegistry stateKeys, string where, List<string> errors)
    {
        if (Interval < 1)
            errors.Add($"{where}: DayPeriod interval은 1 이상 (현재 {Interval})");
        if (Start < GameStateManager.FirstDay)
            errors.Add($"{where}: DayPeriod start는 {GameStateManager.FirstDay} 이상 (현재 {Start})");
    }

    public override bool Evaluate(in InteractionContext ctx)
    {
        if (ctx.State == null || Interval < 1)
            return false;

        int day = ctx.State.CurrentDay;
        return day >= Start && (day - Start) % Interval == 0;
    }
}
#endregion

#region RaiseSignal
/// <summary>
/// 상호작용 → 게임 이벤트. 상태는 직접 바꾸지 않는다 — 상태를 바꾸는 곳은 규칙 파일 한 곳.
/// 받는 쪽: 상태 규칙 { "event": "Signal", "ruleKey": SignalId, ... }
/// </summary>
[Serializable]
[InteractionNode("RaiseSignal", "신호 발생", "게임 이벤트 신호를 발생시킨다. 상태 규칙(event: Signal, ruleKey: 신호 ID)이 받아 값을 바꾼다")]
public sealed class RaiseSignalEffect : InteractionEffect
{
    [JsonProperty("signalId")]
    public int SignalId;

    public override void OnActivate(in InteractionContext ctx)
    {
        var signal = new SignalEvent(SignalId, ctx.Owner);

        // 소유자가 있으면 개체 발생 이벤트의 공통 정책을 거친다
        if (ctx.Owner != null)
            ctx.Owner.RaiseGameEvent(in signal);
        else
            GameEventBus.Raise(in signal);
    }
}
#endregion