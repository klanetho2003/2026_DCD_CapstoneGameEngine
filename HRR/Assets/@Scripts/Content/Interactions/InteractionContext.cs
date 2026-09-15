using static Define;

/// <summary>
/// 평가 1회에 필요한 정보. readonly struct + in 전달로 할당 0.
/// 트리거 발생 지점에서 만들어 Condition/Effect까지 그대로 내려간다.
/// </summary>
public readonly struct InteractionContext
{
    public readonly NPC Owner;               // 상호작용 보유자
    public readonly CreatureBase Instigator; // 촉발 주체. null 가능 (빙의 대상 없음)
    public readonly ETriggerType Trigger;    // 이번 평가를 일으킨 트리거
    public readonly float Time;              // 쿨다운 기준 시각 (Time.time)

    public InteractionContext(NPC owner, CreatureBase instigator, ETriggerType trigger, float time)
    {
        Owner = owner;
        Instigator = instigator;
        Trigger = trigger;
        Time = time;
    }

    /// <summary>ETargetRef를 실제 대상으로 해석. ObjectId는 호출 측이 ObjectManager로 푼다.</summary>
    public CreatureBase Resolve(ETargetRef target, int objectId)
    {
        switch (target)
        {
            case ETargetRef.Owner: return Owner;
            case ETargetRef.Instigator: return Instigator;
            // case ETargetRef.ObjectId: return Managers.Object.FindCreature(objectId);
            default: return null;
        }
    }
}