using System.Collections.Generic;
using UnityEngine;
using static Define;

/// <summary>
/// 글로벌 입력 매니저.
///
/// 핵심 원칙
///   1. "맵을 켜는 것"과 "수신자를 결합하는 것"은 분리할 수 없는 하나의 동작이다.
///      그 동작이 일어나는 지점은 ApplyActive 하나뿐이다.
///   2. 활성 맵은 언제나 정확히 하나.
///      오버레이가 비어 있으면 gameplay 슬롯, 비어 있지 않으면 최상단 오버레이.
///   3. Villager 등 도메인 타입을 알지 않는다 (의존 방향: 도메인 → Input).
///
/// 상태 구분
///   원본(state) : _gameplayReceiver / _gameplayContext — Acquire, Release가 소유
///                 _overlayStack                        — PushOverlay, PopOverlay가 소유
///   파생(cache) : _appliedReceiver / _activeContext    — ApplyActive만 갱신
///                 원본이 바뀔 때마다 ApplyActive로 다시 계산되며, 직접 대입 금지.
/// </summary>
public class InputManager
{
    public ManagerActionAsset ActionAsset { get; private set; }
    public InputRebindingManager Rebinding { get; private set; }

    /// <summary>지금 실제로 켜져 있는 ActionMap. (UI 오버레이 중이면 EActionMap.UI)</summary>
    public EActionMap ActiveActionMap
    {
        get { return _activeContext != null ? _activeContext.ActionMapType : EActionMap.None; }
    }
    // 모든 ActionMap Handle. 인덱스 = enum 값.
    private readonly IInputControlContext[] _contexts = new IInputControlContext[(int)EActionMap.Count];

    /// <summary>UI 오버레이 스택. 하나라도 있으면 gameplay 맵은 비활성.</summary>
    private readonly List<IUIInputReceiver> _overlayStack = new List<IUIInputReceiver>(/*capacity*/4);
    public bool HasOverlay { get { return _overlayStack.Count > 0; } }

    #region applied
    // [파생] 실제로 Enable된 Map. ApplyActive만 갱신한다.
    private IInputControlContext _activeContext;
    // [파생] 실제로 Reader에 결합된 수신자 (gameplay 또는 UI).
    private IInputReceiver _appliedReceiver;
    /// <summary>지금 실제로 입력을 받는 주체. UI 오버레이 중이면 UI 수신자.</summary>
    public IInputReceiver AppliedReceiver { get { return _appliedReceiver; } }
    #endregion

    #region gameplay
    // [원본] gameplay 슬롯이 요구하는 Map. 오버레이 중에도 값은 보존된다.
    private IInputControlContext _gameplayContext;
    // [원본] gameplay 입력을 소유한 주체. UI가 떠 있어도 유지된다.
    private IInputReceiver _gameplayReceiver;
    /// <summary>gameplay 입력의 소유자. UI 오버레이 중에도 값이 유지되므로, "지금 입력을 받는가"를 묻는 용도로는 쓰지 말 것.</summary>
    public IInputReceiver GameplayReceiver { get { return _gameplayReceiver; } }
    #endregion


    public void Init()
    {
        ActionAsset = new ManagerActionAsset();

        /* 의도적으로 ActionAsset.Enable()을 호출하지 않는다.
         * 전부 켜두면 UI 오버레이가 gameplay 입력을 차단할 수 없다.
         * 컨텍스트 전환과 무관하게 항상 켜둘 맵이 있다면 여기서 개별 Enable 할 것.
         * ex) ActionAsset.전역Input.Enable();*/

        Rebinding = new InputRebindingManager();
        Rebinding.Init(); // 내부에서 LoadBindings 자동 호출

        RegisterContext(new NoneInputReader());
        RegisterContext(new MovementInputReader());
        RegisterContext(new UIInputReader());

        for (int i = 0; i < _contexts.Length; i++)
        {
            Debug.Assert(_contexts[i] != null, $"[InputManager] EActionMap.{(EActionMap)i} 에 대응하는 Reader 미등록");
            _contexts[i].Initialize(ActionAsset);
        }

        // Clear
        _overlayStack.Clear();
        _gameplayReceiver = null;
        _appliedReceiver = null;

        // 초기 상태: None 컨텍스트, 수신자 없음
        _gameplayContext = _contexts[(int)EActionMap.None];
        _activeContext = _contexts[(int)EActionMap.None];
        _activeContext.Enable(null);
    }

    private void RegisterContext(IInputControlContext context)
    {
        int index = (int)context.ActionMapType;
        Debug.Assert(_contexts[index] == null, $"[InputManager] Context 중복 등록 >> {context.ActionMapType}");
        _contexts[index] = context;
    }

    /// <summary>
    /// 수신자를 활성화.
    /// 수신자가 요구하는 Map으로 전환하고 수신자를 결합하는 것을 하나의 동작으로 수행한다.
    /// 같은 수신자 / 같은 Map이면 아무 일도 하지 않는다.
    /// </summary>
    public void Acquire(IInputReceiver receiver)
    {
        if (receiver == null)
        {
            LogPrinter.LogError("[InputManager] Acquire(null). Release를 사용할 것");
            return;
        }
        if (_activeContext == null) // Init 이전이거나 Clear 이후
        {
            LogPrinter.LogError("[InputManager] Init 이전(또는 Clear 이후) Acquire");
            return;
        }

        IInputControlContext next = _contexts[(int)receiver.RequiredContext];
        if (_gameplayReceiver == receiver && _gameplayContext == next)
            return;

        _gameplayReceiver = receiver;
        _gameplayContext = next;

        /* 오버레이가 떠 있으면 슬롯 값만 갱신하고 맵은 건드리지 않는다.
         * (UI가 열린 채 Villager가 상태 전환해도 UI 입력이 끊기지 않는다)*/
        if (HasOverlay == false)
            ApplyActive();
    }

    /// <summary>
    /// 수신자를 해제하고 None 컨텍스트로 돌아간다.
    /// 소유자 검증: 현재 gameplay 소유자가 아니면 무시한다.
    /// (ex. 빙의 A→B 전환 시 콜백 순서가 B.Acquire → A.Release여도 A의 Release가 B를 끊지 못한다)
    /// </summary>
    public void Release(IInputReceiver receiver)
    {
        if (receiver == null || _gameplayReceiver != receiver)
            return;

        _gameplayReceiver = null;
        _gameplayContext = _contexts[(int)EActionMap.None];

        if (HasOverlay == false)
            ApplyActive();
    }

    /// <summary>UI를 열 때 호출. gameplay 입력이 차단되고 MAui가 활성화된다.</summary>
    public void PushOverlay(IUIInputReceiver overlay)
    {
        if (overlay == null)
            return;
        if (_activeContext == null)
        {
            LogPrinter.LogError("[InputManager] Init 이전(또는 Clear 이후) PushOverlay");
            return;
        }

        int existing = _overlayStack.IndexOf(overlay);
        if (existing >= 0)
        {
            if (existing == _overlayStack.Count - 1)
                return; // 이미 최상단

            _overlayStack.RemoveAt(existing); // 후에 재Push = 최상단으로 끌어올림
        }

        _overlayStack.Add(overlay);
        ApplyActive();
    }

    /// <summary>
    /// UI를 닫을 때 호출. 최상단이 아니어도 스택 중간에서 제거할 수 있다
    /// (팝업 여러 개가 임의 순서로 닫히는 경우 대응).
    /// </summary>
    public void PopOverlay(IUIInputReceiver overlay)
    {
        if (overlay == null)
            return;

        int index = _overlayStack.IndexOf(overlay);
        if (index < 0)
            return;

        bool wasTop = (index == _overlayStack.Count - 1);
        _overlayStack.RemoveAt(index);

        if (wasTop) // 중간 제거는 활성 맵에 영향 없음 → 불필요한 Disable/Enable 회피
            ApplyActive();
    }

    /// <summary>
    /// 현재 활성 Map과 수신자를 결정해 반영한다. 전환이 일어나는 유일한 지점.
    /// 우선순위: (1)오버레이 최상단  (2)gameplay 슬롯
    /// </summary>
    private void ApplyActive()
    {
        IInputReceiver nextReceiver;
        IInputControlContext nextContext;

        if (HasOverlay)
        {
            nextReceiver = _overlayStack[_overlayStack.Count - 1];
            nextContext = _contexts[(int)EActionMap.UI];
        }
        else
        {
            nextReceiver = _gameplayReceiver;
            nextContext = _gameplayContext;
        }

        if (nextContext == null) // Clear 이후 잔여 호출 방어
            return;

        if (_activeContext == nextContext && _appliedReceiver == nextReceiver)
            return;

        // 1. 이전 맵 off → 진행 중 액션의 canceled가 아직 결합된 이전 수신자에게 전달된다 (정지 신호)
        _activeContext?.Disable();

        // 2. 파생 상태 확정. 맵을 켜기 전에 갱신한다.
        _appliedReceiver = nextReceiver;
        _activeContext = nextContext;

        // 3. 새 맵 on + 수신자 결합
        nextContext.Enable(nextReceiver);

        LogPrinter.Log($"[InputManager] Active >> {nextContext.ActionMapType} / {(nextReceiver != null ? nextReceiver.GetType().Name : "null")} (overlay {_overlayStack.Count})");
    }

    public void Clear()
    {
        Rebinding?.CancelCurrentRebinding();
        _activeContext?.Disable();

        /* 활성 상태 캐시를 전부 무효화한다.
         * 하나라도 남으면 ApplyActive의 조기 return이 맵이 이미 켜져 있다고 오판.*/
        _overlayStack.Clear();
        _gameplayReceiver = null;
        _appliedReceiver = null;
        _activeContext = null;
        _gameplayContext = null;

        ActionAsset?.Disable(); // 안전망
    }
}