using static Define;

/// <summary>
/// 평가 1회에 필요한 정보. 트리거 발생 지점에서 만들어 Condition/Effect까지 한 번에 진행.
/// </summary>
public readonly struct InteractionContext
{
    public readonly CreatureBase Owner;          // 상호작용 보유자 (NPC/Villager/Monster 무관)
    public readonly InteractionComponent Source; // 이번 평가를 수행 중인 컴포넌트
    public readonly CreatureBase Instigator;     // 촉발 주체. null 가능 (빙의 대상 없음)
    public readonly ETriggerType Trigger;
    public readonly float Time;                  // 쿨다운 기준 시각 (Time.time)

    public InteractionContext(InteractionComponent source, CreatureBase owner, CreatureBase instigator, ETriggerType trigger, float time)
    {
        Source = source;
        Owner = owner;
        Instigator = instigator;
        Trigger = trigger;
        Time = time;
    }

    /// <summary>ETargetRef를 실제 대상으로 해석</summary>
    public CreatureBase Resolve(ETargetRef target, int objectId)
    {
        switch (target)
        {
            case ETargetRef.Owner: return Owner;
            case ETargetRef.Instigator: return Instigator;
            //case ETargetRef.ObjectId: return Managers.Object.FindCreature(objectId);
            default: return null;
        }
    }
}