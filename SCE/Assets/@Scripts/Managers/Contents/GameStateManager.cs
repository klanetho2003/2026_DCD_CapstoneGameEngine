using System;
using System.Collections.Generic;
using UnityEngine;
using static Define;

/// <summary>
/// 게임 상태(플래그, 카운터, 일차) 저장소 + 사건 → 값 변경 규칙 처리기.
///
/// 이벤트 버스의 규칙 처리기(IGameEventRuleSink)로 등록되어, 모든 이벤트를 타입별 리스너보다 먼저 받는다.
/// 
/// Clear()는 두지 않았다.
/// 게임 상태는 세션 수명이라 씬 전환에서 지우면 안 되기 때문. 새 게임 시작 시에만 ResetAllToDefault()를 호출.
/// </summary>
public sealed class GameStateManager : IGameEventRuleSink
{

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>규칙이 값을 바꿀 때마다 로그. 디버그 창에서 토글.</summary>
    public static bool TraceRules = true;
#endif

    private readonly StateKeyRegistry _registry = new();
    private int[] _values = Array.Empty<int>();
    private CompiledStateRule[][] _rulesByType = CreateEmptyRuleTable();
    public int RuleCount { get; private set; }
    private bool _warnedNotLoaded;

    public StateKeyRegistry Registry { get { return _registry; } }
    public bool IsLoaded { get; private set; }

    /// <summary>값이 하나라도 바뀔 때마다 증가. 소비자는 마지막으로 본 값과 비교해 변경을 감지한다.</summary>
    public int ChangeVersion { get; private set; }


    public DayCycle Day { get; private set; }
    public const string DayKey = StateKeyRegistry.ReservedPrefix + "day";
    public const int FirstDay = 1;

    /// <summary>
    /// 주기 초기화 대상 — 로드 시 정책별로 분류해 두어 일차 진행 때 전체 키를 훑지 않는다.
    /// </summary>
    private readonly struct PeriodicReset
    {
        public readonly StateHandle Handle;
        public readonly int Interval;

        public PeriodicReset(StateHandle handle, int interval)
        {
            Handle = handle;
            Interval = interval;
        }
    }

    private StateHandle[] _resetEveryDay = Array.Empty<StateHandle>();
    private PeriodicReset[] _resetEveryNDays = Array.Empty<PeriodicReset>();
    public StateHandle DayHandle { get; private set; }
    public int CurrentDay { get { return IsLoaded ? _values[DayHandle.Index] : 0; } }

    public void Init()
    {
        Day = new DayCycle();
        Day.Init();
    }

    #region 로드
    /// <summary>
    /// 상태 정의를 로드하고 모든 값을 기본값으로 초기화한다.
    /// 1단계: 파싱 → 2단계: 검사 + 키 등록 (모든 파일의 키를 먼저 — 파일 간 키 참조 허용) → 3단계: 통과한 규칙만 변환
    /// 검사는 StateDefinitionValidator가 한다 — 에디터 툴과 같은 함수라 툴의 검증 결과와 게임의 로드 결과가 어긋나지 않는다.
    /// 로드 순서 불변식: 상호작용·스폰 규칙보다 먼저 호출되어야 한다.
    /// 일부 항목에 에러가 있어도 유효한 항목으로 계속 진행하고 false를 반환한다.
    /// </summary>
    public bool LoadDefinitions(IReadOnlyList<string> jsonTexts)
    {
        _registry.Clear();
        _rulesByType = CreateEmptyRuleTable();
        IsLoaded = false;
        bool ok = true;

        // 시스템 키 선등록 (코드로 추가하는 키)
        RegisterSystemKeys(_registry);

        // 1단계: 파싱
        var sets = new List<StateDefinitionSet>(jsonTexts.Count); // 로드 시 1회
        for (int f = 0; f < jsonTexts.Count; f++)
        {
            StateDefinitionSet set = StateDefinitionLoader.Parse(jsonTexts[f]);
            if (set == null)
            {
                ok = false;
                continue;
            }
            sets.Add(set);
        }

        // 2단계: 검사 + 키 등록. 검사 결과 문장을 그대로 로그로 남긴다
        var issues = new List<StateDefinitionIssue>();
        var acceptedRules = new List<StateRuleLocation>();
        ok &= StateDefinitionValidator.Validate(sets, _registry, issues, acceptedRules);
        LogIssues(issues);

        // 3단계: 통과한 규칙만 변환
        ok &= CompileRules(sets, acceptedRules);

        _registry.TryGetHandle(DayKey, out StateHandle dayHandle);
        DayHandle = dayHandle;

        BuildResetTables();

        _values = new int[_registry.Count];
        IsLoaded = true;
        _warnedNotLoaded = false;
        ResetAllToDefault();

        RuleCount = CountRules();
        LogPrinter.Log($"[GameStateManager] 상태 키 {_registry.Count}개, 규칙 {RuleCount}개 로드{(ok ? "" : " — 에러 있음")}");
        return ok;
    }

    /// <summary>
    /// 코드로 추가하는 시스템 키 등록. 런타임 로드와 에디터 툴의 검사가 같은 목록을 쓰도록 여기 한 곳에 둔다.
    /// 새 시스템 키는 여기에 추가한다 (이름은 StateKeyRegistry.ReservedPrefix로 시작).
    /// </summary>
    public static void RegisterSystemKeys(StateKeyRegistry registry)
    {
        registry.TryAdd(new StateKeyDefinition
        {
            Key = DayKey,
            Default = FirstDay,
            Reset = EStateResetPolicy.None,
            Description = "현재 일차 (시스템)",
        }, allowReserved: true, out _);
    }

    /// <summary>검사 결과를 로그로. 문장은 검사기가 만든 그대로 — 툴의 검증 패널과 같은 문장이 콘솔에 찍힌다.</summary>
    private static void LogIssues(List<StateDefinitionIssue> issues)
    {
        for (int i = 0; i < issues.Count; i++)
        {
            StateDefinitionIssue issue = issues[i];
            if (issue.IsError)
                LogPrinter.LogError($"[GameStateManager] {issue.Text}");
            else
                LogPrinter.LogWarning($"[GameStateManager] {issue.Text}");
        }
    }

    private bool RegisterKeys(StateDefinitionSet set)
    {
        bool ok = true;
        for (int k = 0; k < set.Keys.Length; k++)
        {
            StateKeyDefinition key = set.Keys[k];
            if (_registry.TryAdd(key, allowReserved: false, out string error) == false)
            {
                LogPrinter.LogError($"[GameStateManager] '{set.Id}'.keys[{k}]: {error}");
                ok = false;
            }
            else if (key.Reset != EStateResetPolicy.EveryNDays && key.Interval != 0)
            {
                LogPrinter.LogWarning($"[GameStateManager] '{set.Id}' '{key.Key}': key.Reset != EStateResetPolicy.EveryNDays && interval > 0. interval은 EveryNDays에서만 사용됩니다. — 무시");
            }
        }
        return ok;
    }


    /// <summary>검사를 통과한 규칙만 변환해 이벤트 종류별 배열에 담는다. 담는 순서 = 선언 순서 = 실행 순서.</summary>
    private bool CompileRules(List<StateDefinitionSet> sets, List<StateRuleLocation> acceptedRules)
    {
        bool ok = true;
        var buckets = new List<CompiledStateRule>[(int)EGameEventType.Count];

        for (int i = 0; i < acceptedRules.Count; i++)
        {
            StateRuleLocation location = acceptedRules[i];
            StateDefinitionSet set = sets[location.SetIndex];
            StateRuleDefinition rule = set.Rules[location.RuleIndex];
            string label = StateDefinitionValidator.RuleLabel(set.Id, location.RuleIndex);

            CompiledStateRule compiled = CompileRule(rule, label);
            if (compiled == null)
            {
                // 검사를 통과했는데 키를 못 찾았다 — 검사기와 등록소가 어긋난 코드 버그 (데이터 문제가 아니다)
                LogPrinter.LogError($"[GameStateManager] {label}: 내부 오류 — 검사를 통과한 규칙의 키를 등록소에서 찾지 못함");
                ok = false;
                continue;
            }

            // 반환 받은 Rule을 EGameEventType을 기준으로 캐싱한다
            (buckets[(int)rule.Event] ??= new List<CompiledStateRule>()).Add(compiled);
        }

        // Rule을 EGameEventType을 기준으로 캐싱한다. 동적배열을 고정 배열로 전환한다. (Run Time 중에 불변할 Array이기 때문)
        for (int t = 0; t < buckets.Length; t++)
            _rulesByType[t] = buckets[t] != null ? buckets[t].ToArray() : Array.Empty<CompiledStateRule>();

        return ok;
    }

    /// <summary>
    /// 검사를 통과한 규칙을 변환한다 — 정의할 때 쓰는 문자열 key를 index 접근용 핸들로 바꾼다.
    /// 검사는 하지 않는다 (StateDefinitionValidator가 이미 함). 키를 못 찾으면 null.
    /// </summary>
    private CompiledStateRule CompileRule(StateRuleDefinition rule, string label)
    {
        var requires = new CompiledStateRequire[rule.Require.Length];
        for (int i = 0; i < requires.Length; i++)
        {
            StateRequireDefinition require = rule.Require[i];
            if (_registry.TryGetHandle(require.Key, out StateHandle handle) == false)
                return null;
            requires[i] = new CompiledStateRequire(handle, require.Comparison, require.Value);
        }

        var ops = new CompiledStateOp[rule.Ops.Length];
        for (int i = 0; i < ops.Length; i++)
        {
            StateOpDefinition op = rule.Ops[i];
            if (_registry.TryGetHandle(op.Key, out StateHandle handle) == false)
                return null;
            ops[i] = new CompiledStateOp(handle, op.Op, op.Value);
        }

        bool anyKey = rule.HasRuleKey == false; // nullable 없이 플래그로 판단
        return new CompiledStateRule(anyKey, anyKey ? 0 : rule.RuleKeyValue, requires, ops, label);
    }

    /// <summary>
    /// Rule을 넘겨 받아 참고하고, 적절한지 확인하고, 정의할 때 사용하는 문자열 key를
    /// index접근 방식으로 Get할 수 있도록 Data는 Handle로 바꾼다.
    /// </summary>
    /// <param name="rule"></param>
    /// <param name="label"></param>
    /// <out name="compiled"></param>
    /// <out name="type"></param>
    /// <returns></returns>
    private bool TryCompileRule(StateRuleDefinition rule, string label, out CompiledStateRule compiled, out EGameEventType type)
    {
        compiled = null;
        type = EGameEventType.None;

        if (rule == null)
        {
            LogPrinter.LogError($"[GameStateManager] {label}: null");
            return false;
        }
        if (rule.Event == EGameEventType.None || (uint)rule.Event >= (uint)EGameEventType.Count)
        {
            LogPrinter.LogError($"[GameStateManager] {label}: event 지정 필요 ({rule.Event})");
            return false;
        }

        bool ok = true;

        var requires = new CompiledStateRequire[rule.Require.Length];
        for (int i = 0; i < rule.Require.Length; i++)
        {
            StateRequireDefinition require = rule.Require[i];
            if (require == null || _registry.TryGetHandle(require.Key, out StateHandle handle) == false)
            {
                LogPrinter.LogError($"[GameStateManager] {label}.require[{i}]: 선언되지 않은 키 '{require?.Key}'");
                ok = false;
                continue;
            }
            requires[i] = new CompiledStateRequire(handle, require.Comparison, require.Value);
        }

        var ops = new CompiledStateOp[rule.Ops.Length];
        for (int i = 0; i < rule.Ops.Length; i++)
        {
            StateOpDefinition op = rule.Ops[i];
            if (op == null || _registry.TryGetHandle(op.Key, out StateHandle handle) == false)
            {
                LogPrinter.LogError($"[GameStateManager] {label}.ops[{i}]: 선언되지 않은 키 '{op?.Key}'");
                ok = false;
                continue;
            }
            if (op.Key.StartsWith(StateKeyRegistry.ReservedPrefix, StringComparison.Ordinal))
            {
                LogPrinter.LogError($"[GameStateManager] {label}.ops[{i}]: 시스템 키 '{op.Key}'는 규칙으로 변경 불가 (읽기만 허용)");
                ok = false;
                continue;
            }
            if (op.Op == EStateOp.Add && op.Value == 0)
                LogPrinter.LogWarning($"[GameStateManager] {label}.ops[{i}]: Add 0 — 아무 변화 없음");

            ops[i] = new CompiledStateOp(handle, op.Op, op.Value);
        }

        if (ops.Length == 0)
            LogPrinter.LogWarning($"[GameStateManager] {label}: ops 없음 — 아무 일도 하지 않는 규칙");

        if (ok == false)
            return false;

        bool anyKey = rule.RuleKey.HasValue == false; // nullable 비교 함정 회피 — HasValue로만 판단
        compiled = new CompiledStateRule(anyKey, anyKey ? 0 : rule.RuleKey.Value, requires, ops, label);
        type = rule.Event;
        return true;
    }

    private static CompiledStateRule[][] CreateEmptyRuleTable()
    {
        var table = new CompiledStateRule[(int)EGameEventType.Count][];
        for (int i = 0; i < table.Length; i++)
            table[i] = Array.Empty<CompiledStateRule>();
        return table;
    }

    private int CountRules()
    {
        int count = 0;
        for (int i = 0; i < _rulesByType.Length; i++)
            count += _rulesByType[i].Length;
        return count;
    }

    /// <summary>
    /// 새 게임 시작 시. 씬 전환에서는 호출하지 않는다.
    /// 일차도 1로 되돌리므로 DayCycle에 별도 처리가 필요 없습니다
    /// </summary>
    public void ResetAllToDefault()
    {
        for (int i = 0; i < _values.Length; i++)
            _values[i] = _registry.GetDefinition(i).Default;
        ChangeVersion++;
    }
    #endregion

    #region IGameEventRuleSink — 모든 이벤트가 타입별 리스너보다 먼저 여기를 지난다
    public void OnEventHeader(EGameEventType type, int ruleKey, CreatureBase source)
    {
        if (IsLoaded == false)
        {
            if (_warnedNotLoaded == false)
            {
                LogPrinter.LogWarning($"[GameStateManager] 정의 로드 전 이벤트 수신 ({type}) — 무시. 로드 순서 확인");
                _warnedNotLoaded = true;
            }
            return;
        }

        // DayAdvanced: 일차 갱신 → 주기 초기화를 규칙보다 먼저. 비정상 일차면 규칙도 실행하지 않는다
        if (type == EGameEventType.DayAdvanced && ApplyDayAdvance(ruleKey) == false)
            return;

        int t = (int)type;
        if ((uint)t >= (uint)_rulesByType.Length)
            return;

        CompiledStateRule[] rules = _rulesByType[t];
        for (int i = 0; i < rules.Length; i++)
        {
            CompiledStateRule rule = rules[i];
            if (rule.AnyKey == false && rule.RuleKey != ruleKey)
                continue;
            if (RequiresPass(rule.Requires) == false)
                continue;

            ApplyOps(rule);
        }
    }

    private bool RequiresPass(CompiledStateRequire[] requires)
    {
        for (int i = 0; i < requires.Length; i++)
        {
            CompiledStateRequire require = requires[i];
            if (Util.Evaluate(_values[require.Handle.Index], require.Comparison, require.Value) == false)
                return false; // early-out
        }
        return true;
    }

    private void ApplyOps(CompiledStateRule rule)
    {
        CompiledStateOp[] ops = rule.Ops;
        for (int i = 0; i < ops.Length; i++)
        {
            CompiledStateOp op = ops[i];
            int index = op.Handle.Index;
            int before = _values[index];
            int after = ComputeOp(op.Op, before, op.Value);

            if (Set(op.Handle, after))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (TraceRules)
                    LogPrinter.Log($"[GameState] {rule.Label}: {_registry.GetDefinition(index).Key} {before} → {after}");
#endif
            }
        }
    }

    /// <summary>
    /// op 하나의 결과값. 규칙 실행(ApplyOps)과 툴 경고(효과 없는 op 탐지)가 같은 계산을 쓴다.
    /// 모르는 op는 현재 값을 그대로 돌려준다 — Set이 "변화 없음"으로 처리해 아무 일도 일어나지 않는다.
    /// </summary>
    public static int ComputeOp(EStateOp op, int current, int value)
    {
        switch (op)
        {
            case EStateOp.Set: return value;
            case EStateOp.Add: return ClampedAdd(current, value);
            case EStateOp.Max: return current > value ? current : value;
            case EStateOp.Min: return current < value ? current : value;
            default: return current;
        }
    }
    #endregion

    #region 일차 관련 (Day Advance)
    /// <summary>
    /// 일차 갱신 + 주기 초기화. 규칙보다 먼저 실행된다 — 규칙이 켠 값을 초기화가 지우지 않도록.
    /// 일차를 건너뛰어도 초기화 경계는 정확히 계산된다 (건너뛴 일차의 DayAdvanced 규칙은 실행되지 않음).
    /// </summary>
    private bool ApplyDayAdvance(int newDay)
    {
        int previousDay = _values[DayHandle.Index];

        if (newDay <= previousDay)
        {
            LogPrinter.LogError($"[GameStateManager] 일차 역행/중복 ({previousDay} → {newDay}) — 무시. DayAdvancedEvent는 DayCycle만 발생시킬 것");
            return false;
        }
        if (newDay != previousDay + 1)
            LogPrinter.LogWarning($"[GameStateManager] 일차 건너뜀 ({previousDay} → {newDay}) — 건너뛴 일차의 DayAdvanced 규칙은 실행되지 않음. DayCycle.AdvanceDays 사용 권장");

        Set(DayHandle, newDay);

        int resetCount = 0;

        for (int i = 0; i < _resetEveryDay.Length; i++)
        {
            if (ResetToDefault(_resetEveryDay[i]))
                resetCount++;
        }

        // "(일차 - 1)이 N의 배수인 일차에 진입" — 경계를 넘었는지로 판정해 건너뛰기에도 정확
        int previousIndex = previousDay - 1;
        int newIndex = newDay - 1;
        for (int i = 0; i < _resetEveryNDays.Length; i++)
        {
            PeriodicReset reset = _resetEveryNDays[i];
            if (newIndex / reset.Interval > previousIndex / reset.Interval && ResetToDefault(reset.Handle))
                resetCount++;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (TraceRules)
            LogPrinter.Log($"[GameState] 일차 {previousDay} → {newDay}, 주기 초기화 {resetCount}개");
#endif
        return true;
    }

    private void BuildResetTables()
    {
        var everyDay = new List<StateHandle>();
        var everyNDays = new List<PeriodicReset>();

        for (int i = 0; i < _registry.Count; i++)
        {
            StateKeyDefinition definition = _registry.GetDefinition(i);
            switch (definition.Reset)
            {
                case EStateResetPolicy.EveryDay:
                    everyDay.Add(new StateHandle(i));
                    break;
                case EStateResetPolicy.EveryNDays:
                    everyNDays.Add(new PeriodicReset(new StateHandle(i), definition.Interval)); // Interval ≥ 1은 등록소가 보장
                    break;
            }
        }

        _resetEveryDay = everyDay.ToArray();
        _resetEveryNDays = everyNDays.ToArray();
    }

    private bool ResetToDefault(StateHandle handle)
    {
        return Set(handle, _registry.GetDefinition(handle.Index).Default);
    }
    #endregion

    #region 읽기 / 쓰기 (핸들 — 런타임 경로)
    public int Get(StateHandle handle)
    {
        int index = handle.Index;
        return (uint)index < (uint)_values.Length ? _values[index] : 0;
    }

    /// <summary>값이 실제로 바뀌었을 때만 true + ChangeVersion 증가.</summary>
    public bool Set(StateHandle handle, int value)
    {
        int index = handle.Index;
        if ((uint)index >= (uint)_values.Length)
        {
            LogPrinter.LogError($"[GameStateManager] 무효 핸들로 Set 시도 (index {index})");
            return false;
        }
        if (_values[index] == value)
            return false;

        _values[index] = value;
        ChangeVersion++;
        return true;
    }

    public bool Add(StateHandle handle, int delta)
    {
        int index = handle.Index;
        if ((uint)index >= (uint)_values.Length)
        {
            LogPrinter.LogError($"[GameStateManager] 무효 핸들로 Add 시도 (index {index})");
            return false;
        }
        return Set(handle, ClampedAdd(_values[index], delta));
    }

    /// <summary>int 범위를 넘으면 경계값으로 고정 (순환해서 음수가 되는 것 방지).</summary>
    private static int ClampedAdd(int current, int delta)
    {
        long sum = (long)current + delta;
        return sum > int.MaxValue ? int.MaxValue : sum < int.MinValue ? int.MinValue : (int)sum;
    }
    #endregion

    #region 이름 기반 (도구·디버그·저장 복원용 — 매 프레임 경로에서 쓰지 말 것)
    public bool TryGet(string key, out int value)
    {
        if (_registry.TryGetHandle(key, out StateHandle handle))
        {
            value = _values[handle.Index];
            return true;
        }
        value = 0;
        return false;
    }

    /// <summary>저장 복원용. 정의에 없는 키는 false (삭제된 키 — 호출 측에서 건너뛴다).</summary>
    public bool TrySetByKey(string key, int value)
    {
        if (_registry.TryGetHandle(key, out StateHandle handle) == false)
            return false;
        Set(handle, value);
        return true;
    }
    #endregion

    #region 저장 코드용 순회
    public int KeyCount { get { return _values.Length; } }
    public string GetKeyAt(int index) { return _registry.GetDefinition(index)?.Key; }
    public int GetValueAt(int index) { return _values[index]; }
    public int GetDefaultAt(int index) { return _registry.GetDefinition(index).Default; }
    #endregion
}