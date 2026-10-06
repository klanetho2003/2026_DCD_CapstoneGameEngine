using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Define;

public class GameStateRuleTests
{
    private const string Json = @"{
      ""id"": ""test"",
      ""keys"": [
        { ""key"": ""kill.slime"", ""default"": 0 },
        { ""key"": ""kill.any"",   ""default"": 0 },
        { ""key"": ""ach.slime3"", ""default"": 0 }
      ],
      ""rules"": [
        { ""event"": ""CreatureDied"", ""ruleKey"": 2001, ""ops"": [ { ""key"": ""kill.slime"", ""op"": ""Add"", ""value"": 1 } ] },
        { ""event"": ""CreatureDied"", ""ops"": [ { ""key"": ""kill.any"", ""op"": ""Add"", ""value"": 1 } ] },
        { ""event"": ""CreatureDied"", ""ruleKey"": 2001,
          ""require"": [ { ""key"": ""kill.slime"", ""cmp"": ""GreaterOrEqual"", ""value"": 3 } ],
          ""ops"": [ { ""key"": ""ach.slime3"", ""op"": ""Max"", ""value"": 1 } ] }
      ]
    }";

    private GameStateManager _state;

    [SetUp]
    public void SetUp()
    {
        GameEventBus.ResetAll();
        _state = new GameStateManager();
        Assert.IsTrue(_state.LoadDefinitions(new[] { Json }));
    }

    [TearDown]
    public void TearDown()
    {
        GameEventBus.ResetAll();
    }

    private int Value(string key)
    {
        Assert.IsTrue(_state.TryGet(key, out int value), key);
        return value;
    }

    [Test]
    public void KeyedRule_OnlyMatchingKey()
    {
        _state.OnEventHeader(EGameEventType.CreatureDied, 2001, null);
        _state.OnEventHeader(EGameEventType.CreatureDied, 9999, null);

        Assert.AreEqual(1, Value("kill.slime"));
        Assert.AreEqual(2, Value("kill.any")); // ruleKey 생략 = 모든 키
    }

    [Test]
    public void Require_SeesPreviousRuleResult_InSameEvent()
    {
        _state.OnEventHeader(EGameEventType.CreatureDied, 2001, null);
        _state.OnEventHeader(EGameEventType.CreatureDied, 2001, null);
        Assert.AreEqual(0, Value("ach.slime3"));

        _state.OnEventHeader(EGameEventType.CreatureDied, 2001, null); // 3번째 — 첫 규칙이 3으로 만든 뒤 업적 규칙이 본다
        Assert.AreEqual(1, Value("ach.slime3"));
    }

    [Test]
    public void ChangeVersion_OnlyOnActualChange()
    {
        _state.Registry.TryGetHandle("kill.slime", out StateHandle handle);
        int before = _state.ChangeVersion;

        Assert.IsFalse(_state.Set(handle, 0)); // 기본값과 같음
        Assert.AreEqual(before, _state.ChangeVersion);

        Assert.IsTrue(_state.Set(handle, 5));
        Assert.AreNotEqual(before, _state.ChangeVersion);
    }

    [Test]
    public void ViaBus_StateUpdatedBeforeListener()
    {
        GameEventBus.SetRuleSink(_state);
        var listener = new ReadOnDied(_state);
        GameEventBus.Subscribe(listener);

        GameEventBus.Raise(new CreatureDiedEvent(null, null, 2001));

        Assert.AreEqual(1, listener.SeenKillCount); // 리스너가 받을 때 이미 갱신되어 있다
    }

    private sealed class ReadOnDied : IGameEventListener<CreatureDiedEvent>
    {
        private readonly GameStateManager _state;
        public int SeenKillCount = -1;
        public ReadOnDied(GameStateManager state) { _state = state; }

        public void OnGameEvent(in CreatureDiedEvent evt)
        {
            _state.TryGet("kill.slime", out SeenKillCount);
        }
    }

    [Test]
    public void UndeclaredKey_RuleSkipped_LoadReportsError()
    {
        const string bad = @"{ ""id"": ""bad"", ""keys"": [ { ""key"": ""a"", ""default"": 0 } ],
          ""rules"": [ { ""event"": ""Signal"", ""ops"": [ { ""key"": ""a"", ""op"": ""Add"", ""value"": 1 },
                                                        { ""key"": ""typo"", ""op"": ""Add"", ""value"": 1 } ] } ] }";
        var state = new GameStateManager();
        LogAssert.Expect(LogType.Error, new Regex("선언되지 않은 키 'typo'"));

        Assert.IsFalse(state.LoadDefinitions(new[] { bad }));

        state.OnEventHeader(EGameEventType.Signal, 0, null);
        state.TryGet("a", out int a);
        Assert.AreEqual(0, a); // 반쪽 규칙을 만들지 않는다 — 올바른 op도 실행되지 않음
    }

    [Test]
    public void SystemKey_WriteByRule_Rejected()
    {
        const string bad = @"{ ""id"": ""bad"", ""rules"": [ { ""event"": ""Signal"", ""ops"": [ { ""key"": ""sys.day"", ""op"": ""Set"", ""value"": 9 } ] } ] }";
        var state = new GameStateManager();
        LogAssert.Expect(LogType.Error, new Regex("시스템 키"));

        Assert.IsFalse(state.LoadDefinitions(new[] { bad }));
        Assert.AreEqual(GameStateManager.FirstDay, state.CurrentDay);
    }

    [Test]
    public void CrossFileKeyReference_Allowed()
    {
        const string keys = @"{ ""id"": ""keys"", ""keys"": [ { ""key"": ""shared"", ""default"": 0 } ] }";
        const string rules = @"{ ""id"": ""rules"", ""rules"": [ { ""event"": ""Signal"", ""ruleKey"": 1, ""ops"": [ { ""key"": ""shared"", ""op"": ""Set"", ""value"": 7 } ] } ] }";
        var state = new GameStateManager();

        Assert.IsTrue(state.LoadDefinitions(new[] { rules, keys })); // 규칙 파일이 먼저 와도 된다
        state.OnEventHeader(EGameEventType.Signal, 1, null);
        state.TryGet("shared", out int value);
        Assert.AreEqual(7, value);
    }
}