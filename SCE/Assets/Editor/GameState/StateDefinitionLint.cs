using System;
using System.Collections.Generic;
using static Define;

/// <summary>
/// 툴 경고 — 에디터 전용 기획 실수 탐지. 게임 로드는 이 검사를 하지 않는다 (런타임 로그를 시끄럽게 만들지 않기 위해).
/// 모두 경고(저장은 막지 않음)이며, 문장 앞에 "[툴] "을 붙여 런타임 검사 결과와 구분한다.
/// 파일 사이의 규칙 순서는 게임의 로드 순서에 달려 있어 보지 않는다 — 순서 판단은 한 파일 안에서만 한다.
/// 거짓 경고는 기획자가 경고 자체를 무시하게 만들므로, 확실한 경우에만 경고한다.
/// </summary>
public static class StateDefinitionLint
{
    public const string Tag = "[툴] ";

    /// <param name="sets">StateDefinitionValidator에 넘긴 것과 같은 목록 (SetIndex가 같은 순번을 가리키도록)</param>
    public static void Check(IReadOnlyList<StateDefinitionSet> sets, List<StateDefinitionIssue> issues)
    {
        // 키 정의 색인 — 첫 선언 기준 (중복 선언은 런타임 검사가 에러로 알린다)
        var keys = new Dictionary<string, StateKeyDefinition>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);

        for (int s = 0; s < sets.Count; s++)
        {
            StateDefinitionSet set = sets[s];
            if (set == null)
                continue;

            if (string.IsNullOrEmpty(set.Id) == false && ids.Add(set.Id) == false)
            {
                issues.Add(Warning(s, EStateDefinitionSection.File, -1, -1,
                    $"파일 id '{set.Id}'가 다른 파일과 같음 — 검증·로그 문장의 위치({set.Id}.rules[0] 등)로 파일을 구분할 수 없음"));
            }

            StateKeyDefinition[] declared = set.Keys ?? Array.Empty<StateKeyDefinition>();
            for (int k = 0; k < declared.Length; k++)
            {
                StateKeyDefinition key = declared[k];
                if (key?.Key != null && keys.ContainsKey(key.Key) == false)
                    keys.Add(key.Key, key);
            }
        }

        for (int s = 0; s < sets.Count; s++)
        {
            StateDefinitionSet set = sets[s];
            if (set == null || string.IsNullOrEmpty(set.Id))
                continue;

            StateRuleDefinition[] rules = set.Rules ?? Array.Empty<StateRuleDefinition>();
            for (int r = 0; r < rules.Length; r++)
            {
                if (rules[r] == null)
                    continue;

                string label = StateDefinitionValidator.RuleLabel(set.Id, r);
                CheckRuleKey(rules[r], s, r, label, issues);
                CheckNoEffectOnDayAdvance(rules, r, s, label, keys, issues);
                CheckReadBeforeLaterWrite(set.Id, rules, r, s, label, issues);
            }
        }
    }

    /// <summary>ruleKey 값이 의도와 다르게 동작하는 경우 — "0 = 생략"으로 착각, 오지 않는 일차.</summary>
    private static void CheckRuleKey(StateRuleDefinition rule, int s, int r, string label, List<StateDefinitionIssue> issues)
    {
        if (rule.HasRuleKey == false)
            return;

        if (rule.Event == EGameEventType.CreatureDied && rule.RuleKeyValue == 0)
        {
            issues.Add(Warning(s, EStateDefinitionSection.Rule, r, -1,
                $"{label}: ruleKey 0 — templateID가 0인 개체에만 반응. 모든 개체에 반응시키려면 '특정 값만'을 끄세요 (ruleKey 생략)"));
        }
        else if (rule.Event == EGameEventType.DayAdvanced && rule.RuleKeyValue <= GameStateManager.FirstDay)
        {
            issues.Add(Warning(s, EStateDefinitionSection.Rule, r, -1,
                $"{label}: ruleKey {rule.RuleKeyValue} — 게임은 {GameStateManager.FirstDay}일차로 시작하고 일차 진행은 {GameStateManager.FirstDay + 1}일차부터 발생하므로 실행되지 않음"));
        }
    }

    /// <summary>
    /// 일차 진행 때는 주기 초기화가 규칙보다 먼저 실행된다 (GameStateManager.ApplyDayAdvance).
    /// 매일 초기화되는 키에 대해 결과가 기본값과 같은 op는 이미 기본값이라 효과가 없다.
    /// 같은 사건에 반응하는 앞선 규칙(같은 파일)이 그 키를 바꾼다면 되돌리는 의미가 있으므로 경고하지 않는다.
    /// </summary>
    private static void CheckNoEffectOnDayAdvance(StateRuleDefinition[] rules, int r, int s, string label,
                                                  Dictionary<string, StateKeyDefinition> keys, List<StateDefinitionIssue> issues)
    {
        StateRuleDefinition rule = rules[r];
        if (rule.Event != EGameEventType.DayAdvanced || rule.Ops == null)
            return;

        for (int i = 0; i < rule.Ops.Length; i++)
        {
            StateOpDefinition op = rule.Ops[i];
            if (op?.Key == null || op.Op == EStateOp.Add) // Add 0은 런타임 검사가 이미 경고한다
                continue;
            if (keys.TryGetValue(op.Key, out StateKeyDefinition key) == false || key.Reset != EStateResetPolicy.EveryDay)
                continue;
            if (GameStateManager.ComputeOp(op.Op, key.Default, op.Value) != key.Default)
                continue;
            if (WrittenByEarlierRule(rules, r, op.Key))
                continue;

            issues.Add(Warning(s, EStateDefinitionSection.Op, r, i,
                $"{label}.ops[{i}]: '{op.Key}'는 매일 초기화되는 키라 일차 진행 때 규칙보다 먼저 기본값({key.Default})이 됨 — 이 op는 효과 없음 (다른 파일의 규칙이 먼저 바꾸는 경우 제외)"));
        }
    }

    /// <summary>
    /// 같은 사건에서 require가 읽는 키를 뒤 규칙이 바꾸면, 이번 사건에서는 바뀌기 전 값을 본다 (정리본 4.6 — 업적이 한 박자 늦게 켜짐).
    /// 의도한 순서일 수도 있어 경고만 한다. require마다 처음 찾은 뒤 규칙 하나만 알린다.
    /// 자기 규칙의 ops는 해당 없음 — require는 항상 자기 ops보다 먼저 평가된다.
    /// </summary>
    private static void CheckReadBeforeLaterWrite(string setId, StateRuleDefinition[] rules, int r, int s, string label,
                                                  List<StateDefinitionIssue> issues)
    {
        StateRuleDefinition rule = rules[r];
        if (rule.Require == null)
            return;

        for (int q = 0; q < rule.Require.Length; q++)
        {
            string key = rule.Require[q]?.Key;
            if (key == null)
                continue;

            for (int j = r + 1; j < rules.Length; j++)
            {
                StateRuleDefinition later = rules[j];
                if (later == null || later.Event != rule.Event || Overlaps(rule, later) == false || Writes(later, key) == false)
                    continue;

                string laterLabel = StateDefinitionValidator.RuleLabel(setId, j);
                issues.Add(Warning(s, EStateDefinitionSection.Require, r, q,
                    $"{label}.require[{q}]: '{key}'를 같은 이벤트의 뒤 규칙 {laterLabel}이 바꿈 — 이번 이벤트에서는 바뀌기 전 값을 봄. 바뀐 값을 봐야 한다면 {laterLabel}을 이 규칙보다 위로 (의도한 순서면 무시)"));
                break;
            }
        }
    }

    #region 도우미
    /// <summary>두 규칙이 같은 사건 하나에 함께 반응할 수 있는가 — 한쪽이라도 ruleKey를 생략했거나, 둘의 값이 같으면.</summary>
    private static bool Overlaps(StateRuleDefinition a, StateRuleDefinition b)
    {
        return a.HasRuleKey == false || b.HasRuleKey == false || a.RuleKeyValue == b.RuleKeyValue;
    }

    private static bool Writes(StateRuleDefinition rule, string key)
    {
        if (rule.Ops == null)
            return false;
        for (int i = 0; i < rule.Ops.Length; i++)
        {
            if (rule.Ops[i] != null && rule.Ops[i].Key == key)
                return true;
        }
        return false;
    }

    private static bool WrittenByEarlierRule(StateRuleDefinition[] rules, int r, string key)
    {
        StateRuleDefinition rule = rules[r];
        for (int j = 0; j < r; j++)
        {
            StateRuleDefinition earlier = rules[j];
            if (earlier != null && earlier.Event == rule.Event && Overlaps(earlier, rule) && Writes(earlier, key))
                return true;
        }
        return false;
    }

    private static StateDefinitionIssue Warning(int setIndex, EStateDefinitionSection section, int itemIndex, int subIndex, string text)
    {
        return new StateDefinitionIssue(false, setIndex, section, itemIndex, subIndex, Tag + text);
    }
    #endregion
}