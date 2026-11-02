using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints; // AllocatingGCMemory 확장 메서드 (ConstraintExtensions)

/// <summary>SCE로 옮긴 행동 트리 실행 기반 확인 — JSON 읽기 · 컴파일, Wait · Loop 시간, 평가 중 할당 0</summary>
public class BehaviorCoreTests
{
    private sealed class FakeAgent : IBehaviorAgent
    {
        public Vector2 Position => Vector2.zero;
        public int SpawnSerial => 1;
        public bool IsActionLocked => false;
        public bool HasTag(Define.ECreatureTag tags) { return false; }
        public void StopMoving() { }
        public bool TryGetIntSeed(string seed, out int value) { value = 0; return false; }
    }

    private sealed class FakeWorld : IBehaviorWorld
    {
        public CreatureBase PossessedTarget => null;
    }

    private const string WaitThenSetJson = @"{
  ""id"": ""Test_WaitThenSet"",
  ""settings"": { ""tickInterval"": 0.0 },
  ""blackboard"": [ { ""name"": ""Done"", ""type"": ""Bool"", ""default"": false } ],
  ""root"": { ""type"": ""Sequence"", ""children"": [
    { ""type"": ""Wait"", ""seconds"": 1.0 },
    { ""type"": ""SetBool"", ""key"": ""Done"", ""value"": true } ] }
}";

    private const string LoopWaitThenSetJson = @"{
  ""id"": ""Test_LoopWaitThenSet"",
  ""settings"": { ""tickInterval"": 0.0 },
  ""blackboard"": [ { ""name"": ""Done"", ""type"": ""Bool"", ""default"": false } ],
  ""root"": { ""type"": ""Sequence"", ""children"": [
    { ""type"": ""Wait"", ""seconds"": 1.0, ""decorators"": [ { ""type"": ""Loop"", ""count"": 3 } ] },
    { ""type"": ""SetBool"", ""key"": ""Done"", ""value"": true } ] }
}";

    private const string EndlessWaitJson = @"{
  ""id"": ""Test_EndlessWait"",
  ""settings"": { ""tickInterval"": 0.0 },
  ""root"": { ""type"": ""Sequence"", ""children"": [
    { ""type"": ""Wait"", ""seconds"": 0.1, ""decorators"": [ { ""type"": ""Loop"", ""count"": 0 } ] } ] }
}";

    private static BehaviorTreeDefinition Load(string json)
    {
        var messages = new List<BehaviorCompileMessage>();
        BehaviorTreeDefinition definition = BehaviorTreeLoader.Load(json, BehaviorNodeRegistry.Default, messages);
        var text = new StringBuilder();
        foreach (BehaviorCompileMessage message in messages)
            text.AppendLine(message.ToString());
        Assert.IsNotNull(definition, text.ToString());
        return definition;
    }

    private static BehaviorTreeRunner Bind(BehaviorTreeDefinition definition)
    {
        var runner = new BehaviorTreeRunner(new FakeAgent(), new FakeWorld());
        runner.Bind(definition);
        return runner;
    }

    private static bool ReadDone(BehaviorTreeRunner runner)
    {
        Assert.IsTrue(runner.Definition.Blackboard.TryGetSlot("Done", out int slot));
        return runner.Blackboard.GetBool(slot);
    }

    [Test]
    public void Load_UnknownNodeType_FailsWithError()
    {
        var messages = new List<BehaviorCompileMessage>();
        BehaviorTreeDefinition definition = BehaviorTreeLoader.Load(@"{ ""id"": ""Bad"", ""root"": { ""type"": ""NoSuchNode"" } }", BehaviorNodeRegistry.Default, messages);
        Assert.IsNull(definition);
        Assert.IsTrue(messages.Exists(message => message.IsError));
    }

    [Test]
    public void Wait_EndsAfterSeconds_ThenNextChildRuns()
    {
        BehaviorTreeRunner runner = Bind(Load(WaitThenSetJson));

        runner.Tick(0f);
        runner.Tick(0.5f);
        runner.Tick(0.99f);
        Assert.IsFalse(ReadDone(runner), "1초 전에는 Wait가 끝나지 않는다");

        runner.Tick(1f);
        Assert.IsTrue(ReadDone(runner), "Wait가 끝난 같은 평가에서 다음 자식이 실행된다");
    }

    [Test]
    public void Loop_RepeatsWaitCountTimes()
    {
        BehaviorTreeRunner runner = Bind(Load(LoopWaitThenSetJson));

        float[] times = { 0f, 0.5f, 1f, 1.5f, 2f, 2.5f, 2.99f };
        foreach (float time in times)
            runner.Tick(time);
        Assert.IsFalse(ReadDone(runner), "Wait 1초를 세 번 마치기 전");

        runner.Tick(3f);
        Assert.IsTrue(ReadDone(runner), "세 번째 Wait가 끝나면 다음 자식");
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        BehaviorTreeRunner runner = Bind(Load(EndlessWaitJson));
        float time = 0f;
        for (int i = 0; i < 40; i++, time += 0.05f)
            runner.Tick(time);

        Assert.That(() =>
        {
            for (int i = 0; i < 200; i++, time += 0.05f)
                runner.Tick(time);
        }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        Assert.IsFalse(runner.IsFaulted);
    }
}
