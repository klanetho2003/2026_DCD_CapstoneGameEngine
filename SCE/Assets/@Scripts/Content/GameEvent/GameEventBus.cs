using System;
using System.Collections.Generic;
using UnityEngine;
using static Define;

/// <summary>
/// 단일 이벤트 통로.
///
/// - Raise&lt;: 박싱 없음. 인자는 채널별 Queue&lt;에 원래 타입으로 저장
/// - 처리 순서: 발생 순서 그대로 (타입이 섞여도). 처리 중 발생한 이벤트는 현재 이벤트가 끝난 뒤
/// - 각 이벤트: 규칙 처리기(머리) → 타입별 리스너(우선순위 순)
///
/// T별 정적 저장소를 쓰기 위해 정적 클래스로 둔다.
/// </summary>
public static class GameEventBus
{

#if UNITY_EDITOR
    /// <summary>
    /// 에디터 디버그 창용 관찰자. 규칙 처리 직후, 타입별 리스너보다 먼저 호출된다.
    /// ResetAll에서 지우지 않는다 — 창의 수명은 플레이 세션과 무관하기 때문.
    /// </summary>
    public static Action<EGameEventType, int, CreatureBase> DebugObserver;
#endif

    private const int MaxEventsPerDrain = 1024;

    // 발생 순서 — 원소는 "어느 채널의 대기 이벤트를 하나 꺼낼지"
    private static readonly List<IGameEventChannel> s_order = new(32);
    private static int s_head;
    
    /// <summary>버스가 이벤트를 처리하는 중인가. true면 지금 Raise한 이벤트는 대기열에 들어가 나중에 처리된다.</summary>
    public static bool IsFlushing { get { return s_isFlushing; } }
    private static bool s_isFlushing;

    // 초기화 대상 — 채널이 처음 쓰일 때 스스로 등록한다
    private static readonly List<IGameEventChannel> s_channels = new(16);

    private static IGameEventRuleSink s_ruleSink;

    #region 공개 API
    /// <summary>규칙 처리기 지정. Managers.Init에서 GameStateManager로 1회.</summary>
    public static void SetRuleSink(IGameEventRuleSink sink)
    {
        s_ruleSink = sink;
    }

    public static void Subscribe<T>(IGameEventListener<T> listener, int priority = GameEventPriority.Default)
        where T : struct, IGameEvent
    {
        GameEventChannel<T>.Instance.Subscribe(listener, priority);
    }

    public static void Unsubscribe<T>(IGameEventListener<T> listener)
        where T : struct, IGameEvent
    {
        GameEventChannel<T>.Instance.Unsubscribe(listener);
    }

    public static void Raise<T>(in T evt) where T : struct, IGameEvent
    {
        GameEventChannel<T> channel = GameEventChannel<T>.Instance;
        channel.Enqueue(in evt);
        s_order.Add(channel);

        if (s_isFlushing)
            return; // 처리 중 발생 → 현재 이벤트가 끝난 뒤 순서대로 처리된다

        Drain();
    }

    /// <summary>모든 구독,대기 이벤트,규칙 처리기 초기화. 테스트와 Play 진입 시.</summary>
    public static void ResetAll()
    {
        for (int i = 0; i < s_channels.Count; i++)
        {
            s_channels[i].ClearPending();
            s_channels[i].ClearListeners();
        }
        s_order.Clear();
        s_head = 0;
        s_isFlushing = false;
        s_ruleSink = null;
    }
    #endregion

    #region 내부
    internal static IGameEventRuleSink RuleSink { get { return s_ruleSink; } }

    internal static void RegisterChannel(IGameEventChannel channel)
    {
        s_channels.Add(channel);
    }

    private static void Drain()
    {
        s_isFlushing = true;
        int processed = 0;

        try
        {
            while (s_head < s_order.Count)
            {
                if (++processed > MaxEventsPerDrain)
                {
                    LogPrinter.LogError($"[GameEventBus] 한 번에 {MaxEventsPerDrain}개 초과 — 이벤트 순환 의심 (리스너가 서로를 계속 발생시키는지 확인). 남은 이벤트 폐기");
                    break;
                }
                s_order[s_head++].DispatchNext();
            }
        }
        finally
        {
            // 정상 종료가 아니면(순환 차단, 예상치 못한 예외) 채널 대기열과 순서 목록이 어긋나므로 대기 이벤트를 모두 버린다
            if (s_head < s_order.Count)
            {
                for (int i = 0; i < s_channels.Count; i++)
                    s_channels[i].ClearPending();
            }
            s_order.Clear();
            s_head = 0;
            s_isFlushing = false;
        }
    }

    /// <summary>Domain Reload를 끈 Enter Play Mode에서도 이전 플레이의 구독이 남지 않도록.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlayModeEnter()
    {
        ResetAll();
    }
    #endregion
}

/// <summary>버스가 타입을 모른 채 채널을 다루기 위한 인터페이스.</summary>
internal interface IGameEventChannel
{
    void DispatchNext();
    void ClearPending();
    void ClearListeners();
}

/// <summary>
/// 이벤트 타입 T 전용 채널. T마다 인스턴스가 1개 생성된다 (정적 필드는 제네릭 인스턴스화마다 별도).
/// 한 채널의 DispatchNext는 중첩되지 않는다 — 처리 중 발생한 이벤트는 버스 큐로 미뤄지기 때문.
/// </summary>
internal sealed class GameEventChannel<T> : IGameEventChannel where T : struct, IGameEvent
{
    public static readonly GameEventChannel<T> Instance = new GameEventChannel<T>();

    private struct Entry
    {
        public IGameEventListener<T> Listener; // 처리 중 해제되면 null (처리 후 정리)
        public int Priority;
    }

    private readonly Queue<T> _pending = new Queue<T>(8);
    private readonly List<Entry> _listeners = new List<Entry>(4);
    private readonly List<Entry> _pendingAdds = new List<Entry>(2);
    private bool _isDispatching;
    private bool _hasRemoved;

    private GameEventChannel()
    {
        GameEventBus.RegisterChannel(this);
    }

    public void Enqueue(in T evt)
    {
        _pending.Enqueue(evt);
    }

    #region 구독
    public void Subscribe(IGameEventListener<T> listener, int priority)
    {
        if (listener == null)
            return;
        if (IndexOf(_listeners, listener) >= 0 || IndexOf(_pendingAdds, listener) >= 0)
        {
            LogPrinter.LogWarning($"[GameEventBus] {typeof(T).Name} 중복 구독 무시 >> {listener}");
            return;
        }

        var entry = new Entry { Listener = listener, Priority = priority };

        // 처리 중 삽입하면 순회 인덱스가 밀려 같은 리스너가 두 번 불리거나 건너뛴다 → 처리 후 반영
        if (_isDispatching)
        {
            _pendingAdds.Add(entry);
            return;
        }

        InsertSorted(entry);
    }

    public void Unsubscribe(IGameEventListener<T> listener)
    {
        int pendingIndex = IndexOf(_pendingAdds, listener);
        if (pendingIndex >= 0)
        {
            _pendingAdds.RemoveAt(pendingIndex);
            return;
        }

        int index = IndexOf(_listeners, listener);
        if (index < 0)
            return;

        if (_isDispatching)
        {
            Entry entry = _listeners[index];
            entry.Listener = null;       // 슬롯만 비운다 — 인덱스 유지
            _listeners[index] = entry;
            _hasRemoved = true;
            return;
        }

        _listeners.RemoveAt(index);
    }

    /// <summary>안정 삽입 — 같은 우선순위는 먼저 구독한 쪽이 먼저.</summary>
    private void InsertSorted(Entry entry)
    {
        int insert = _listeners.Count;
        for (int i = 0; i < _listeners.Count; i++)
        {
            if (_listeners[i].Priority > entry.Priority)
            {
                insert = i;
                break;
            }
        }
        _listeners.Insert(insert, entry);
    }

    private static int IndexOf(List<Entry> list, IGameEventListener<T> listener)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i].Listener, listener))
                return i;
        }
        return -1;
    }
    #endregion

    #region 처리
    public void DispatchNext()
    {
        // 지역 변수로 꺼낸다 — in 파라미터와 달리 멤버 호출 시 방어적 복사가 생기지 않는다
        T evt = _pending.Dequeue();

        // 1. 규칙 처리기 먼저 — 리스너가 받는 시점에 상태가 이미 갱신되어 있도록
        IGameEventRuleSink sink = GameEventBus.RuleSink;
        if (sink != null)
        {
            try
            {
                sink.OnEventHeader(evt.Type, evt.RuleKey, evt.Source);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        // 2. 타입별 리스너 — 개수를 고정해 처리 중 추가된 리스너는 이번 이벤트에서 제외
        _isDispatching = true;
        int count = _listeners.Count;
        for (int i = 0; i < count; i++)
        {
            IGameEventListener<T> listener = _listeners[i].Listener;
            if (listener == null)
                continue;

            // 해제를 잊은 채 파괴된 MonoBehaviour 등
            if (listener is UnityEngine.Object unityObject && unityObject == null)
            {
                LogPrinter.LogWarning($"[GameEventBus] {typeof(T).Name}: 파괴된 리스너 자동 제거 — OnDisable/Clear에서 Unsubscribe 누락");
                Entry dead = _listeners[i];
                dead.Listener = null;
                _listeners[i] = dead;
                _hasRemoved = true;
                continue;
            }

            try
            {
                listener.OnGameEvent(in evt);
            }
            catch (Exception e)
            {
                Debug.LogException(e); // 한 리스너의 예외가 나머지를 막지 않는다
            }
        }
        _isDispatching = false;

        // 3. 처리 중 변경 반영
        if (_hasRemoved)
        {
            Compact();
            _hasRemoved = false;
        }
        if (_pendingAdds.Count > 0)
        {
            for (int i = 0; i < _pendingAdds.Count; i++)
                InsertSorted(_pendingAdds[i]);
            _pendingAdds.Clear();
        }

#if UNITY_EDITOR
        GameEventBus.DebugObserver?.Invoke(evt.Type, evt.RuleKey, evt.Source);
#endif
    }

    /// <summary>비운 슬롯 제거. 람다(RemoveAll) 대신 직접 당겨 쓴다.</summary>
    private void Compact()
    {
        int write = 0;
        for (int read = 0; read < _listeners.Count; read++)
        {
            if (_listeners[read].Listener == null)
                continue;
            _listeners[write++] = _listeners[read];
        }
        _listeners.RemoveRange(write, _listeners.Count - write);
    }

    public void ClearPending()
    {
        _pending.Clear();
    }

    public void ClearListeners()
    {
        _listeners.Clear();
        _pendingAdds.Clear();
        _isDispatching = false;
        _hasRemoved = false;
    }
    #endregion
}