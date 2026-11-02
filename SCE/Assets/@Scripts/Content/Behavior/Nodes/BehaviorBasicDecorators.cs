using System;
using System.Text;
using Newtonsoft.Json;

/// <summary>Blackboard 조건이 보는 것</summary>
public enum EBehaviorKeyCheck : byte
{
    IsSet,          // 값이 있다 (크리처 — 쓸 수 있는 크리처, Vector2 — 지우지 않음, 그 밖은 늘 참)
    IsNotSet,
    IsTrue,         // Bool 키
    IsFalse,
    Equal,          // Int · Float 키를 value와 (Int 키는 float로)
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
}

/// <summary>Blackboard 키 값을 보는 조건 — 있음 · 참 · 비교. LowerPriority로 지켜볼 때 키가 바뀌면 (참인 채여도) 다시 검색을 요청한다</summary>
[Serializable]
[BehaviorNode("Blackboard", "Blackboard 조건", "키 값을 본다 — 있음 · 참 · 거짓 · 비교", Category = "Blackboard")]
public sealed class BehaviorBlackboardDecorator : BehaviorDecorator
{
    [JsonProperty("key")] public BehaviorKeySelector Key;
    [JsonProperty("check")] public EBehaviorKeyCheck Check = EBehaviorKeyCheck.IsSet;

    /// <summary>비교 값 (Equal ~ GreaterOrEqual) — Int 키도 float로 비교한다</summary>
    [JsonProperty("value")] public BehaviorFloat Value;

    /// <summary>키 값이 바뀌면 조건이 참인 채여도 붙은 가지를 다시 시작한다 (Unreal의 Notify Observer: On Value Change) — Self · Both일 때</summary>
    [JsonProperty("notifyOnValueChange")] public bool NotifyOnValueChange;

    public override int RestartKeySlot => NotifyOnValueChange ? Key.Slot : -1;

    public override int ObservedKeySlot => Key.Slot;

    public override void Compile(BehaviorCompileContext context)
    {
        bool resolved = context.ResolveKey(ref Key, "key", EBehaviorKeyTypeMask.Any);
        context.Resolve(ref Value, "value");
        if (Enum.IsDefined(typeof(EBehaviorKeyCheck), Check) == false)
        {
            context.Error("check", $"모르는 값 {(int)Check}");
            return;
        }
        if (resolved == false)
            return;
        EBehaviorKeyType type = context.Blackboard.TypeOf(Key.Slot);
        switch (Check)
        {
            case EBehaviorKeyCheck.IsSet:
            case EBehaviorKeyCheck.IsNotSet:
                if (type != EBehaviorKeyType.Creature && type != EBehaviorKeyType.Vector2)
                    context.Warning("check", $"{type} 키는 늘 값이 있다 — {Check}는 늘 {(Check == EBehaviorKeyCheck.IsSet ? "참" : "거짓")}");
                break;
            case EBehaviorKeyCheck.IsTrue:
            case EBehaviorKeyCheck.IsFalse:
                if (type != EBehaviorKeyType.Bool)
                    context.Error("check", $"{Check}는 Bool 키만 볼 수 있다 ('{Key.Name}'는 {type})");
                break;
            default:
                if (type != EBehaviorKeyType.Int && type != EBehaviorKeyType.Float)
                    context.Error("check", $"{Check}는 Int · Float 키만 비교할 수 있다 ('{Key.Name}'는 {type})");
                break;
        }
    }

    protected override bool CalculateRawConditionValue(BehaviorTreeRunner runner)
    {
        BehaviorBlackboard blackboard = runner.Blackboard;
        int slot = Key.Slot;
        switch (Check)
        {
            case EBehaviorKeyCheck.IsSet:
                return blackboard.HasValue(slot);
            case EBehaviorKeyCheck.IsNotSet:
                return blackboard.HasValue(slot) == false;
            case EBehaviorKeyCheck.IsTrue:
                return blackboard.GetBool(slot);
            case EBehaviorKeyCheck.IsFalse:
                return blackboard.GetBool(slot) == false;
        }
        float current = blackboard.Layout.TypeOf(slot) == EBehaviorKeyType.Int ? blackboard.GetInt(slot) : blackboard.GetFloat(slot);
        float value = Value.Get(runner);
        switch (Check)
        {
            case EBehaviorKeyCheck.Equal:
                return current == value;
            case EBehaviorKeyCheck.NotEqual:
                return current != value;
            case EBehaviorKeyCheck.Less:
                return current < value;
            case EBehaviorKeyCheck.LessOrEqual:
                return current <= value;
            case EBehaviorKeyCheck.Greater:
                return current > value;
            default:
                return current >= value;
        }
    }

#if UNITY_EDITOR
    public override void DescribeRuntimeValues(BehaviorTreeRunner runner, bool active, StringBuilder text)
    {
        if (Key.Slot < 0)
            return;
        text.Append(Key.Name).Append(" = ").Append(runner.Blackboard.DebugValueText(Key.Slot));
        if (Check >= EBehaviorKeyCheck.Equal)
            text.Append(" · 비교 ").Append(BehaviorDebugText.Number(Value.Get(runner)));
    }

    /// <summary>
    /// value는 비교 + Int · Float 키 (고르지 않았거나 없는 키면 비교만 보고)일 때만, notifyOnValueChange는 Abort Mode가 Self · Both일 때만 쓴다.
    /// value가 Float 키가 아닌 키 (없는 키 포함)를 가리키거나 (컴파일 에러) 유한하지 않은 수면 (저장할 수 없음) 고칠 수 있게 잠그지 않는다
    /// </summary>
    public override string UnusedFieldReason(string field, BehaviorBlackboardKey[] keys)
    {
        switch (field)
        {
            case "value":
                if (Value.UsesKey && KeyTypeOf(keys, Value.Key) != EBehaviorKeyType.Float)
                    return null;
                if (Value.UsesKey == false && (float.IsNaN(Value.Value) || float.IsInfinity(Value.Value)))
                    return null;
                if (IsComparison(Check) == false)
                    return "Check가 비교 (Equal ~ GreaterOrEqual)일 때만 쓴다";
                EBehaviorKeyType? type = Key.IsSelected ? KeyTypeOf(keys, Key.Name) : null;
                return type.HasValue && type.Value != EBehaviorKeyType.Int && type.Value != EBehaviorKeyType.Float
                    ? $"비교는 Int · Float 키만 — '{Key.Name}'는 {type.Value}"
                    : null;
            case "notifyOnValueChange":
                return BehaviorTreeCompiler.HasSelf(AbortMode) ? null : "Abort Mode가 Self · Both일 때만 쓴다 (값이 바뀌면 붙은 가지를 다시 시작)";
            default:
                return base.UnusedFieldReason(field, keys);
        }
    }

    /// <summary>check 목록 = 고른 키의 형식이 볼 수 있는 것 — Bool: IsTrue · IsFalse, Int · Float: 비교, Creature · Vector2: IsSet · IsNotSet. 키를 고르지 않았거나 없는 키면 모두</summary>
    public override bool AllowsChoice(string field, int value, BehaviorBlackboardKey[] keys)
    {
        if (field != "check")
            return base.AllowsChoice(field, value, keys);
        EBehaviorKeyType? type = Key.IsSelected ? KeyTypeOf(keys, Key.Name) : null;
        if (type.HasValue == false)
            return true;
        var check = (EBehaviorKeyCheck)value;
        switch (type.Value)
        {
            case EBehaviorKeyType.Bool:
                return check == EBehaviorKeyCheck.IsTrue || check == EBehaviorKeyCheck.IsFalse;
            case EBehaviorKeyType.Int:
            case EBehaviorKeyType.Float:
                return IsComparison(check);
            default:
                return check == EBehaviorKeyCheck.IsSet || check == EBehaviorKeyCheck.IsNotSet;
        }
    }

    /// <summary>value와 견주는 Check인가 (Equal ~ GreaterOrEqual)</summary>
    private static bool IsComparison(EBehaviorKeyCheck check)
    {
        return check >= EBehaviorKeyCheck.Equal && check <= EBehaviorKeyCheck.GreaterOrEqual;
    }

    /// <summary>키 목록에서 그 이름의 형식 — 목록에 없으면 null (키 목록 차례로 처음 것). O(키 수)</summary>
    private static EBehaviorKeyType? KeyTypeOf(BehaviorBlackboardKey[] keys, string name)
    {
        if (keys == null)
            return null;
        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i] != null && string.Equals(keys[i].Name, name, StringComparison.Ordinal))
                return keys[i].Type;
        }
        return null;
    }
#endif
}

/// <summary>붙은 노드가 끝난 (끊긴) 뒤 seconds초 동안 들어가지 못한다. 쿨다운이 끝나면 LowerPriority로 다시 검색을 요청할 수 있다. 칸 0: 끝 시각</summary>
[Serializable]
[BehaviorNode("Cooldown", "쿨다운", "붙은 노드가 끝난 뒤 seconds초 동안 들어가지 못한다")]
public sealed class BehaviorCooldownDecorator : BehaviorDecorator
{
    [JsonProperty("seconds")] public BehaviorFloat Seconds = new BehaviorFloat(5f);

    /// <summary>성공으로 끝났을 때만 쿨다운을 센다 (실패 · 끊김은 세지 않는다)</summary>
    [JsonProperty("onlyAfterSuccess")] public bool OnlyAfterSuccess;

    public override int MemorySlotCount => 1;

    public override bool AllowsAbortMode(EBehaviorAbortMode mode)
    {
        return mode == EBehaviorAbortMode.None || mode == EBehaviorAbortMode.LowerPriority;
    }

    public override bool AllowsInverse => false;

    public override void Compile(BehaviorCompileContext context)
    {
        context.Resolve(ref Seconds, "seconds");
        if (Seconds.UsesKey == false && Seconds.Value < 0f)
            context.Error("seconds", "0 이상이어야 한다");
    }

    protected override bool CalculateRawConditionValue(BehaviorTreeRunner runner)
    {
        return runner.Now >= runner.Memory(this).Float0;
    }

    public override void OnNodeDeactivation(BehaviorTreeRunner runner, EBehaviorStatus result, bool aborted)
    {
        if (OnlyAfterSuccess && (aborted || result != EBehaviorStatus.Success))
            return;
        runner.Memory(this).Float0 = runner.Now + Seconds.Get(runner);
    }

#if UNITY_EDITOR
    public override void DescribeRuntimeValues(BehaviorTreeRunner runner, bool active, StringBuilder text)
    {
        float left = runner.Memory(this).Float0 - runner.Now;
        text.Append(left > 0f ? "남은 " + BehaviorDebugText.Number(left) + "초" : "준비됨");
    }
#endif
}

/// <summary>붙은 노드가 seconds초 안에 끝나지 않으면 실패로 끊는다 (Observer Aborts는 Self). 칸 0: Int0 진행 중 · Float0 기한</summary>
[Serializable]
[BehaviorNode("TimeLimit", "시간 제한", "붙은 노드가 seconds초 안에 끝나지 않으면 실패로 끊는다")]
public sealed class BehaviorTimeLimitDecorator : BehaviorDecorator
{
    [JsonProperty("seconds")] public BehaviorFloat Seconds = new BehaviorFloat(5f);

    public BehaviorTimeLimitDecorator()
    {
        AbortMode = EBehaviorAbortMode.Self;
    }

    public override int MemorySlotCount => 1;

    public override bool AllowsAbortMode(EBehaviorAbortMode mode)
    {
        return mode == EBehaviorAbortMode.Self;
    }

    public override bool AllowsInverse => false;

    public override void Compile(BehaviorCompileContext context)
    {
        context.Resolve(ref Seconds, "seconds");
        if (Seconds.UsesKey == false && Seconds.Value <= 0f)
            context.Error("seconds", "0보다 커야 한다");
    }

    protected override bool CalculateRawConditionValue(BehaviorTreeRunner runner)
    {
        ref BehaviorNodeMemory memory = ref runner.Memory(this);
        return memory.Int0 == 0 || runner.Now < memory.Float0;
    }

    public override void OnNodeActivation(BehaviorTreeRunner runner)
    {
        ref BehaviorNodeMemory memory = ref runner.Memory(this);
        memory.Int0 = 1;
        memory.Float0 = runner.Now + Seconds.Get(runner);
    }

    public override void OnNodeDeactivation(BehaviorTreeRunner runner, EBehaviorStatus result, bool aborted)
    {
        runner.Memory(this).Int0 = 0;
    }

#if UNITY_EDITOR
    public override void DescribeRuntimeValues(BehaviorTreeRunner runner, bool active, StringBuilder text)
    {
        ref BehaviorNodeMemory memory = ref runner.Memory(this);
        if (memory.Int0 != 0)
            text.Append("남은 ").Append(BehaviorDebugText.Number(Math.Max(0f, memory.Float0 - runner.Now))).Append("초");
    }
#endif
}

/// <summary>붙은 노드를 count번 반복한다 (0 = 끝없이, 키 값이 음수면 한 번) — 결과와 상관없이. 회마다 조건을 다시 본다. 칸 0: Int0 남은 횟수 (-1 = 끝없이)</summary>
[Serializable]
[BehaviorNode("Loop", "반복", "붙은 노드를 count번 반복한다 — 0이면 끝없이")]
public sealed class BehaviorLoopDecorator : BehaviorDecorator
{
    [JsonProperty("count")] public BehaviorInt Count = new BehaviorInt(3);

    public override int MemorySlotCount => 1;

    public override bool AllowsAbortMode(EBehaviorAbortMode mode)
    {
        return mode == EBehaviorAbortMode.None;
    }

    public override bool AllowsInverse => false;

    public override void Compile(BehaviorCompileContext context)
    {
        context.Resolve(ref Count, "count");
        if (Count.UsesKey == false && Count.Value < 0)
            context.Error("count", "0 이상이어야 한다 (0 = 끝없이)");
    }

    public override void OnNodeActivation(BehaviorTreeRunner runner)
    {
        if (runner.IsRepeatEntry)
            return;
        int count = Count.Get(runner);
        runner.Memory(this).Int0 = count == 0 ? -1 : (count < 0 ? 1 : count); // 키 값이 음수면 한 번
    }

    public override bool ShouldRepeat(BehaviorTreeRunner runner, EBehaviorStatus result)
    {
        ref BehaviorNodeMemory memory = ref runner.Memory(this);
        if (memory.Int0 < 0)
            return true;
        memory.Int0--;
        return memory.Int0 > 0;
    }

#if UNITY_EDITOR
    public override void DescribeRuntimeValues(BehaviorTreeRunner runner, bool active, StringBuilder text)
    {
        if (active == false)
            return;
        int left = runner.Memory(this).Int0;
        if (left < 0)
            text.Append("끝없이");
        else
            text.Append("남은 ").Append(left).Append("회 (이번 포함)");
    }
#endif
}

/// <summary>붙은 노드의 결과를 늘 성공으로 — Sequence가 실패해도 다음 단계로 가게</summary>
[Serializable]
[BehaviorNode("ForceSuccess", "성공으로", "붙은 노드의 결과를 늘 성공으로 바꾼다")]
public sealed class BehaviorForceSuccessDecorator : BehaviorDecorator
{
    public override bool AllowsAbortMode(EBehaviorAbortMode mode)
    {
        return mode == EBehaviorAbortMode.None;
    }

    public override bool AllowsInverse => false;

    public override EBehaviorStatus ModifyResult(BehaviorTreeRunner runner, EBehaviorStatus result)
    {
        return EBehaviorStatus.Success;
    }
}