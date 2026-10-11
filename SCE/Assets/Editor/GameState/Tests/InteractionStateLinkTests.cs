using System.Collections.Generic;
using NUnit.Framework;
using static Define;

/// <summary>
/// Interaction Editor의 상태 연결 테스트 — 상태 정의 조회(StateDefinitionCatalog), 신호를 받는 규칙 문구, 상호작용 Set 검사(InteractionStateLinkCheck).
/// 파일 IO 없이 Set 목록으로 채워서 확인한다.
/// </summary>
public class InteractionStateLinkTests
{
    // 선언된 키 1개 + 이름 규칙에 어긋난 선언 1개 / 신호 100번 규칙 + 같은 숫자의 개체 사망 규칙
    private const string CoreJson = @"{ ""id"": ""core"",
      ""keys"": [ { ""key"": ""quest.done"", ""default"": 0, ""desc"": ""퀘스트 완료"" }, { ""key"": ""bad key"", ""default"": 0 } ],
      ""rules"": [
        { ""event"": ""Signal"",       ""ruleKey"": 100, ""ops"": [ { ""key"": ""quest.done"", ""op"": ""Set"", ""value"": 1 } ] },
        { ""event"": ""CreatureDied"", ""ruleKey"": 200, ""ops"": [ { ""key"": ""quest.done"", ""op"": ""Set"", ""value"": 1 } ] } ] }";

    // "특정 값만"이 꺼진 신호 규칙 — 모든 신호를 받는다
    private const string AnySignalJson = @"{ ""id"": ""any"", ""keys"": [ { ""key"": ""signal.count"", ""default"": 0 } ],
      ""rules"": [ { ""event"": ""Signal"", ""ops"": [ { ""key"": ""signal.count"", ""op"": ""Add"", ""value"": 1 } ] } ] }";

    #region 상태 정의 조회
    [Test]
    public void Catalog_KnowsWhatTheGameRegisters()
    {
        StateDefinitionCatalog catalog = Catalog(CoreJson);

        Assert.IsTrue(catalog.IsComplete);
        Assert.IsTrue(catalog.IsDeclared("quest.done"));
        Assert.IsTrue(catalog.IsDeclared(GameStateManager.DayKey), "시스템 키");
        Assert.IsFalse(catalog.IsDeclared("bad key"), "이름 규칙에 어긋나 등록이 거부된 선언");
        Assert.IsFalse(catalog.IsDeclared("quest.typo"));
        Assert.IsFalse(catalog.IsDeclared(""));
        Assert.IsFalse(catalog.IsDeclared(null));

        Assert.IsTrue(catalog.TryGetKey("quest.done", out StateKeyDefinition key));
        Assert.AreEqual("퀘스트 완료", key.Description);
        Assert.AreEqual("core", catalog.OwnerOf("quest.done"));
        Assert.AreEqual("", catalog.OwnerOf(GameStateManager.DayKey), "시스템 키는 선언한 파일이 없다");
        Assert.IsFalse(catalog.TryGetKey("quest.typo", out _));
    }

    [Test]
    public void LooksLikeStateDefinition_SeparatesBrokenStateFilesFromOtherJson()
    {
        Assert.IsTrue(StateDefinitionCatalog.LooksLikeStateDefinition(@"{ ""id"": ""x"", ""keys"": [ { ""key"": ""a"", ""defualt"": 0 } ] }"), "필드 오타가 있는 상태 파일");
        Assert.IsTrue(StateDefinitionCatalog.LooksLikeStateDefinition(@"{ ""id"": ""x"", ""keys"": [ "), "문법이 깨져 종류를 알 수 없으면 숨기지 않는다");
        Assert.IsFalse(StateDefinitionCatalog.LooksLikeStateDefinition(@"{ ""creatures"": [] }"), "다른 종류의 JSON");
        Assert.IsFalse(StateDefinitionCatalog.LooksLikeStateDefinition(@"[ 1, 2, 3 ]"));
    }

    [Test]
    public void DescribeReceivers_ListsSignalRules()
    {
        List<StateDefinitionSet> both = Parse(CoreJson, AnySignalJson);

        Assert.AreEqual("core.rules[0], any.rules[0] (모든 신호)", StateInteractionCrossCheck.DescribeReceivers(both, 100, 3));
        Assert.AreEqual("any.rules[0] (모든 신호)", StateInteractionCrossCheck.DescribeReceivers(both, 200, 3), "개체 사망 규칙의 ruleKey 200은 신호가 아니다");
        Assert.AreEqual("core.rules[0] 외 1곳", StateInteractionCrossCheck.DescribeReceivers(both, 100, 1));
        Assert.IsNull(StateInteractionCrossCheck.DescribeReceivers(Parse(CoreJson), 200, 3));
    }
    #endregion

    #region 상호작용 Set 검사
    [Test]
    public void Check_UndeclaredKey_IsErrorOnThatInteraction()
    {
        InteractionSetDefinition set = MakeSet(
            Interaction("ok", new InteractionCondition[] { new StateValueCondition { Key = "quest.done" }, new StateValueCondition { Key = "sys.day" } }, Raises(100)),
            Interaction("broken", new InteractionCondition[]
            {
                new StateValueCondition { Key = "quest.typo" },
                new NotCondition { Inner = new StateValueCondition { Key = "bad key" } },
            }, Raises(100)));

        List<InteractionStateLinkIssue> issues = Check(set, Catalog(CoreJson));

        Assert.AreEqual(2, issues.Count, Dump(issues));
        Assert.IsTrue(issues[0].IsError);
        Assert.AreEqual(1, issues[0].InteractionIndex, "둘째 카드");
        StringAssert.StartsWith("merchant[1]: Conditions[0] — 선언되지 않은 상태 키 'quest.typo'", issues[0].Message);

        Assert.IsTrue(issues[1].IsError);
        Assert.AreEqual(1, issues[1].InteractionIndex);
        StringAssert.StartsWith("merchant[1]: Conditions[1] 안쪽 — 선언되지 않은 상태 키 'bad key'", issues[1].Message);
    }

    [Test]
    public void Check_SignalWithoutRule_IsWarning()
    {
        InteractionSetDefinition set = MakeSet(Interaction("talk", new InteractionCondition[0], Raises(100, 300)));

        List<InteractionStateLinkIssue> issues = Check(set, Catalog(CoreJson));

        Assert.AreEqual(1, issues.Count, Dump(issues));
        Assert.IsFalse(issues[0].IsError, "코드가 직접 받는 신호일 수 있어 저장을 막지 않는다");
        Assert.AreEqual(0, issues[0].InteractionIndex);
        StringAssert.StartsWith("merchant[0]: Effects[1] — 신호 300번을 받는 규칙이 없음", issues[0].Message);
    }

    [Test]
    public void Check_RuleWithoutRuleKey_ReceivesEverySignal()
    {
        InteractionSetDefinition set = MakeSet(Interaction("talk", new InteractionCondition[0], Raises(100, 300)));

        List<InteractionStateLinkIssue> issues = Check(set, Catalog(CoreJson, AnySignalJson));

        Assert.AreEqual(0, issues.Count, Dump(issues));
    }

    [Test]
    public void Check_IncompleteCatalog_SaysNothing()
    {
        InteractionSetDefinition set = MakeSet(
            Interaction("talk", new InteractionCondition[] { new StateValueCondition { Key = "quest.typo" } }, Raises(300)));

        // 읽지 못한 상태 파일이 있다 — 그 안에 키와 규칙이 있을 수 있으므로 "없다"고 하지 않는다
        var withUnreadable = new StateDefinitionCatalog();
        withUnreadable.SetSets(Parse(CoreJson), new[] { "state_extra.json: 파싱 실패" });
        Assert.IsFalse(withUnreadable.IsComplete);
        Assert.AreEqual(0, Check(set, withUnreadable).Count);

        // 한 번도 읽지 않았다 (폴더 없음과 같은 상태)
        Assert.AreEqual(0, Check(set, new StateDefinitionCatalog()).Count);
    }
    #endregion

    #region 도우미
    private static List<StateDefinitionSet> Parse(params string[] jsons)
    {
        var sets = new List<StateDefinitionSet>(jsons.Length);
        for (int i = 0; i < jsons.Length; i++)
        {
            StateDefinitionSet set = StateDefinitionLoader.ParseRaw(jsons[i], out string error);
            Assert.IsNotNull(set, error);
            sets.Add(set);
        }
        return sets;
    }

    private static StateDefinitionCatalog Catalog(params string[] jsons)
    {
        var catalog = new StateDefinitionCatalog();
        catalog.SetSets(Parse(jsons));
        return catalog;
    }

    private static List<InteractionStateLinkIssue> Check(InteractionSetDefinition set, StateDefinitionCatalog catalog)
    {
        var issues = new List<InteractionStateLinkIssue>();
        InteractionStateLinkCheck.Check(set, catalog, new InteractionReferenceScan(), issues);
        return issues;
    }

    private static InteractionSetDefinition MakeSet(params InteractionDefinition[] interactions)
    {
        return new InteractionSetDefinition { Id = "merchant", Interactions = interactions };
    }

    private static InteractionDefinition Interaction(string id, InteractionCondition[] conditions, InteractionEffect[] effects)
    {
        return new InteractionDefinition { Id = id, Conditions = conditions, EffectPrototypes = effects };
    }

    private static InteractionEffect[] Raises(params int[] signalIds)
    {
        var effects = new InteractionEffect[signalIds.Length];
        for (int i = 0; i < signalIds.Length; i++)
            effects[i] = new RaiseSignalEffect { SignalId = signalIds[i] };
        return effects;
    }

    private static string Dump(List<InteractionStateLinkIssue> issues)
    {
        var lines = new List<string>(issues.Count);
        for (int i = 0; i < issues.Count; i++)
            lines.Add((issues[i].IsError ? "✕ " : "⚠ ") + issues[i].Message);
        return "\n" + string.Join("\n", lines);
    }
    #endregion
}