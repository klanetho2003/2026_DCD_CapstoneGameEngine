using System.Collections.Generic;
using NUnit.Framework;
using static Define;

/// <summary>
/// 상태 정의 ↔ 상호작용 교차 확인 (StateInteractionCrossCheck) 테스트.
/// 경고마다 "잡아야 하는 경우"와 "잡으면 안 되는 경우"를 짝으로 둔다.
/// </summary>
public class StateInteractionCrossCheckTests
{
    private const string FilePath = "Assets/Test/merchant.json";

    // 키 2개 + 신호 100번을 받는 규칙 1개
    private const string StateJson = @"{ ""id"": ""core"",
      ""keys"": [ { ""key"": ""quest.done"", ""default"": 0 }, { ""key"": ""talk.count"", ""default"": 0 } ],
      ""rules"": [ { ""event"": ""Signal"", ""ruleKey"": 100, ""ops"": [ { ""key"": ""talk.count"", ""op"": ""Add"", ""value"": 1 } ] } ] }";

    [Test]
    public void Connected_NoWarnings()
    {
        // 선언된 키와 시스템 키를 읽고, 받는 규칙이 있는 신호를 낸다
        Result result = Run(Scan(Reads("quest.done", "sys.day"), Raises(100)), StateJson);

        Assert.AreEqual(0, result.RuleIssues.Count, Dump(result));
        Assert.AreEqual(0, result.InteractionIssues.Count, Dump(result));
    }

    #region 없는 키를 읽는 상호작용
    [Test]
    public void UndeclaredKeyRead_WarnsWithKeyAndFile()
    {
        Result result = Run(Scan(Reads("quest.done", "quest.typo"), Raises(100)), StateJson);

        Assert.AreEqual(1, result.InteractionIssues.Count, Dump(result));
        InteractionCrossIssue issue = result.InteractionIssues[0];
        Assert.AreEqual("quest.typo", issue.Key);
        Assert.AreEqual(FilePath, issue.FilePath);
        StringAssert.StartsWith(StateDefinitionLint.Tag, issue.Text);
        StringAssert.Contains("'merchant › talk' 조건 2:", issue.Text);
        StringAssert.Contains("선언되지 않은 상태 키 'quest.typo'", issue.Text);
        Assert.AreEqual(0, result.RuleIssues.Count, Dump(result));
    }

    [Test]
    public void UndeclaredKeyRead_InsideNot_IsChecked()
    {
        var conditions = new InteractionCondition[]
        {
            new NotCondition { Inner = new StateValueCondition { Key = "quest.typo" } },
        };
        Result result = Run(Scan(conditions, Raises(100)), StateJson);

        Assert.AreEqual(1, result.InteractionIssues.Count, Dump(result));
        StringAssert.Contains("조건 1 안쪽:", result.InteractionIssues[0].Text);
    }

    [Test]
    public void KeyRejectedByRegistry_CountsAsUndeclared()
    {
        // 이름 규칙에 어긋난 선언은 게임이 등록하지 않는다 — 그 키를 읽는 상호작용도 로드에 실패한다
        const string state = @"{ ""id"": ""bad"", ""keys"": [ { ""key"": ""bad key"", ""default"": 0 } ] }";
        Result result = Run(Scan(Reads("bad key"), Raises()), state);

        Assert.AreEqual(1, result.InteractionIssues.Count, Dump(result));
        Assert.AreEqual("bad key", result.InteractionIssues[0].Key);
    }
    #endregion

    #region 신호 짝
    [Test]
    public void SignalWithoutRule_Warns()
    {
        Result result = Run(Scan(Reads(), Raises(100, 200)), StateJson);

        Assert.AreEqual(1, result.InteractionIssues.Count, Dump(result));
        InteractionCrossIssue issue = result.InteractionIssues[0];
        Assert.IsNull(issue.Key, "신호에 대한 경고는 키가 없다");
        Assert.AreEqual(FilePath, issue.FilePath);
        StringAssert.Contains("효과 2:", issue.Text);
        StringAssert.Contains("신호 200번을 받는 규칙이 없음", issue.Text);
    }

    [Test]
    public void RuleWithoutRuleKey_ReceivesEverySignal()
    {
        // "특정 값만"이 꺼진 신호 규칙은 모든 신호를 받는다
        const string any = @"{ ""id"": ""any"", ""keys"": [ { ""key"": ""signal.count"", ""default"": 0 } ],
          ""rules"": [ { ""event"": ""Signal"", ""ops"": [ { ""key"": ""signal.count"", ""op"": ""Add"", ""value"": 1 } ] } ] }";
        Result result = Run(Scan(Reads(), Raises(100, 200)), StateJson, any);

        Assert.AreEqual(0, result.InteractionIssues.Count, Dump(result));
        Assert.AreEqual(0, result.RuleIssues.Count, Dump(result)); // 그런 규칙에는 "내는 상호작용이 없음"도 묻지 않는다
    }

    [Test]
    public void SignalRuleWithoutSender_WarnsOnThatRule()
    {
        // 개체 사망 규칙의 ruleKey 300은 templateID다 — 신호가 아니므로 경고 대상이 아니다
        const string extra = @"{ ""id"": ""extra"", ""keys"": [ { ""key"": ""door.open"", ""default"": 0 } ],
          ""rules"": [
            { ""event"": ""CreatureDied"", ""ruleKey"": 300, ""ops"": [ { ""key"": ""door.open"", ""op"": ""Set"", ""value"": 1 } ] },
            { ""event"": ""Signal"",       ""ruleKey"": 300, ""ops"": [ { ""key"": ""door.open"", ""op"": ""Set"", ""value"": 1 } ] } ] }";
        Result result = Run(Scan(Reads(), Raises(100)), StateJson, extra);

        Assert.AreEqual(1, result.RuleIssues.Count, Dump(result));
        StateDefinitionIssue issue = result.RuleIssues[0];
        Assert.IsFalse(issue.IsError, "툴 경고는 저장을 막지 않는다");
        Assert.AreEqual(1, issue.SetIndex, "두 번째 파일");
        Assert.AreEqual(EStateDefinitionSection.Rule, issue.Section);
        Assert.AreEqual(1, issue.ItemIndex);
        Assert.AreEqual(-1, issue.SubIndex);
        StringAssert.StartsWith(StateDefinitionLint.Tag, issue.Text);
        StringAssert.Contains("extra.rules[1]: 신호 300번을 내는 상호작용이 없음", issue.Text);
        Assert.AreEqual(0, result.InteractionIssues.Count, Dump(result));
    }
    #endregion

    #region 근거가 없으면 경고하지 않는다
    [Test]
    public void NoInteractionFolder_NoWarnings()
    {
        // 폴더를 읽지 못한 상태 — 신호 100번을 내는 곳이 "없다"고 말할 근거가 없다
        Result result = Run(new InteractionReferenceScan(), StateJson);

        Assert.AreEqual(0, result.RuleIssues.Count, Dump(result));
        Assert.AreEqual(0, result.InteractionIssues.Count, Dump(result));
    }

    [Test]
    public void UnreadableFile_SkipsSenderCheck_AndSaysSo()
    {
        InteractionReferenceScan scan = Scan(Reads(), Raises()); // 읽은 파일에는 신호 100번을 내는 곳이 없다
        scan.UnreadableFiles.Add("guard.json: 파싱 실패");

        Result result = Run(scan, StateJson);

        Assert.AreEqual(0, result.RuleIssues.Count, "읽지 못한 파일이 신호를 낼 수도 있으므로 '없음'이라고 하지 않는다");
        Assert.AreEqual(1, result.InteractionIssues.Count, Dump(result));
        StringAssert.Contains("읽지 못한 상호작용 파일 1개", result.InteractionIssues[0].Text);
        StringAssert.Contains("guard.json", result.InteractionIssues[0].Detail);
        Assert.IsNull(result.InteractionIssues[0].FilePath);
    }
    #endregion

    [Test]
    public void DescribeSenders_ListsInteractions_UpToMax()
    {
        var scan = new InteractionReferenceScan { FolderExists = true };
        InteractionReferenceScanner.Collect(MakeSet("merchant", "talk", Reads(), Raises(100, 100)), "a.json", scan); // 같은 상호작용이 두 번 → 한 번만 적는다
        InteractionReferenceScanner.Collect(MakeSet("guard", "alarm", Reads(), Raises(100)), "b.json", scan);
        InteractionReferenceScanner.Collect(MakeSet("door", "open", Reads(), Raises(7, 100)), "c.json", scan);

        Assert.AreEqual("merchant › talk, guard › alarm, door › open", StateInteractionCrossCheck.DescribeSenders(scan, 100, 3));
        Assert.AreEqual("merchant › talk, guard › alarm 외 1곳", StateInteractionCrossCheck.DescribeSenders(scan, 100, 2));
        Assert.AreEqual("door › open", StateInteractionCrossCheck.DescribeSenders(scan, 7, 3));
        Assert.IsNull(StateInteractionCrossCheck.DescribeSenders(scan, 999, 3));
    }

    #region 도우미
    private sealed class Result
    {
        public readonly List<StateDefinitionIssue> RuleIssues = new List<StateDefinitionIssue>();
        public readonly List<InteractionCrossIssue> InteractionIssues = new List<InteractionCrossIssue>();
    }

    /// <summary>창과 같은 방식으로 실행한다: 검사기가 채운 등록소를 교차 확인에 넘긴다.</summary>
    private static Result Run(InteractionReferenceScan scan, params string[] stateJsons)
    {
        var sets = new List<StateDefinitionSet>(stateJsons.Length);
        for (int i = 0; i < stateJsons.Length; i++)
        {
            StateDefinitionSet set = StateDefinitionLoader.ParseRaw(stateJsons[i], out string error);
            Assert.IsNotNull(set, error);
            sets.Add(set);
        }

        var registry = new StateKeyRegistry();
        GameStateManager.RegisterSystemKeys(registry);
        StateDefinitionValidator.Validate(sets, registry, new List<StateDefinitionIssue>(), null);

        var result = new Result();
        StateInteractionCrossCheck.Check(sets, registry, scan, result.RuleIssues, result.InteractionIssues);
        return result;
    }

    /// <summary>상호작용 Set 'merchant'의 상호작용 'talk' 하나를 읽은 스캔 결과 (폴더는 읽힌 것으로 둔다).</summary>
    private static InteractionReferenceScan Scan(InteractionCondition[] conditions, InteractionEffect[] effects)
    {
        var scan = new InteractionReferenceScan { FolderExists = true };
        InteractionReferenceScanner.Collect(MakeSet("merchant", "talk", conditions, effects), FilePath, scan);
        return scan;
    }

    private static InteractionSetDefinition MakeSet(string setId, string interactionId, InteractionCondition[] conditions, InteractionEffect[] effects)
    {
        return new InteractionSetDefinition
        {
            Id = setId,
            Interactions = new[]
            {
                new InteractionDefinition { Id = interactionId, Conditions = conditions, EffectPrototypes = effects },
            },
        };
    }

    private static InteractionCondition[] Reads(params string[] keys)
    {
        var conditions = new InteractionCondition[keys.Length];
        for (int i = 0; i < keys.Length; i++)
            conditions[i] = new StateValueCondition { Key = keys[i] };
        return conditions;
    }

    private static InteractionEffect[] Raises(params int[] signalIds)
    {
        var effects = new InteractionEffect[signalIds.Length];
        for (int i = 0; i < signalIds.Length; i++)
            effects[i] = new RaiseSignalEffect { SignalId = signalIds[i] };
        return effects;
    }

    private static string Dump(Result result)
    {
        var lines = new List<string>();
        for (int i = 0; i < result.RuleIssues.Count; i++)
            lines.Add("규칙: " + result.RuleIssues[i].Text);
        for (int i = 0; i < result.InteractionIssues.Count; i++)
            lines.Add("상호작용: " + result.InteractionIssues[i].Text);
        return "\n" + string.Join("\n", lines);
    }
    #endregion
}