using static Define;

/// <summary>일차가 넘어감. DayCycle이 발생시킨다 (Step 4).</summary>
public readonly struct DayAdvancedEvent : IGameEvent
{
    public readonly int PreviousDay;
    public readonly int NewDay;

    public DayAdvancedEvent(int previousDay, int newDay)
    {
        PreviousDay = previousDay;
        NewDay = newDay;
    }

    public EGameEventType Type { get { return EGameEventType.DayAdvanced; } }
    public int RuleKey { get { return NewDay; } }
    public CreatureBase Source { get { return null; } }
}

/// <summary>전투 대상 사망. CombatCreature.Die에서 발생시킨다 (Step 6).</summary>
public readonly struct CreatureDiedEvent : IGameEvent
{
    public readonly CombatCreature Victim;
    public readonly CombatCreature Killer;   // 없으면 null (환경 피해 등)
    public readonly int TemplateId;

    public CreatureDiedEvent(CombatCreature victim, CombatCreature killer, int templateId)
    {
        Victim = victim;
        Killer = killer;
        TemplateId = templateId;
    }

    public EGameEventType Type { get { return EGameEventType.CreatureDied; } }
    public int RuleKey { get { return TemplateId; } }
    public CreatureBase Source { get { return Victim; } }
}

/// <summary>기획 정의 신호 — 코드 없이 "대화 완료", "문 열림" 같은 사건을 표현한다 (Step 5).</summary>
public readonly struct SignalEvent : IGameEvent
{
    public readonly int SignalId;
    public readonly CreatureBase Sender;

    public SignalEvent(int signalId, CreatureBase sender)
    {
        SignalId = signalId;
        Sender = sender;
    }

    public EGameEventType Type { get { return EGameEventType.Signal; } }
    public int RuleKey { get { return SignalId; } }
    public CreatureBase Source { get { return Sender; } }
}