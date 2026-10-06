using System.Collections.Generic;
using System.Text;
using static Define;

/// <summary>
/// 교차 확인 경고 중 상호작용 쪽에 위치가 있는 것 1건.
/// 상태 파일 안에는 가리킬 위치가 없어서 StateDefinitionIssue(파일 순번이 필수)에 담을 수 없다.
/// </summary>
public readonly struct InteractionCrossIssue
{
    public readonly string FilePath; // 상호작용 JSON 경로. 파일 하나를 가리키지 않는 알림이면 null
    public readonly string Key;      // 상태 키에 대한 경고면 그 키. 아니면 null
    public readonly string Text;     // 검증 목록에 보이는 문장. StateDefinitionLint.Tag("[툴] ")로 시작
    public readonly string Detail;   // 덧붙일 내용 (툴팁용). 없으면 null

    public InteractionCrossIssue(string filePath, string key, string text, string detail)
    {
        FilePath = filePath;
        Key = key;
        Text = text;
        Detail = detail;
    }
}

/// <summary>
/// 상태 정의 ↔ 상호작용 교차 확인 — 에디터 전용 툴 경고. UI와 파일 IO를 모르는 순수 로직이라 테스트할 수 있다.
/// 두 시스템은 문자열 키와 신호 ID(정수)로만 이어져 있어, 한쪽만 고치면 게임을 실행하기 전에는 어긋난 것을 알 수 없다.
///   1) 상호작용이 읽는 상태 키가 게임에 등록되는가 (아니면 그 상호작용 Set이 통째로 로드되지 않는다)
///   2) 상호작용이 내는 신호를 받는 규칙이 있는가
///   3) 신호 규칙의 ruleKey를 내는 상호작용이 있는가
/// 상호작용 폴더를 읽지 못했으면 아무것도 경고하지 않는다 — 근거 없이 "없음"이라고 하지 않는다.
/// 신호는 코드가 직접 내거나 받을 수도 있어서(GameEventBus), 2)·3)은 문장에 그 가능성을 적는다.
/// </summary>
public static class StateInteractionCrossCheck
{
    /// <param name="sets">검사기에 넘긴 것과 같은 목록 (SetIndex가 같은 순번을 가리키도록)</param>
    /// <param name="registry">검사기가 채운 등록소 — "게임이 실제로 등록할 키"의 원본 (시스템 키 포함)</param>
    /// <param name="ruleIssues">규칙 카드에 위치가 있는 경고가 여기에 더해진다</param>
    /// <param name="interactionIssues">상호작용 쪽에 위치가 있는 경고가 여기에 더해진다</param>
    public static void Check(IReadOnlyList<StateDefinitionSet> sets, StateKeyRegistry registry, InteractionReferenceScan scan,
                             List<StateDefinitionIssue> ruleIssues, List<InteractionCrossIssue> interactionIssues)
    {
        if (scan == null || scan.FolderExists == false)
            return;

        CheckKeyReads(registry, scan, interactionIssues);

        // 규칙이 받는 신호 ID. 에러가 있는 규칙·파일도 받는 쪽으로 센다 — 이미 에러로 알린 것 위에 경고를 겹치지 않는다
        var received = new HashSet<int>();
        bool receivesAll = false; // "특정 값만"이 꺼진 신호 규칙이 하나라도 있으면 모든 신호에 받는 규칙이 있다
        for (int s = 0; s < sets.Count; s++)
        {
            StateRuleDefinition[] rules = sets[s]?.Rules;
            if (rules == null)
                continue;

            for (int r = 0; r < rules.Length; r++)
            {
                StateRuleDefinition rule = rules[r];
                if (rule == null || rule.Event != EGameEventType.Signal)
                    continue;

                if (rule.HasRuleKey)
                    received.Add(rule.RuleKeyValue);
                else
                    receivesAll = true;
            }
        }

        if (receivesAll == false)
            CheckRaisesWithoutRule(scan, received, interactionIssues);

        // 읽지 못한 파일이 신호를 낼 수도 있다 — 그럴 때는 "내는 상호작용이 없음"을 판정하지 않고, 건너뛴 사실을 알린다
        if (scan.UnreadableFiles.Count > 0)
        {
            interactionIssues.Add(new InteractionCrossIssue(null, null,
                $"{StateDefinitionLint.Tag}읽지 못한 상호작용 파일 {scan.UnreadableFiles.Count}개 — 그 안의 키 읽기·신호 발생은 확인하지 못함. '신호를 내는 상호작용이 없음' 확인은 건너뜀",
                string.Join("\n", scan.UnreadableFiles)));
            return;
        }

        CheckRulesWithoutSender(sets, scan, ruleIssues);
    }

    /// <summary>
    /// 신호 ID를 내는 상호작용을 "merchant › talk, guard › alarm 외 1곳" 형태로 돌려준다. 없으면 null.
    /// 규칙 카드의 정보 표시용 — 경고와 달리 툴 경고를 꺼도 보인다.
    /// </summary>
    public static string DescribeSenders(InteractionReferenceScan scan, int signalId, int max)
    {
        if (scan == null)
            return null;
        if (max < 1)
            max = 1;

        StringBuilder builder = null;
        int total = 0;
        string lastFile = null;
        int lastInteraction = -1;

        for (int i = 0; i < scan.SignalRaises.Count; i++)
        {
            InteractionSignalRaise raise = scan.SignalRaises[i];
            if (raise.SignalId != signalId)
                continue;

            // 한 상호작용이 같은 신호를 효과 여러 개로 내면 한 번만 적는다 (스캔은 상호작용 순서대로라 이어서 나온다)
            if (raise.FilePath == lastFile && raise.InteractionIndex == lastInteraction)
                continue;
            lastFile = raise.FilePath;
            lastInteraction = raise.InteractionIndex;

            total++;
            if (total > max)
                continue;

            builder ??= new StringBuilder();
            if (total > 1)
                builder.Append(", ");
            builder.Append(raise.SetId).Append(" › ").Append(raise.InteractionId);
        }

        if (total == 0)
            return null;
        if (total > max)
            builder.Append(" 외 ").Append(total - max).Append("곳");
        return builder.ToString();
    }

    /// <summary>
    /// 신호 ID를 받는 신호 규칙을 "core.rules[2], extra.rules[0] 외 1곳" 형태로 돌려준다. 없으면 null.
    /// "특정 값만"이 꺼진 신호 규칙은 모든 신호를 받으므로 포함하고 "(모든 신호)"를 붙인다.
    /// DescribeSenders의 반대 방향 — Interaction Editor가 신호 발생 효과 옆에 쓴다.
    /// </summary>
    public static string DescribeReceivers(IReadOnlyList<StateDefinitionSet> sets, int signalId, int max)
    {
        if (sets == null)
            return null;
        if (max < 1)
            max = 1;

        StringBuilder builder = null;
        int total = 0;

        for (int s = 0; s < sets.Count; s++)
        {
            StateDefinitionSet set = sets[s];
            if (set?.Rules == null)
                continue;

            for (int r = 0; r < set.Rules.Length; r++)
            {
                StateRuleDefinition rule = set.Rules[r];
                if (rule == null || rule.Event != EGameEventType.Signal)
                    continue;
                if (rule.HasRuleKey && rule.RuleKeyValue != signalId)
                    continue;

                total++;
                if (total > max)
                    continue;

                builder ??= new StringBuilder();
                if (total > 1)
                    builder.Append(", ");
                builder.Append(StateDefinitionValidator.RuleLabel(string.IsNullOrEmpty(set.Id) ? "(id 없음)" : set.Id, r));
                if (rule.HasRuleKey == false)
                    builder.Append(" (모든 신호)");
            }
        }

        if (total == 0)
            return null;
        if (total > max)
            builder.Append(" 외 ").Append(total - max).Append("곳");
        return builder.ToString();
    }

    #region 검사
    /// <summary>
    /// 1) 상호작용이 읽는 키가 등록소에 없으면 경고.
    /// 게임은 로드할 때 StateValueCondition.OnLoad가 같은 등록소 조회로 에러를 내고, 그 Set 전체를 버린다.
    /// </summary>
    private static void CheckKeyReads(StateKeyRegistry registry, InteractionReferenceScan scan, List<InteractionCrossIssue> interactionIssues)
    {
        for (int i = 0; i < scan.KeyReads.Count; i++)
        {
            InteractionKeyRead read = scan.KeyReads[i];
            if (string.IsNullOrEmpty(read.Key) == false && registry.TryGetHandle(read.Key, out _))
                continue;

            string nested = read.IsNested ? " 안쪽" : "";
            interactionIssues.Add(new InteractionCrossIssue(read.FilePath, read.Key ?? "",
                $"{StateDefinitionLint.Tag}상호작용 '{read.SetId} › {read.InteractionId}' 조건 {read.ConditionIndex + 1}{nested}: "
                + $"선언되지 않은 상태 키 '{read.Key}' — 게임에서 상호작용 Set '{read.SetId}' 전체가 로드되지 않음",
                read.FilePath));
        }
    }

    /// <summary>2) 상호작용이 내는 신호 ID를 받는 신호 규칙이 없으면 경고.</summary>
    private static void CheckRaisesWithoutRule(InteractionReferenceScan scan, HashSet<int> received, List<InteractionCrossIssue> interactionIssues)
    {
        for (int i = 0; i < scan.SignalRaises.Count; i++)
        {
            InteractionSignalRaise raise = scan.SignalRaises[i];
            if (received.Contains(raise.SignalId))
                continue;

            interactionIssues.Add(new InteractionCrossIssue(raise.FilePath, null,
                $"{StateDefinitionLint.Tag}상호작용 '{raise.SetId} › {raise.InteractionId}' 효과 {raise.EffectIndex + 1}: "
                + $"신호 {raise.SignalId}번을 받는 규칙이 없음 — 상태 값이 바뀌지 않음 (코드가 직접 받는 신호면 무시)",
                raise.FilePath));
        }
    }

    /// <summary>3) 신호 규칙의 ruleKey를 내는 상호작용이 없으면 그 규칙에 경고.</summary>
    private static void CheckRulesWithoutSender(IReadOnlyList<StateDefinitionSet> sets, InteractionReferenceScan scan, List<StateDefinitionIssue> ruleIssues)
    {
        var raised = new HashSet<int>();
        for (int i = 0; i < scan.SignalRaises.Count; i++)
            raised.Add(scan.SignalRaises[i].SignalId);

        for (int s = 0; s < sets.Count; s++)
        {
            StateDefinitionSet set = sets[s];
            if (set == null || string.IsNullOrEmpty(set.Id) || set.Rules == null)
                continue; // id가 빈 파일은 게임에서 통째로 로드되지 않는다 — 런타임 검사가 이미 에러로 알린다

            for (int r = 0; r < set.Rules.Length; r++)
            {
                StateRuleDefinition rule = set.Rules[r];
                if (rule == null || rule.Event != EGameEventType.Signal || rule.HasRuleKey == false)
                    continue; // "특정 값만"이 꺼진 규칙은 모든 신호를 받는다 — 내는 곳을 특정할 수 없다
                if (raised.Contains(rule.RuleKeyValue))
                    continue;

                string label = StateDefinitionValidator.RuleLabel(set.Id, r);
                ruleIssues.Add(new StateDefinitionIssue(false, s, EStateDefinitionSection.Rule, r, -1,
                    $"{StateDefinitionLint.Tag}{label}: 신호 {rule.RuleKeyValue}번을 내는 상호작용이 없음 — 코드가 직접 내는 신호가 아니라면 이 규칙은 실행되지 않음"));
            }
        }
    }
    #endregion
}