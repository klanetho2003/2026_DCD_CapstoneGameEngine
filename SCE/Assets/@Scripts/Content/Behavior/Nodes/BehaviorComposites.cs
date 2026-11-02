using System;

/// <summary>자식을 차례로 시도해 하나가 성공하면 성공, 모두 실패하면 실패. 앞 자식이 우선순위가 높다 — 뒤 자식이 진행 중일 때 앞 자식의 LowerPriority 조건이 참이 되면 앞 자식부터 다시</summary>
[Serializable]
[BehaviorNode("Selector", "Selector", "자식을 차례로 — 하나가 성공하면 성공, 모두 실패하면 실패")]
public sealed class BehaviorSelector : BehaviorComposite
{
    public override int NextChild(BehaviorTreeRunner runner, int previousOrdinal, EBehaviorStatus previousResult, out EBehaviorStatus finishResult)
    {
        finishResult = EBehaviorStatus.Failure;
        if (previousOrdinal < 0)
            return 0;
        if (previousResult == EBehaviorStatus.Success)
        {
            finishResult = EBehaviorStatus.Success;
            return -1;
        }
        return previousOrdinal + 1 < Children.Length ? previousOrdinal + 1 : -1;
    }
}

/// <summary>자식을 차례로 실행해 하나가 실패하면 실패, 모두 성공하면 성공. 뒤 자식은 앞 자식의 다음 단계라 LowerPriority 조건을 받지 않는다</summary>
[Serializable]
[BehaviorNode("Sequence", "Sequence", "자식을 차례로 — 하나가 실패하면 실패, 모두 성공하면 성공")]
public sealed class BehaviorSequence : BehaviorComposite
{
    public override bool CanAbortLowerPriority => false;

    public override int NextChild(BehaviorTreeRunner runner, int previousOrdinal, EBehaviorStatus previousResult, out EBehaviorStatus finishResult)
    {
        finishResult = EBehaviorStatus.Success;
        if (previousOrdinal < 0)
            return 0;
        if (previousResult == EBehaviorStatus.Failure)
        {
            finishResult = EBehaviorStatus.Failure;
            return -1;
        }
        return previousOrdinal + 1 < Children.Length ? previousOrdinal + 1 : -1;
    }
}