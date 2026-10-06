using System.Collections.Generic;

/// <summary>상호작용 Set 하나의 상태 연결 문제 1건.</summary>
public readonly struct InteractionStateLinkIssue
{
    public readonly bool IsError;
    public readonly int InteractionIndex; // 카드 순번
    public readonly string Message;       // InteractionValidator와 같은 "{Set Id}[{순번}]: …" 형식

    public InteractionStateLinkIssue(bool isError, int interactionIndex, string message)
    {
        IsError = isError;
        InteractionIndex = interactionIndex;
        Message = message;
    }
}

/// <summary>
/// 상호작용 Set이 상태 정의와 맞는지 확인한다 — UI와 파일 IO를 모르는 순수 로직이라 테스트할 수 있다.
///   에러: 게임에 등록되지 않을 상태 키를 읽음 → 게임이 로드할 때 그 Set 전체를 거부한다 (StateValueCondition.OnLoad)
///   경고: 받는 규칙이 없는 신호를 냄 → 코드가 직접 받는 신호일 수도 있어 경고에 그친다
/// 상태 정의를 온전히 읽지 못했으면(StateDefinitionCatalog.IsComplete == false) 아무것도 알리지 않는다 —
/// 덜 읽은 목록으로 "없는 키"라고 하면 멀쩡한 Set의 저장을 막게 된다.
/// </summary>
public static class InteractionStateLinkCheck
{
    /// <param name="scratch">노드 수집에 쓰는 작업 공간 — 호출하는 쪽이 재사용한다 (내용은 덮어쓴다)</param>
    public static void Check(InteractionSetDefinition set, StateDefinitionCatalog catalog, InteractionReferenceScan scratch,
                             List<InteractionStateLinkIssue> issues)
    {
        if (set == null || catalog == null || catalog.IsComplete == false)
            return;

        // 묶음 조건(Not 등) 안쪽까지 빠짐없이 찾는 순회는 Step 5-B의 수집기를 그대로 쓴다
        scratch.Clear();
        InteractionReferenceScanner.Collect(set, null, scratch);
        string id = set.Id ?? "";

        for (int i = 0; i < scratch.KeyReads.Count; i++)
        {
            InteractionKeyRead read = scratch.KeyReads[i];
            if (catalog.IsDeclared(read.Key))
                continue;

            string nested = read.IsNested ? " 안쪽" : "";
            issues.Add(new InteractionStateLinkIssue(true, read.InteractionIndex,
                $"{id}[{read.InteractionIndex}]: Conditions[{read.ConditionIndex}]{nested} — 선언되지 않은 상태 키 '{read.Key}'. 게임에서 이 Set 전체가 로드되지 않음"));
        }

        for (int i = 0; i < scratch.SignalRaises.Count; i++)
        {
            InteractionSignalRaise raise = scratch.SignalRaises[i];
            if (catalog.DescribeSignalReceivers(raise.SignalId, 1) != null)
                continue;

            issues.Add(new InteractionStateLinkIssue(false, raise.InteractionIndex,
                $"{id}[{raise.InteractionIndex}]: Effects[{raise.EffectIndex}] — 신호 {raise.SignalId}번을 받는 규칙이 없음. 상태 값이 바뀌지 않음 (코드가 직접 받는 신호면 무시)"));
        }
    }
}