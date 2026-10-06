using System.Collections.Generic;
using NUnit.Framework;

/// <summary>키 사용처 수집과 참조 이름 변경 (StateKeyUsage) 테스트 — UI·Undo 없이 로직만 본다.</summary>
public class StateKeyUsageTests
{
    private const string FileA = @"{ ""id"": ""a"",
      ""keys"": [ { ""key"": ""kill"", ""default"": 0 }, { ""key"": ""ach"", ""default"": 0 } ],
      ""rules"": [
        { ""event"": ""CreatureDied"", ""ops"": [ { ""key"": ""kill"", ""op"": ""Add"", ""value"": 1 } ] },
        { ""event"": ""CreatureDied"",
          ""require"": [ { ""key"": ""kill"", ""value"": 3 } ],
          ""ops"": [ { ""key"": ""ach"", ""op"": ""Max"", ""value"": 1 } ] } ] }";

    // 다른 파일의 키를 읽고 바꾸는 규칙만 있는 파일
    private const string FileB = @"{ ""id"": ""b"",
      ""rules"": [
        { ""event"": ""Signal"", ""ruleKey"": 1,
          ""require"": [ { ""key"": ""sys.day"", ""value"": 2 }, { ""key"": ""kill"", ""value"": 1 } ],
          ""ops"": [ { ""key"": ""kill"", ""op"": ""Set"", ""value"": 0 } ] } ] }";

    [Test]
    public void Collect_FindsDeclarationReadersAndWriters_AcrossFiles()
    {
        List<StateDefinitionSet> sets = Parse(FileA, FileB);
        var usage = new StateKeyUsageResult();

        StateKeyUsage.Collect(sets, "kill", usage);

        Assert.AreEqual(1, usage.Declarations.Count);
        Assert.AreEqual(0, usage.Declarations[0].SetIndex);
        Assert.AreEqual(0, usage.Declarations[0].KeyIndex);

        Assert.AreEqual(2, usage.Writers.Count);
        AssertReference(usage.Writers[0], 0, 0, 0, true);  // a.rules[0].ops[0]
        AssertReference(usage.Writers[1], 1, 0, 0, true);  // b.rules[0].ops[0]

        Assert.AreEqual(2, usage.Readers.Count);
        AssertReference(usage.Readers[0], 0, 1, 0, false); // a.rules[1].require[0]
        AssertReference(usage.Readers[1], 1, 0, 1, false); // b.rules[0].require[1]

        Assert.AreEqual(4, usage.ReferenceCount);
        Assert.AreEqual(2, usage.ReferencedFileCount());
    }

    [Test]
    public void Collect_UnknownKey_IsEmpty()
    {
        var usage = new StateKeyUsageResult();
        StateKeyUsage.Collect(Parse(FileA, FileB), "no.such.key", usage);

        Assert.AreEqual(0, usage.Declarations.Count);
        Assert.AreEqual(0, usage.ReferenceCount);
    }

    [Test]
    public void RenameReferences_ChangesOnlyThatKey_InAllFiles()
    {
        List<StateDefinitionSet> sets = Parse(FileA, FileB);
        var usage = new StateKeyUsageResult();

        int changed = StateKeyUsage.RenameReferences(sets, "kill", "kill.total");
        Assert.AreEqual(4, changed);

        StateKeyUsage.Collect(sets, "kill", usage);
        Assert.AreEqual(0, usage.ReferenceCount, "예전 이름을 가리키는 규칙이 남으면 안 됨");
        Assert.AreEqual(1, usage.Declarations.Count, "선언은 건드리지 않는다 (창이 따로 바꾼다)");

        StateKeyUsage.Collect(sets, "kill.total", usage);
        Assert.AreEqual(4, usage.ReferenceCount);

        StateKeyUsage.Collect(sets, "ach", usage);
        Assert.AreEqual(1, usage.Writers.Count, "다른 키는 그대로");
        StateKeyUsage.Collect(sets, "sys.day", usage);
        Assert.AreEqual(1, usage.Readers.Count, "다른 키는 그대로");
    }

    [Test]
    public void RenameDeclarationAndReferences_StaysValid()
    {
        // 창이 하는 일을 그대로: 선언 이름 변경 + 참조 변경 → 런타임 검사에 에러가 없어야 한다
        List<StateDefinitionSet> sets = Parse(FileA, FileB);
        sets[0].Keys[0].Key = "kill.total";
        StateKeyUsage.RenameReferences(sets, "kill", "kill.total");

        var issues = new List<StateDefinitionIssue>();
        Assert.IsTrue(StateDefinitionValidator.Validate(sets, issues), issues.Count > 0 ? issues[0].Text : "");
    }

    [Test]
    public void IsFreeName_RejectsExistingReservedAndInvalid()
    {
        List<StateDefinitionSet> sets = Parse(FileA, FileB);

        Assert.IsTrue(StateKeyUsage.IsFreeName(sets, "kill.total", out _));
        Assert.IsFalse(StateKeyUsage.IsFreeName(sets, "ach", out string existing));
        StringAssert.Contains("이미 선언된 키", existing);
        Assert.IsFalse(StateKeyUsage.IsFreeName(sets, "sys.custom", out _));
        Assert.IsFalse(StateKeyUsage.IsFreeName(sets, "bad key", out _));
        Assert.IsFalse(StateKeyUsage.IsFreeName(sets, "", out _));
    }

    [Test]
    public void IsSystemKey_KnowsDayKey()
    {
        Assert.IsTrue(StateKeyUsage.IsSystemKey(GameStateManager.DayKey, out StateKeyDefinition definition));
        Assert.AreEqual(GameStateManager.FirstDay, definition.Default);
        Assert.IsFalse(StateKeyUsage.IsSystemKey("kill", out _));
    }

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

    private static void AssertReference(StateKeyRuleReference reference, int setIndex, int ruleIndex, int entryIndex, bool isWrite)
    {
        Assert.AreEqual(setIndex, reference.SetIndex);
        Assert.AreEqual(ruleIndex, reference.RuleIndex);
        Assert.AreEqual(entryIndex, reference.EntryIndex);
        Assert.AreEqual(isWrite, reference.IsWrite);
    }
    #endregion
}