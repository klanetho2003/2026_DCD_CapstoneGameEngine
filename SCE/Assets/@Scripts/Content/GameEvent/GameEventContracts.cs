using static Define;

/// <summary>
/// 모든 게임 이벤트가 공유하는 머리. 데이터 규칙은 이 세 값만 본다.
/// 구현은 반드시 readonly struct — 버스는 제네릭 제약으로 박싱 없이 이 인터페이스를 호출한다.
/// </summary>
public interface IGameEvent
{
    EGameEventType Type { get; }
    int RuleKey { get; }          // 규칙 매칭 키 (templateID, 일차 번호, 신호 ID 등)
    CreatureBase Source { get; }  // 발생원 (없으면 null)
}

/// <summary>타입별 구독자. 인자 전체가 필요한 시스템(퀘스트, UI, 스포너 등)이 구현한다.</summary>
public interface IGameEventListener<T> where T : struct, IGameEvent
{
    void OnGameEvent(in T evt);
}

/// <summary>
/// 규칙 처리기 — 모든 이벤트의 머리를 타입별 리스너보다 먼저 받는다.
/// GameStateManager가 구현한다. 버스에 1개만 존재.
/// </summary>
public interface IGameEventRuleSink
{
    void OnEventHeader(EGameEventType type, int ruleKey, CreatureBase source);
}

/// <summary>구독 우선순위. 작을수록 먼저. 같은 값이면 먼저 구독한 쪽이 먼저.</summary>
public static class GameEventPriority
{
    public const int Early = -100;
    public const int Default = 0;
    public const int Late = 100;
}