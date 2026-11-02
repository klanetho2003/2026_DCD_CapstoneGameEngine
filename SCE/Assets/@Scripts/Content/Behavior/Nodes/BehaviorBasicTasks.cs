using System;
using System.Text;
using Newtonsoft.Json;

/// <summary>정해진 시간 (± 편차)을 기다린다 — 몬스터마다 편차가 달라 같은 트리의 몬스터들이 같은 프레임에 움직이지 않는다. 칸 0: 끝 시각</summary>
[Serializable]
[BehaviorNode("Wait", "기다리기", "seconds ± randomDeviation초 기다린 뒤 성공")]
public sealed class BehaviorWaitTask : BehaviorTask
{
    [JsonProperty("seconds")] public BehaviorFloat Seconds = new BehaviorFloat(1f);

    /// <summary>기다리는 시간의 무작위 편차 (초) — 0 이상</summary>
    [JsonProperty("randomDeviation")] public BehaviorFloat RandomDeviation;

    public override int MemorySlotCount => 1;

    public override void Compile(BehaviorCompileContext context)
    {
        context.Resolve(ref Seconds, "seconds");
        context.Resolve(ref RandomDeviation, "randomDeviation");
        if (Seconds.UsesKey == false && Seconds.Value < 0f)
            context.Error("seconds", "0 이상이어야 한다");
        if (RandomDeviation.UsesKey == false && RandomDeviation.Value < 0f)
            context.Error("randomDeviation", "0 이상이어야 한다");
    }

    public override EBehaviorStatus OnStart(BehaviorTreeRunner runner)
    {
        float wait = Seconds.Get(runner) + (runner.NextRandom() * 2f - 1f) * RandomDeviation.Get(runner);
        if (wait <= 0f)
            return EBehaviorStatus.Success;
        runner.Memory(this).Float0 = runner.Now + wait;
        return EBehaviorStatus.Running;
    }

    public override EBehaviorStatus OnUpdate(BehaviorTreeRunner runner)
    {
        return runner.Now >= runner.Memory(this).Float0 ? EBehaviorStatus.Success : EBehaviorStatus.Running;
    }

#if UNITY_EDITOR
    public override void DescribeRuntimeValues(BehaviorTreeRunner runner, bool active, StringBuilder text)
    {
        if (active)
            text.Append("남은 ").Append(BehaviorDebugText.Number(Math.Max(0f, runner.Memory(this).Float0 - runner.Now))).Append("초");
    }
#endif
}

/// <summary>끝나지 않는 Task — 아무것도 하지 않고 진행 중. 보통 Selector의 마지막 자식 (할 일이 없을 때)</summary>
[Serializable]
[BehaviorNode("Idle", "가만히", "끝나지 않는다 — 조건이 바뀌어 끊길 때까지")]
public sealed class BehaviorIdleTask : BehaviorTask
{
    public override EBehaviorStatus OnStart(BehaviorTreeRunner runner)
    {
        return EBehaviorStatus.Running;
    }
}

/// <summary>Bool 키에 값을 쓰고 바로 성공</summary>
[Serializable]
[BehaviorNode("SetBool", "Bool 키 쓰기", "Bool 키에 value를 쓴다", Category = "Blackboard")]
public sealed class BehaviorSetBoolTask : BehaviorTask
{
    [JsonProperty("key")] public BehaviorKeySelector Key;
    [JsonProperty("value")] public BehaviorBool Value = new BehaviorBool(true);

    public override void Compile(BehaviorCompileContext context)
    {
        context.ResolveKey(ref Key, "key", EBehaviorKeyTypeMask.Bool);
        context.Resolve(ref Value, "value");
    }

    public override EBehaviorStatus OnStart(BehaviorTreeRunner runner)
    {
        runner.Blackboard.SetBool(Key.Slot, Value.Get(runner));
        return EBehaviorStatus.Success;
    }
}

/// <summary>Int 키에 값을 쓰고 바로 성공</summary>
[Serializable]
[BehaviorNode("SetInt", "Int 키 쓰기", "Int 키에 value를 쓴다", Category = "Blackboard")]
public sealed class BehaviorSetIntTask : BehaviorTask
{
    [JsonProperty("key")] public BehaviorKeySelector Key;
    [JsonProperty("value")] public BehaviorInt Value;

    public override void Compile(BehaviorCompileContext context)
    {
        context.ResolveKey(ref Key, "key", EBehaviorKeyTypeMask.Int);
        context.Resolve(ref Value, "value");
    }

    public override EBehaviorStatus OnStart(BehaviorTreeRunner runner)
    {
        runner.Blackboard.SetInt(Key.Slot, Value.Get(runner));
        return EBehaviorStatus.Success;
    }
}

/// <summary>Float 키에 값을 쓰고 바로 성공</summary>
[Serializable]
[BehaviorNode("SetFloat", "Float 키 쓰기", "Float 키에 value를 쓴다", Category = "Blackboard")]
public sealed class BehaviorSetFloatTask : BehaviorTask
{
    [JsonProperty("key")] public BehaviorKeySelector Key;
    [JsonProperty("value")] public BehaviorFloat Value;

    public override void Compile(BehaviorCompileContext context)
    {
        context.ResolveKey(ref Key, "key", EBehaviorKeyTypeMask.Float);
        context.Resolve(ref Value, "value");
    }

    public override EBehaviorStatus OnStart(BehaviorTreeRunner runner)
    {
        runner.Blackboard.SetFloat(Key.Slot, Value.Get(runner));
        return EBehaviorStatus.Success;
    }
}

/// <summary>Vector2 키에 값을 쓰고 바로 성공</summary>
[Serializable]
[BehaviorNode("SetVector2", "Vector2 키 쓰기", "Vector2 키에 value를 쓴다", Category = "Blackboard")]
public sealed class BehaviorSetVector2Task : BehaviorTask
{
    [JsonProperty("key")] public BehaviorKeySelector Key;
    [JsonProperty("value")] public BehaviorVector2 Value;

    public override void Compile(BehaviorCompileContext context)
    {
        context.ResolveKey(ref Key, "key", EBehaviorKeyTypeMask.Vector2);
        context.Resolve(ref Value, "value");
    }

    public override EBehaviorStatus OnStart(BehaviorTreeRunner runner)
    {
        runner.Blackboard.SetVector2(Key.Slot, Value.Get(runner));
        return EBehaviorStatus.Success;
    }
}

/// <summary>키를 지우고 바로 성공 — Vector2 · 크리처는 '없음', Bool · Int · Float는 키의 기본값</summary>
[Serializable]
[BehaviorNode("ClearKey", "키 지우기", "키를 지운다 — Vector2 · 크리처는 없음, 그 밖은 기본값", Category = "Blackboard")]
public sealed class BehaviorClearKeyTask : BehaviorTask
{
    [JsonProperty("key")] public BehaviorKeySelector Key;

    public override void Compile(BehaviorCompileContext context)
    {
        context.ResolveKey(ref Key, "key", EBehaviorKeyTypeMask.Any);
    }

    public override EBehaviorStatus OnStart(BehaviorTreeRunner runner)
    {
        runner.Blackboard.Clear(Key.Slot);
        return EBehaviorStatus.Success;
    }
}