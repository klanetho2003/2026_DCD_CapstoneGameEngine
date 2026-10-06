using System;
using System.Collections.Generic;
using static Define;

/// <summary>검사 결과가 가리키는 위치의 종류.</summary>
public enum EStateDefinitionSection
{
    File = 0,   // 파일 전체 (id 등)
    Key,        // keys[ItemIndex]
    Rule,       // rules[ItemIndex]
    Require,    // rules[ItemIndex].require[SubIndex]
    Op,         // rules[ItemIndex].ops[SubIndex]
}

/// <summary>
/// 검사 결과 1건.
/// Text는 위치를 포함한 문장으로, 런타임 로그 본문과 같다 (로그에는 앞에 "[GameStateManager] "만 붙는다).
/// 위치 필드는 툴의 클릭 이동용 — 문장을 파싱하지 않아도 된다.
/// </summary>
public readonly struct StateDefinitionIssue
{
    public readonly bool IsError;
    public readonly int SetIndex;                    // Validate에 넘긴 sets 목록의 순번
    public readonly EStateDefinitionSection Section;
    public readonly int ItemIndex;                   // 키 또는 규칙 순번 (File이면 -1)
    public readonly int SubIndex;                    // require&ops 순번 (그 외 -1)
    public readonly string Text;

    public StateDefinitionIssue(bool isError, int setIndex, EStateDefinitionSection section, int itemIndex, int subIndex, string text)
    {
        IsError = isError;
        SetIndex = setIndex;
        Section = section;
        ItemIndex = itemIndex;
        SubIndex = subIndex;
        Text = text;
    }
}

/// <summary>검사를 통과한 규칙의 위치. 런타임은 이 목록에 있는 규칙만 변환한다.</summary>
public readonly struct StateRuleLocation
{
    public readonly int SetIndex;
    public readonly int RuleIndex;

    public StateRuleLocation(int setIndex, int ruleIndex)
    {
        SetIndex = setIndex;
        RuleIndex = ruleIndex;
    }
}

/// <summary>
/// 상태 정의 검사기 — 런타임(GameStateManager.LoadDefinitions)과 에디터 툴이 같은 함수를 쓴다.
/// 검사 규칙이 여기 한 곳에만 있으므로, 툴에서 통과한 파일은 게임에서도 같은 결과가 나온다.
///
/// 순서: 모든 파일의 키를 먼저 등록한 뒤 규칙을 본다 — 규칙이 다른 파일의 키를 참조할 수 있도록.
/// 키 검사는 등록소의 TryAdd를 그대로 쓴다 (이름 규칙·예약 접두사·중복·interval 판정이 등록소에 있으므로).
/// 에디터 전용 경고(ruleKey 0 등 기획 실수 탐지)는 여기 두지 않는다 — 런타임 로그를 시끄럽게 만들지 않기 위해.
/// </summary>
public static class StateDefinitionValidator
{
    /// <summary>에디터 툴·테스트용. 시스템 키(sys.day 등)를 미리 등록한 임시 등록소로 검사한다.</summary>
    public static bool Validate(IReadOnlyList<StateDefinitionSet> sets, List<StateDefinitionIssue> issues)
    {
        var registry = new StateKeyRegistry();
        GameStateManager.RegisterSystemKeys(registry);
        return Validate(sets, registry, issues, null);
    }

    /// <summary>
    /// 런타임용. 검사를 통과한 키는 registry에 등록되고 (시스템 키는 호출 측이 먼저 등록),
    /// 에러 없는 규칙은 acceptedRules에 선언 순서대로 담긴다 (null이면 생략).
    /// 에러가 하나라도 있으면 false — 그래도 유효한 항목은 모두 처리한다.
    /// </summary>
    public static bool Validate(IReadOnlyList<StateDefinitionSet> sets, StateKeyRegistry registry,
                                List<StateDefinitionIssue> issues, List<StateRuleLocation> acceptedRules)
    {
        bool ok = true;

        // 1단계: 모든 파일의 키
        for (int s = 0; s < sets.Count; s++)
        {
            StateDefinitionSet set = sets[s];
            if (set == null)
                continue;

            if (string.IsNullOrEmpty(set.Id))
            {
                // 런타임에서는 로더가 먼저 거부해 여기까지 오지 않는다. 툴에서 id를 지운 경우.
                issues.Add(new StateDefinitionIssue(true, s, EStateDefinitionSection.File, -1, -1,
                    "id 비어 있음 — 게임에서는 이 파일 전체가 로드되지 않음"));
                ok = false;
                continue;
            }

            ok &= ValidateKeys(set, s, registry, issues);
        }

        // 2단계: 규칙
        for (int s = 0; s < sets.Count; s++)
        {
            StateDefinitionSet set = sets[s];
            if (set == null || string.IsNullOrEmpty(set.Id))
                continue;

            StateRuleDefinition[] rules = set.Rules ?? Array.Empty<StateRuleDefinition>();
            for (int r = 0; r < rules.Length; r++)
            {
                if (ValidateRule(set, s, r, registry, issues))
                    acceptedRules?.Add(new StateRuleLocation(s, r));
                else
                    ok = false; // 에러 있는 규칙은 통째로 제외 — 일부 op만 실행되는 반쪽 규칙을 만들지 않는다
            }
        }

        return ok;
    }

    /// <summary>규칙 위치 표기 "{파일 id}.rules[{순번}]" — 검사 문장과 런타임 추적 로그가 같이 쓴다.</summary>
    public static string RuleLabel(string setId, int ruleIndex)
    {
        return $"{setId}.rules[{ruleIndex}]";
    }

    private static bool ValidateKeys(StateDefinitionSet set, int setIndex, StateKeyRegistry registry, List<StateDefinitionIssue> issues)
    {
        bool ok = true;
        StateKeyDefinition[] keys = set.Keys ?? Array.Empty<StateKeyDefinition>();

        for (int k = 0; k < keys.Length; k++)
        {
            StateKeyDefinition key = keys[k];
            if (registry.TryAdd(key, allowReserved: false, out string error) == false)
            {
                issues.Add(new StateDefinitionIssue(true, setIndex, EStateDefinitionSection.Key, k, -1,
                    $"'{set.Id}'.keys[{k}]: {error}"));
                ok = false;
            }
            else if (key.Reset != EStateResetPolicy.EveryNDays && key.Interval != 0)
            {
                issues.Add(new StateDefinitionIssue(false, setIndex, EStateDefinitionSection.Key, k, -1,
                    $"'{set.Id}'.keys[{k}]: '{key.Key}' — interval {key.Interval}은 reset이 EveryNDays일 때만 쓰임 (지금 {key.Reset}) — 무시"));
            }
        }
        return ok;
    }

    private static bool ValidateRule(StateDefinitionSet set, int setIndex, int ruleIndex, StateKeyRegistry registry, List<StateDefinitionIssue> issues)
    {
        StateRuleDefinition rule = set.Rules[ruleIndex];
        string label = RuleLabel(set.Id, ruleIndex);

        if (rule == null)
        {
            issues.Add(new StateDefinitionIssue(true, setIndex, EStateDefinitionSection.Rule, ruleIndex, -1,
                $"{label}: null"));
            return false;
        }
        if (rule.Event == EGameEventType.None || (uint)rule.Event >= (uint)EGameEventType.Count)
        {
            issues.Add(new StateDefinitionIssue(true, setIndex, EStateDefinitionSection.Rule, ruleIndex, -1,
                $"{label}: event 지정 필요 ({rule.Event})"));
            return false;
        }

        bool ok = true;

        StateRequireDefinition[] requires = rule.Require ?? Array.Empty<StateRequireDefinition>();
        for (int i = 0; i < requires.Length; i++)
        {
            StateRequireDefinition require = requires[i];
            if (require == null || registry.TryGetHandle(require.Key, out _) == false)
            {
                issues.Add(new StateDefinitionIssue(true, setIndex, EStateDefinitionSection.Require, ruleIndex, i,
                    $"{label}.require[{i}]: 선언되지 않은 키 '{require?.Key}'"));
                ok = false;
            }
        }

        StateOpDefinition[] ops = rule.Ops ?? Array.Empty<StateOpDefinition>();
        for (int i = 0; i < ops.Length; i++)
        {
            StateOpDefinition op = ops[i];
            if (op == null || registry.TryGetHandle(op.Key, out _) == false)
            {
                issues.Add(new StateDefinitionIssue(true, setIndex, EStateDefinitionSection.Op, ruleIndex, i,
                    $"{label}.ops[{i}]: 선언되지 않은 키 '{op?.Key}'"));
                ok = false;
                continue;
            }
            if (op.Key.StartsWith(StateKeyRegistry.ReservedPrefix, StringComparison.Ordinal))
            {
                issues.Add(new StateDefinitionIssue(true, setIndex, EStateDefinitionSection.Op, ruleIndex, i,
                    $"{label}.ops[{i}]: 시스템 키 '{op.Key}'는 규칙으로 변경 불가 (읽기만 허용)"));
                ok = false;
                continue;
            }
            if (op.Op == EStateOp.Add && op.Value == 0)
            {
                issues.Add(new StateDefinitionIssue(false, setIndex, EStateDefinitionSection.Op, ruleIndex, i,
                    $"{label}.ops[{i}]: Add 0 — 아무 변화 없음"));
            }
        }

        if (ops.Length == 0)
        {
            issues.Add(new StateDefinitionIssue(false, setIndex, EStateDefinitionSection.Rule, ruleIndex, -1,
                $"{label}: ops 없음 — 아무 일도 하지 않는 규칙"));
        }

        return ok;
    }
}