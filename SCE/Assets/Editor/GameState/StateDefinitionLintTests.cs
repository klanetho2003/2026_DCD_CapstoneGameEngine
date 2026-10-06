using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 툴 경고(StateDefinitionLint) 테스트.
/// 경고마다 "잡아야 하는 경우"와 "잡으면 안 되는 경우"를 짝으로 둔다 — 거짓 경고가 쌓이면 기획자가 경고 자체를 무시하게 된다.
/// </summary>
public class StateDefinitionLintTests
{
    // state_core.json에서 경고가 나야 하는 부분만 추린 것: ruleKey 0 두 건 + 매일 초기화 키를 일차 진행에서 기본값으로 Set
    private const string CoreLike = @"{
      ""id"": ""core"",
      ""keys"": [
        { ""key"": ""kill.slime"",          ""default"": 0 },
        { ""key"": ""ach.slime10"",         ""default"": 0 },
        { ""key"": ""daily.shop.visit"",    ""default"": 0, ""reset"": ""EveryDay"" },
        { ""key"": ""daily.festival.show"", ""default"": 0, ""reset"": ""EveryDay"" }
      ],
      ""rules"": [
        { ""event"": ""CreatureDied"", ""ruleKey"": 0, ""ops"": [ { ""key"": ""kill.slime"", ""op"": ""Add"", ""value"": 1 } ] },
        { ""event"": ""CreatureDied"", ""ruleKey"": 0,
          ""require"": [ { ""key"": ""kill.slime"", ""value"": 10 } ],
          ""ops"": [ { ""key"": ""ach.slime10"", ""op"": ""Max"", ""value"": 1 } ] },
        { ""event"": ""DayAdvanced"", ""ruleKey"": 5, ""ops"": [ { ""key"": ""daily.festival.show"", ""op"": ""Set"", ""value"": 1 } ] },
        { ""event"": ""DayAdvanced"",
          ""require"": [ { ""key"": ""sys.day"", ""value"": 3 } ],
          ""ops"": [ { ""key"": ""daily.shop.visit"", ""op"": ""Set"", ""value"": 0 } ] }
      ]
    }";

    [Test]
    public void CoreLike_ReportsExactlyThreeWarnings()
    {
        List<StateDefinitionIssue> issues = Lint(CoreLike);
        Assert.AreEqual(3, issues.Count, Dump(issues));

        AssertWarning(issues[0], EStateDefinitionSection.Rule, 0, -1, "ruleKey 0");
        AssertWarning(issues[1], EStateDefinitionSection.Rule, 1, -1, "ruleKey 0");
        AssertWarning(issues[2], EStateDefinitionSection.Op, 3, 0, "효과 없음");
    }

    #region ruleKey
    [Test]
    public void CreatureDied_RuleKeyOmitted_NoWarning()
    {
        const string json = @"{ ""id"": ""t"", ""keys"": [ { ""key"": ""kill.any"", ""default"": 0 } ],
          ""rules"": [ { ""event"": ""CreatureDied"", ""ops"": [ { ""key"": ""kill.any"", ""op"": ""Add"", ""value"": 1 } ] } ] }";

        List<StateDefinitionIssue> issues = Lint(json);
        Assert.AreEqual(0, issues.Count, Dump(issues));
    }

    [Test]
    public void DayAdvanced_RuleKeyOne_NeverFires_TwoIsFine()
    {
        const string json = @"{ ""id"": ""t"", ""keys"": [ { ""key"": ""flag"", ""default"": 0 } ],
          ""rules"": [
            { ""event"": ""DayAdvanced"", ""ruleKey"": 1, ""ops"": [ { ""key"": ""flag"", ""op"": ""Set"", ""value"": 1 } ] },
            { ""event"": ""DayAdvanced"", ""ruleKey"": 2, ""ops"": [ { ""key"": ""flag"", ""op"": ""Set"", ""value"": 2 } ] } ] }";

        List<StateDefinitionIssue> issues = Lint(json);
        Assert.AreEqual(1, issues.Count, Dump(issues));
        AssertWarning(issues[0], EStateDefinitionSection.Rule, 0, -1, "실행되지 않음");
    }
    #endregion

    #region 효과 없는 op (일차 진행 + 매일 초기화 키)
    [Test]
    public void EveryDayKey_SetToNonDefault_IsUseful()
    {
        // 정리본의 "5일차 축제 공연" — 초기화 뒤에 켜므로 의미가 있다
        const string json = @"{ ""id"": ""t"", ""keys"": [ { ""key"": ""daily"", ""default"": 0, ""reset"": ""EveryDay"" } ],
          ""rules"": [ { ""event"": ""DayAdvanced"", ""ruleKey"": 5, ""ops"": [ { ""key"": ""daily"", ""op"": ""Set"", ""value"": 1 } ] } ] }";

        List<StateDefinitionIssue> issues = Lint(json);
        Assert.AreEqual(0, issues.Count, Dump(issues));
    }

    [Test]
    public void EveryDayKey_ResetAfterEarlierWrite_IsUseful()
    {
        // 같은 사건에서 앞 규칙이 바꾼 값을 되돌리는 것이라 효과가 있다
        const string json = @"{ ""id"": ""t"", ""keys"": [ { ""key"": ""daily"", ""default"": 0, ""reset"": ""EveryDay"" } ],
          ""rules"": [
            { ""event"": ""DayAdvanced"", ""ops"": [ { ""key"": ""daily"", ""op"": ""Set"", ""value"": 1 } ] },
            { ""event"": ""DayAdvanced"", ""ops"": [ { ""key"": ""daily"", ""op"": ""Set"", ""value"": 0 } ] } ] }";

        List<StateDefinitionIssue> issues = Lint(json);
        Assert.AreEqual(0, issues.Count, Dump(issues));
    }

    [Test]
    public void EveryDayKey_EarlierWriteForAnotherDay_StillWarns()
    {
        // 5일차에 켠 값은 6일차 초기화로 이미 사라진다 — 7일차 규칙의 "0으로"는 효과가 없다
        const string json = @"{ ""id"": ""t"", ""keys"": [ { ""key"": ""daily"", ""default"": 0, ""reset"": ""EveryDay"" } ],
          ""rules"": [
            { ""event"": ""DayAdvanced"", ""ruleKey"": 5, ""ops"": [ { ""key"": ""daily"", ""op"": ""Set"", ""value"": 1 } ] },
            { ""event"": ""DayAdvanced"", ""ruleKey"": 7, ""ops"": [ { ""key"": ""daily"", ""op"": ""Set"", ""value"": 0 } ] } ] }";

        List<StateDefinitionIssue> issues = Lint(json);
        Assert.AreEqual(1, issues.Count, Dump(issues));
        AssertWarning(issues[0], EStateDefinitionSection.Op, 1, 0, "효과 없음");
    }

    [Test]
    public void NoEffect_KeyDeclaredInOtherFile_StillDetected()
    {
        const string keysFile = @"{ ""id"": ""keys"", ""keys"": [ { ""key"": ""daily"", ""default"": 0, ""reset"": ""EveryDay"" } ] }";
        const string rulesFile = @"{ ""id"": ""rules"", ""rules"": [ { ""event"": ""DayAdvanced"", ""ops"": [ { ""key"": ""daily"", ""op"": ""Set"", ""value"": 0 } ] } ] }";

        List<StateDefinitionIssue> issues = Lint(keysFile, rulesFile);
        Assert.AreEqual(1, issues.Count, Dump(issues));
        Assert.AreEqual(1, issues[0].SetIndex, "규칙이 있는 두 번째 파일");
        AssertWarning(issues[0], EStateDefinitionSection.Op, 0, 0, "효과 없음");
    }
    #endregion

    #region 실행 순서 함정
    [Test]
    public void RequireReadsKeyWrittenByLaterRule_Warns()
    {
        // 업적 규칙이 카운트 규칙보다 위에 있어 한 박자 늦게 켜지는 경우
        const string json = @"{ ""id"": ""t"",
          ""keys"": [ { ""key"": ""kill"", ""default"": 0 }, { ""key"": ""ach"", ""default"": 0 } ],
          ""rules"": [
            { ""event"": ""CreatureDied"", ""require"": [ { ""key"": ""kill"", ""value"": 3 } ], ""ops"": [ { ""key"": ""ach"", ""op"": ""Max"", ""value"": 1 } ] },
            { ""event"": ""CreatureDied"", ""ops"": [ { ""key"": ""kill"", ""op"": ""Add"", ""value"": 1 } ] } ] }";

        List<StateDefinitionIssue> issues = Lint(json);
        Assert.AreEqual(1, issues.Count, Dump(issues));
        AssertWarning(issues[0], EStateDefinitionSection.Require, 0, 0, "t.rules[1]");
    }

    [Test]
    public void RequireAndWriteInSameRule_NoWarning()
    {
        // require는 항상 자기 ops보다 먼저 평가된다 — "3번까지만 센다" 같은 자기 제한 규칙
        const string json = @"{ ""id"": ""t"", ""keys"": [ { ""key"": ""count"", ""default"": 0 } ],
          ""rules"": [ { ""event"": ""Signal"", ""ruleKey"": 7,
            ""require"": [ { ""key"": ""count"", ""cmp"": ""Less"", ""value"": 3 } ],
            ""ops"": [ { ""key"": ""count"", ""op"": ""Add"", ""value"": 1 } ] } ] }";

        List<StateDefinitionIssue> issues = Lint(json);
        Assert.AreEqual(0, issues.Count, Dump(issues));
    }

    [Test]
    public void DifferentRuleKeys_DoNotShareAnEvent()
    {
        // templateID 100의 죽음과 200의 죽음은 다른 사건 — 순서가 서로 영향을 주지 않는다
        const string json = @"{ ""id"": ""t"",
          ""keys"": [ { ""key"": ""kill"", ""default"": 0 }, { ""key"": ""ach"", ""default"": 0 } ],
          ""rules"": [
            { ""event"": ""CreatureDied"", ""ruleKey"": 100, ""require"": [ { ""key"": ""kill"", ""value"": 3 } ], ""ops"": [ { ""key"": ""ach"", ""op"": ""Max"", ""value"": 1 } ] },
            { ""event"": ""CreatureDied"", ""ruleKey"": 200, ""ops"": [ { ""key"": ""kill"", ""op"": ""Add"", ""value"": 1 } ] } ] }";

        List<StateDefinitionIssue> issues = Lint(json);
        Assert.AreEqual(0, issues.Count, Dump(issues));
    }
    #endregion

    [Test]
    public void DuplicateFileId_WarnsOnSecondFile()
    {
        const string a = @"{ ""id"": ""same"", ""keys"": [ { ""key"": ""x"", ""default"": 0 } ] }";
        const string b = @"{ ""id"": ""same"", ""keys"": [ { ""key"": ""y"", ""default"": 0 } ] }";

        List<StateDefinitionIssue> issues = Lint(a, b);
        Assert.AreEqual(1, issues.Count, Dump(issues));
        Assert.AreEqual(1, issues[0].SetIndex);
        AssertWarning(issues[0], EStateDefinitionSection.File, -1, -1, "다른 파일과 같음");
    }

    #region 도우미
    private static List<StateDefinitionIssue> Lint(params string[] jsons)
    {
        var sets = new List<StateDefinitionSet>(jsons.Length);
        for (int i = 0; i < jsons.Length; i++)
        {
            StateDefinitionSet set = StateDefinitionLoader.ParseRaw(jsons[i], out string error);
            Assert.IsNotNull(set, error);
            sets.Add(set);
        }

        var issues = new List<StateDefinitionIssue>();
        StateDefinitionLint.Check(sets, issues);
        return issues;
    }

    private static void AssertWarning(StateDefinitionIssue issue, EStateDefinitionSection section, int itemIndex, int subIndex, string fragment)
    {
        Assert.IsFalse(issue.IsError, issue.Text);
        StringAssert.StartsWith(StateDefinitionLint.Tag, issue.Text);
        Assert.AreEqual(section, issue.Section, issue.Text);
        Assert.AreEqual(itemIndex, issue.ItemIndex, issue.Text);
        Assert.AreEqual(subIndex, issue.SubIndex, issue.Text);
        StringAssert.Contains(fragment, issue.Text);
    }

    private static string Dump(List<StateDefinitionIssue> issues)
    {
        var lines = new List<string>(issues.Count);
        for (int i = 0; i < issues.Count; i++)
            lines.Add((issues[i].IsError ? "✕ " : "⚠ ") + issues[i].Text);
        return "\n" + string.Join("\n", lines);
    }
    #endregion
}