using System;
using System.Collections.Generic;

/// <summary>키 선언 위치.</summary>
public readonly struct StateKeyDeclaration
{
    public readonly int SetIndex;
    public readonly int KeyIndex;

    public StateKeyDeclaration(int setIndex, int keyIndex)
    {
        SetIndex = setIndex;
        KeyIndex = keyIndex;
    }
}

/// <summary>규칙이 키를 쓰는 위치 — require(읽기) 또는 ops(바꾸기)의 한 행.</summary>
public readonly struct StateKeyRuleReference
{
    public readonly int SetIndex;
    public readonly int RuleIndex;
    public readonly int EntryIndex;
    public readonly bool IsWrite;

    public StateKeyRuleReference(int setIndex, int ruleIndex, int entryIndex, bool isWrite)
    {
        SetIndex = setIndex;
        RuleIndex = ruleIndex;
        EntryIndex = entryIndex;
        IsWrite = isWrite;
    }
}

/// <summary>키 하나의 사용처. 목록을 재사용하려고 클래스로 둔다.</summary>
public sealed class StateKeyUsageResult
{
    public readonly List<StateKeyDeclaration> Declarations = new List<StateKeyDeclaration>();
    public readonly List<StateKeyRuleReference> Readers = new List<StateKeyRuleReference>(); // require
    public readonly List<StateKeyRuleReference> Writers = new List<StateKeyRuleReference>(); // ops

    public int ReferenceCount { get { return Readers.Count + Writers.Count; } }

    public void Clear()
    {
        Declarations.Clear();
        Readers.Clear();
        Writers.Clear();
    }

    /// <summary>참조가 있는 파일 수 (이름 변경 확인 문구용).</summary>
    public int ReferencedFileCount()
    {
        var files = new HashSet<int>();
        for (int i = 0; i < Readers.Count; i++)
            files.Add(Readers[i].SetIndex);
        for (int i = 0; i < Writers.Count; i++)
            files.Add(Writers[i].SetIndex);
        return files.Count;
    }
}

/// <summary>
/// 상태 키의 사용처 수집과 참조 이름 변경 — UI와 Undo를 모르는 순수 로직이라 테스트할 수 있다.
/// 색인을 유지하지 않고 필요할 때마다 전체를 훑는다: O(키 수 + 규칙 수 × 행 수).
/// </summary>
public static class StateKeyUsage
{
    public static void Collect(IReadOnlyList<StateDefinitionSet> sets, string key, StateKeyUsageResult result)
    {
        result.Clear();
        if (string.IsNullOrEmpty(key))
            return;

        for (int s = 0; s < sets.Count; s++)
        {
            StateDefinitionSet set = sets[s];
            if (set == null)
                continue;

            StateKeyDefinition[] keys = set.Keys ?? Array.Empty<StateKeyDefinition>();
            for (int k = 0; k < keys.Length; k++)
            {
                if (keys[k] != null && keys[k].Key == key)
                    result.Declarations.Add(new StateKeyDeclaration(s, k));
            }

            StateRuleDefinition[] rules = set.Rules ?? Array.Empty<StateRuleDefinition>();
            for (int r = 0; r < rules.Length; r++)
            {
                StateRuleDefinition rule = rules[r];
                if (rule == null)
                    continue;

                StateRequireDefinition[] requires = rule.Require ?? Array.Empty<StateRequireDefinition>();
                for (int i = 0; i < requires.Length; i++)
                {
                    if (requires[i] != null && requires[i].Key == key)
                        result.Readers.Add(new StateKeyRuleReference(s, r, i, false));
                }

                StateOpDefinition[] ops = rule.Ops ?? Array.Empty<StateOpDefinition>();
                for (int i = 0; i < ops.Length; i++)
                {
                    if (ops[i] != null && ops[i].Key == key)
                        result.Writers.Add(new StateKeyRuleReference(s, r, i, true));
                }
            }
        }
    }

    /// <summary>모든 파일의 규칙에서 oldKey를 가리키는 require·ops 행을 newKey로 바꾼다. 선언은 건드리지 않는다. 바꾼 행 수를 돌려준다.</summary>
    public static int RenameReferences(IReadOnlyList<StateDefinitionSet> sets, string oldKey, string newKey)
    {
        int count = 0;
        for (int s = 0; s < sets.Count; s++)
        {
            StateRuleDefinition[] rules = sets[s]?.Rules;
            if (rules == null)
                continue;

            for (int r = 0; r < rules.Length; r++)
            {
                StateRuleDefinition rule = rules[r];
                if (rule == null)
                    continue;

                if (rule.Require != null)
                {
                    for (int i = 0; i < rule.Require.Length; i++)
                    {
                        if (rule.Require[i] != null && rule.Require[i].Key == oldKey)
                        {
                            rule.Require[i].Key = newKey;
                            count++;
                        }
                    }
                }
                if (rule.Ops != null)
                {
                    for (int i = 0; i < rule.Ops.Length; i++)
                    {
                        if (rule.Ops[i] != null && rule.Ops[i].Key == oldKey)
                        {
                            rule.Ops[i].Key = newKey;
                            count++;
                        }
                    }
                }
            }
        }
        return count;
    }

    /// <summary>
    /// 참조를 옮겨도 안전한 새 이름인가 — 이름 규칙에 맞고, 시스템 예약이 아니고, 아직 아무도 선언하지 않은 이름.
    /// 이미 있는 키로 참조를 옮기면 두 키가 하나로 합쳐지므로 막는다.
    /// </summary>
    public static bool IsFreeName(IReadOnlyList<StateDefinitionSet> sets, string name, out string reason)
    {
        if (StateKeyRegistry.IsValidKeyName(name, out reason) == false)
            return false;

        if (name.StartsWith(StateKeyRegistry.ReservedPrefix, StringComparison.Ordinal))
        {
            reason = $"'{StateKeyRegistry.ReservedPrefix}'로 시작하는 이름은 시스템 예약";
            return false;
        }

        for (int s = 0; s < sets.Count; s++)
        {
            StateKeyDefinition[] keys = sets[s]?.Keys;
            if (keys == null)
                continue;
            for (int k = 0; k < keys.Length; k++)
            {
                if (keys[k] != null && keys[k].Key == name)
                {
                    reason = $"'{name}'는 이미 선언된 키";
                    return false;
                }
            }
        }

        reason = null;
        return true;
    }

    /// <summary>코드가 등록하는 시스템 키인가 (sys.day 등). 목록의 원본은 GameStateManager.RegisterSystemKeys.</summary>
    public static bool IsSystemKey(string key, out StateKeyDefinition definition)
    {
        var registry = new StateKeyRegistry();
        GameStateManager.RegisterSystemKeys(registry);
        if (registry.TryGetHandle(key, out StateHandle handle))
        {
            definition = registry.GetDefinition(handle.Index);
            return true;
        }
        definition = null;
        return false;
    }
}