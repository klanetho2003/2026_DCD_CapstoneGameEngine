using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Define;

/// <summary>
/// Step 1 회귀 테스트.
/// 1) 저장 형식 — ruleKey 생략과 0의 구분, 필드 순서, 빈 값 생략, 왕복 후 의미 동일
/// 2) 검사기 — 문제마다 정확한 위치, 런타임 로그 문장 = 검사 결과 문장
/// </summary>
public class StateDefinitionValidatorTests
{
    // state_core.json에서 형태별로 하나씩 뽑은 고정 데이터
    private const string CoreJson = @"{
      ""id"": ""core"",
      ""keys"": [
        { ""key"": ""kill.slime"",         ""default"": 0, ""reset"": ""None"",       ""desc"": ""슬라임 처치 누적"" },
        { ""key"": ""ach.slime10"",        ""default"": 0, ""reset"": ""None"",       ""desc"": ""업적: 슬라임 10마리"" },
        { ""key"": ""daily.shop.visit"",   ""default"": 0, ""reset"": ""EveryDay"",   ""desc"": ""오늘 상점 방문 횟수"" },
        { ""key"": ""weekly.bounty.done"", ""default"": 0, ""reset"": ""EveryNDays"", ""interval"": 7, ""desc"": ""주간 현상금"" },
        { ""key"": ""kill.boss.golem"",    ""default"": 0, ""desc"": ""reset 생략"" }
      ],
      ""rules"": [
        { ""event"": ""CreatureDied"", ""ruleKey"": 0, ""desc"": ""ruleKey 0"",
          ""ops"": [ { ""key"": ""kill.slime"", ""op"": ""Add"", ""value"": 1 } ] },
        { ""event"": ""CreatureDied"", ""ruleKey"": 0, ""desc"": ""업적"",
          ""require"": [ { ""key"": ""kill.slime"", ""cmp"": ""GreaterOrEqual"", ""value"": 10 } ],
          ""ops"": [ { ""key"": ""ach.slime10"", ""op"": ""Max"", ""value"": 1 } ] },
        { ""event"": ""DayAdvanced"", ""desc"": ""ruleKey 생략 + 시스템 키 읽기"",
          ""require"": [ { ""key"": ""sys.day"", ""cmp"": ""GreaterOrEqual"", ""value"": 3 } ],
          ""ops"": [ { ""key"": ""daily.shop.visit"", ""op"": ""Set"", ""value"": 0 } ] },
        { ""event"": ""Signal"", ""ruleKey"": 100,
          ""ops"": [ { ""key"": ""kill.boss.golem"", ""op"": ""Max"", ""value"": 1 } ] }
      ]
    }";

    // 검사 항목마다 하나씩 일부러 틀린 데이터. 기대 결과는 BadJson_ReportsEachProblemWithLocation 참고
    private const string BadJson = @"{
      ""id"": ""bad"",
      ""keys"": [
        { ""key"": ""ok.key"",  ""default"": 0 },
        { ""key"": ""bad key"", ""default"": 0 },
        { ""key"": ""ok.key"",  ""default"": 0 },
        { ""key"": ""weekly"",  ""default"": 0, ""reset"": ""EveryNDays"" },
        { ""key"": ""daily"",   ""default"": 0, ""reset"": ""EveryDay"", ""interval"": 3 }
      ],
      ""rules"": [
        { ""event"": ""Signal"", ""ruleKey"": 1,
          ""require"": [ { ""key"": ""typo"", ""value"": 1 } ],
          ""ops"": [ { ""key"": ""ok.key"", ""op"": ""Add"", ""value"": 0 } ] },
        { ""event"": ""Signal"", ""ops"": [ { ""key"": ""sys.day"", ""op"": ""Set"", ""value"": 9 } ] },
        { ""event"": ""Signal"", ""ops"": [ { ""key"": ""weekly"", ""op"": ""Set"", ""value"": 1 } ] },
        { ""event"": ""None"", ""ops"": [] },
        { ""event"": ""DayAdvanced"" }
      ]
    }";

    #region 저장 형식
    [Test]
    public void RuleKey_OmittedAndZero_StayDistinct_AfterSave()
    {
        StateDefinitionSet set = ParseOrFail(CoreJson);

        Assert.IsTrue(set.Rules[0].HasRuleKey, "\"ruleKey\": 0 은 지정된 것");
        Assert.AreEqual(0, set.Rules[0].RuleKeyValue);
        Assert.IsFalse(set.Rules[2].HasRuleKey, "생략은 모든 이벤트");
        Assert.AreEqual(100, set.Rules[3].RuleKeyValue);

        StateDefinitionSet again = ParseOrFail(StateDefinitionLoader.Serialize(set));
        Assert.IsTrue(again.Rules[0].HasRuleKey);
        Assert.AreEqual(0, again.Rules[0].RuleKeyValue);
        Assert.IsFalse(again.Rules[2].HasRuleKey);
    }

    [Test]
    public void RuleKey_SurvivesUnitySerialization()
    {
        // 에디터 툴은 Unity 직렬화(SerializedObject)로 편집한다 — JsonUtility가 같은 직렬화 규칙을 쓴다
        var zero = new StateRuleDefinition { Event = EGameEventType.CreatureDied, RuleKey = 0 };
        var omitted = new StateRuleDefinition { Event = EGameEventType.CreatureDied, RuleKey = null };

        StateRuleDefinition zeroCopy = JsonUtility.FromJson<StateRuleDefinition>(JsonUtility.ToJson(zero));
        StateRuleDefinition omittedCopy = JsonUtility.FromJson<StateRuleDefinition>(JsonUtility.ToJson(omitted));

        Assert.IsTrue(zeroCopy.HasRuleKey);
        Assert.AreEqual(0, zeroCopy.RuleKeyValue);
        Assert.IsFalse(omittedCopy.HasRuleKey);
        Assert.IsNull(omittedCopy.RuleKey);
    }

    [Test]
    public void Serialize_KeepsHandWrittenOrder_AndOmitsEmptyValues()
    {
        JObject root = JObject.Parse(StateDefinitionLoader.Serialize(ParseOrFail(CoreJson)));

        CollectionAssert.AreEqual(new[] { "id", "keys", "rules" }, Names(root));
        CollectionAssert.AreEqual(new[] { "key", "default", "reset", "interval", "desc" }, Names(root["keys"][3])); // EveryNDays
        CollectionAssert.AreEqual(new[] { "key", "default", "reset", "desc" }, Names(root["keys"][0]));             // interval 0 생략
        CollectionAssert.AreEqual(new[] { "key", "default", "reset", "desc" }, Names(root["keys"][4]));             // reset 생략 → None 명시
        CollectionAssert.AreEqual(new[] { "event", "ruleKey", "desc", "require", "ops" }, Names(root["rules"][1]));
        CollectionAssert.AreEqual(new[] { "event", "ruleKey", "desc", "ops" }, Names(root["rules"][0]));            // 빈 require 생략
        CollectionAssert.AreEqual(new[] { "event", "desc", "require", "ops" }, Names(root["rules"][2]));            // 생략된 ruleKey는 생략 유지
        CollectionAssert.AreEqual(new[] { "event", "ruleKey", "ops" }, Names(root["rules"][3]));                    // 빈 desc 생략
        CollectionAssert.AreEqual(new[] { "key", "cmp", "value" }, Names(root["rules"][1]["require"][0]));
        CollectionAssert.AreEqual(new[] { "key", "op", "value" }, Names(root["rules"][1]["ops"][0]));

        Assert.AreEqual("EveryNDays", (string)root["keys"][3]["reset"]); // enum은 이름으로
        Assert.AreEqual("None", (string)root["keys"][4]["reset"]);
    }

    [Test]
    public void RoundTrip_PreservesMeaning()
    {
        StateDefinitionSet original = ParseOrFail(CoreJson);
        string saved = StateDefinitionLoader.Serialize(original);
        StateDefinitionSet loaded = ParseOrFail(saved);

        AssertSameMeaning(original, loaded);
        Assert.AreEqual(saved, StateDefinitionLoader.Serialize(loaded), "같은 내용은 같은 텍스트로 저장 (불필요한 변경 방지)");
    }

    [Test]
    public void Clone_IsIndependentCopy()
    {
        StateDefinitionSet set = ParseOrFail(CoreJson);

        StateRuleDefinition rule = StateDefinitionLoader.CloneRule(set.Rules[1]);
        rule.Require[0].Value = 99;
        rule.RuleKey = null;
        Assert.AreEqual(10, set.Rules[1].Require[0].Value, "원본 require가 바뀌면 안 됨");
        Assert.IsTrue(set.Rules[1].HasRuleKey);
        Assert.IsNotNull(StateDefinitionLoader.CloneRule(set.Rules[0]).Require, "빈 require는 null이 아닌 빈 배열");

        StateKeyDefinition key = StateDefinitionLoader.CloneKey(set.Keys[3]);
        Assert.AreEqual(7, key.Interval);
        key.Key = "changed";
        Assert.AreEqual("weekly.bounty.done", set.Keys[3].Key);
    }

    [Test]
    public void ParseRaw_ReturnsReasonWithoutLog_ParseKeepsLegacyLog()
    {
        const string typo = @"{ ""id"": ""x"", ""keys"": [ { ""key"": ""a"", ""defualt"": 0 } ] }"; // default 오타

        Assert.IsNull(StateDefinitionLoader.ParseRaw(typo, out string error));
        StringAssert.StartsWith("JSON 파싱 실패 >> ", error);
        LogAssert.NoUnexpectedReceived(); // ParseRaw는 로그를 남기지 않는다

        LogAssert.Expect(LogType.Error, LogContaining("[StateDefinitionLoader] JSON 파싱 실패 >> "));
        Assert.IsNull(StateDefinitionLoader.Parse(typo));
    }
    #endregion

    #region 검사기
    [Test]
    public void CoreFixture_HasNoIssues()
    {
        List<StateDefinitionIssue> issues = ValidateJson(CoreJson);
        Assert.AreEqual(0, issues.Count, Dump(issues)); // sys.day 읽기 포함 — 툴의 검사도 시스템 키를 안다
    }

    [Test]
    public void BadJson_ReportsEachProblemWithLocation()
    {
        List<StateDefinitionIssue> issues = ValidateJson(BadJson);
        Assert.AreEqual(10, issues.Count, Dump(issues));

        AssertIssue(issues[0], true, EStateDefinitionSection.Key, 1, -1, "허용되지 않는 문자");
        AssertIssue(issues[1], true, EStateDefinitionSection.Key, 2, -1, "키 중복");
        AssertIssue(issues[2], true, EStateDefinitionSection.Key, 3, -1, "interval 1 이상 필요");
        AssertIssue(issues[3], false, EStateDefinitionSection.Key, 4, -1, "EveryNDays일 때만");
        AssertIssue(issues[4], true, EStateDefinitionSection.Require, 0, 0, "선언되지 않은 키 'typo'");
        AssertIssue(issues[5], false, EStateDefinitionSection.Op, 0, 0, "Add 0");
        AssertIssue(issues[6], true, EStateDefinitionSection.Op, 1, 0, "시스템 키");
        AssertIssue(issues[7], true, EStateDefinitionSection.Op, 2, 0, "선언되지 않은 키 'weekly'"); // 선언이 거부된 키 = 없는 키
        AssertIssue(issues[8], true, EStateDefinitionSection.Rule, 3, -1, "event 지정 필요");
        AssertIssue(issues[9], false, EStateDefinitionSection.Rule, 4, -1, "ops 없음");
    }

    [Test]
    public void RuntimeLog_EqualsValidatorText()
    {
        List<StateDefinitionIssue> issues = ValidateJson(BadJson);

        // 에러 로그 = "[GameStateManager] " + 검사 결과 문장, 같은 순서 (경고도 같은 함수로 찍힌다)
        for (int i = 0; i < issues.Count; i++)
        {
            if (issues[i].IsError)
                LogAssert.Expect(LogType.Error, LogContaining("[GameStateManager] " + issues[i].Text));
        }

        var state = new GameStateManager();
        Assert.IsFalse(state.LoadDefinitions(new[] { BadJson }));
        Assert.AreEqual(3, state.KeyCount, "sys.day + ok.key + daily");
        Assert.AreEqual(1, state.RuleCount, "에러 없는 rules[4]만 변환");
    }

    [Test]
    public void DuplicateAcrossFiles_SecondDeclarationReported()
    {
        const string a = @"{ ""id"": ""a"", ""keys"": [ { ""key"": ""shared"", ""default"": 0 } ] }";
        const string b = @"{ ""id"": ""b"", ""keys"": [ { ""key"": ""x"", ""default"": 0 }, { ""key"": ""shared"", ""default"": 5 } ] }";

        List<StateDefinitionIssue> issues = ValidateJson(a, b);
        Assert.AreEqual(1, issues.Count, Dump(issues));
        Assert.AreEqual(1, issues[0].SetIndex, "두 번째 파일");
        AssertIssue(issues[0], true, EStateDefinitionSection.Key, 1, -1, "키 중복");
    }

    [Test]
    public void ReservedPrefix_DeclarationRejected()
    {
        const string json = @"{ ""id"": ""f"", ""keys"": [ { ""key"": ""sys.custom"", ""default"": 0 } ] }";

        List<StateDefinitionIssue> issues = ValidateJson(json);
        Assert.AreEqual(1, issues.Count, Dump(issues));
        AssertIssue(issues[0], true, EStateDefinitionSection.Key, 0, -1, "시스템 예약");
    }

    [Test]
    public void EmptyId_FileError_AndItsKeysAreNotDeclared()
    {
        const string noId = @"{ ""id"": """", ""keys"": [ { ""key"": ""k"", ""default"": 0 } ] }";
        const string user = @"{ ""id"": ""user"", ""rules"": [ { ""event"": ""Signal"", ""ops"": [ { ""key"": ""k"", ""op"": ""Set"", ""value"": 1 } ] } ] }";

        List<StateDefinitionIssue> issues = ValidateJson(noId, user);
        Assert.AreEqual(2, issues.Count, Dump(issues));
        Assert.AreEqual(0, issues[0].SetIndex);
        AssertIssue(issues[0], true, EStateDefinitionSection.File, -1, -1, "id 비어 있음");
        Assert.AreEqual(1, issues[1].SetIndex);
        AssertIssue(issues[1], true, EStateDefinitionSection.Op, 0, 0, "선언되지 않은 키 'k'");

        // 게임도 같은 결과 — 로더가 파일 전체를 거부한다
        LogAssert.Expect(LogType.Error, LogContaining("[StateDefinitionLoader] id 비어 있음"));
        Assert.IsNull(StateDefinitionLoader.Parse(noId));
    }

    [Test]
    public void RuleKeyZero_MatchesOnlyZero_AtRuntime()
    {
        const string json = @"{ ""id"": ""z"",
          ""keys"": [ { ""key"": ""zero"", ""default"": 0 }, { ""key"": ""any"", ""default"": 0 } ],
          ""rules"": [ { ""event"": ""Signal"", ""ruleKey"": 0, ""ops"": [ { ""key"": ""zero"", ""op"": ""Add"", ""value"": 1 } ] },
                       { ""event"": ""Signal"", ""ops"": [ { ""key"": ""any"", ""op"": ""Add"", ""value"": 1 } ] } ] }";
        var state = new GameStateManager();
        Assert.IsTrue(state.LoadDefinitions(new[] { json }));

        state.OnEventHeader(EGameEventType.Signal, 0, null);
        state.OnEventHeader(EGameEventType.Signal, 7, null);

        state.TryGet("zero", out int zero);
        state.TryGet("any", out int any);
        Assert.AreEqual(1, zero, "ruleKey 0 = 0번 신호에만");
        Assert.AreEqual(2, any, "ruleKey 생략 = 모든 신호");
    }
    #endregion

    #region 도우미
    private static StateDefinitionSet ParseOrFail(string json)
    {
        StateDefinitionSet set = StateDefinitionLoader.ParseRaw(json, out string error);
        Assert.IsNotNull(set, error);
        return set;
    }

    private static List<StateDefinitionIssue> ValidateJson(params string[] jsons)
    {
        var sets = new List<StateDefinitionSet>(jsons.Length);
        for (int i = 0; i < jsons.Length; i++)
            sets.Add(ParseOrFail(jsons[i]));

        var issues = new List<StateDefinitionIssue>();
        StateDefinitionValidator.Validate(sets, issues);
        return issues;
    }

    private static void AssertIssue(StateDefinitionIssue issue, bool isError, EStateDefinitionSection section, int itemIndex, int subIndex, string fragment)
    {
        Assert.AreEqual(isError, issue.IsError, issue.Text);
        Assert.AreEqual(section, issue.Section, issue.Text);
        Assert.AreEqual(itemIndex, issue.ItemIndex, issue.Text);
        Assert.AreEqual(subIndex, issue.SubIndex, issue.Text);
        StringAssert.Contains(fragment, issue.Text);
    }

    private static void AssertSameMeaning(StateDefinitionSet expected, StateDefinitionSet actual)
    {
        Assert.AreEqual(expected.Id, actual.Id);

        Assert.AreEqual(expected.Keys.Length, actual.Keys.Length, "keys 수");
        for (int i = 0; i < expected.Keys.Length; i++)
        {
            StateKeyDefinition e = expected.Keys[i];
            StateKeyDefinition a = actual.Keys[i];
            string at = $"keys[{i}]";
            Assert.AreEqual(e.Key, a.Key, at);
            Assert.AreEqual(e.Default, a.Default, at);
            Assert.AreEqual(e.Reset, a.Reset, at);
            Assert.AreEqual(e.Interval, a.Interval, at);
            Assert.AreEqual(e.Description, a.Description, at);
        }

        Assert.AreEqual(expected.Rules.Length, actual.Rules.Length, "rules 수");
        for (int r = 0; r < expected.Rules.Length; r++)
        {
            StateRuleDefinition e = expected.Rules[r];
            StateRuleDefinition a = actual.Rules[r];
            string at = $"rules[{r}]";
            Assert.AreEqual(e.Event, a.Event, at);
            Assert.AreEqual(e.HasRuleKey, a.HasRuleKey, at);
            Assert.AreEqual(e.RuleKeyValue, a.RuleKeyValue, at);
            Assert.AreEqual(e.Description, a.Description, at);

            Assert.AreEqual(e.Require.Length, a.Require.Length, at + ".require 수");
            for (int i = 0; i < e.Require.Length; i++)
            {
                Assert.AreEqual(e.Require[i].Key, a.Require[i].Key, $"{at}.require[{i}]");
                Assert.AreEqual(e.Require[i].Comparison, a.Require[i].Comparison, $"{at}.require[{i}]");
                Assert.AreEqual(e.Require[i].Value, a.Require[i].Value, $"{at}.require[{i}]");
            }

            Assert.AreEqual(e.Ops.Length, a.Ops.Length, at + ".ops 수");
            for (int i = 0; i < e.Ops.Length; i++)
            {
                Assert.AreEqual(e.Ops[i].Key, a.Ops[i].Key, $"{at}.ops[{i}]");
                Assert.AreEqual(e.Ops[i].Op, a.Ops[i].Op, $"{at}.ops[{i}]");
                Assert.AreEqual(e.Ops[i].Value, a.Ops[i].Value, $"{at}.ops[{i}]");
            }
        }
    }

    private static string[] Names(JToken token)
    {
        var names = new List<string>();
        foreach (JProperty property in ((JObject)token).Properties())
            names.Add(property.Name);
        return names.ToArray();
    }

    /// <summary>문장이 포함된 로그와 일치. LogPrinter가 앞뒤에 무엇을 붙여도 통과하도록 부분 일치로 본다.</summary>
    private static Regex LogContaining(string text)
    {
        return new Regex(Regex.Escape(text));
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