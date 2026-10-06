using System.Collections.Generic;
using UnityEngine;
using static Define;

/// <summary>
/// 정의 저장소 + 트리거 디스패처.
/// Tick / InputInteract는 여기서 트리거별 리스트로 뿌리고, Zone은 NPC → 컴포넌트로 직접 간다.
/// </summary>
public class InteractionManager
{
    /// <summary>interaction json을 객체화해서 id에 instance를 맵핑</summary>
    private readonly Dictionary<string, InteractionSetDefinition> _sets = new();

    /// <summary>
    /// 트리거별 등록 리스트. 컴포넌트는 자기 TriggerMask에 해당하는 리스트에만 들어간다.
    /// 
    /// _byTrigger[0] = Tick에 반응하는 NPC들 목록
    /// _byTrigger[1] = InputInteract에 반응하는 NPC들 목록
    /// </summary>
    private readonly List<InteractionComponent>[] _byTrigger = new List<InteractionComponent>[(int)ETriggerType.Count];

    // 디스패치 도중 Unregister(효과가 NPC를 디스폰) → 순회 안전을 위해 보류
    private bool _isDispatching;
    private readonly List<InteractionComponent> _pendingUnregister = new();

    /// <summary>이 매니저가 목록으로 디스패치하는 트리거. 나머지(Zone, SpawnRequest, Spawned)는 개별 경로로 호출된다.</summary>
    private const int DispatchedTriggerMask =
        (1 << (int)ETriggerType.Tick) |
        (1 << (int)ETriggerType.InputInteract) |
        (1 << (int)ETriggerType.StateChanged);

    #region StateChanged
    private bool _hasStateVersion; // 첫 비교 전 — sentinel 값 대신 플래그
    private int _lastStateVersion;

    // 새로 등록된 StateChanged 컴포넌트의 초기 평가 대기열. 처리 중 등록분이 섞이지 않도록 두 개를 교체해 쓴다
    private List<InteractionComponent> _pendingStateInit = new();
    private List<InteractionComponent> _pendingStateInitSwap = new();
    #endregion

    public void Init()
    {
        for (int i = 0; i < _byTrigger.Length; i++)
            _byTrigger[i] = new List<InteractionComponent>(64);
    }

    #region 정의
    /// <summary>JSON 텍스트 1개 = Set 1개. 상태 정의(GameState.LoadDefinitions)가 먼저 로드되어 있어야 한다.</summary>
    public bool LoadSet(string json)
    {
        var state = Managers.GameState;
        if (state == null || state.IsLoaded == false)
        {
            LogPrinter.LogError("[InteractionManager] 상태 정의 로드 전 — GameState.LoadDefinitions를 먼저 호출할 것");
            return false;
        }

        InteractionSetDefinition set = InteractionLoader.Parse(json, state.Registry);
        return set != null && AddSet(set);
    }

    public bool AddSet(InteractionSetDefinition set)
    {
        if (set == null)
            return false;
        if (_sets.ContainsKey(set.Id))
        {
            LogPrinter.LogError($"[InteractionManager] Set Id 중복 >> {set.Id}");
            return false;
        }
        _sets.Add(set.Id, set);
        return true;
    }

    public InteractionSetDefinition GetSet(string id)
    {
        return id != null && _sets.TryGetValue(id, out InteractionSetDefinition set) ? set : null;
    }
    #endregion

    #region 등록
    public void Register(InteractionComponent component)
    {
        int mask = component.TriggerMask & DispatchedTriggerMask;
        for (int t = 0; t < _byTrigger.Length; t++) // 모든 트리거 타입(ETriggerType)을 순회
        {
            // 1. 이 컴포넌트가 t번째 트리거를 가지고 있는지 비트 AND(&) 연산으로 확인
            if ((mask & (1 << t)) == 0)
                continue;
            List<InteractionComponent> list = _byTrigger[t]; // 참조형

            // 2. 리스트에 없다면 추가 (중복 등록 방지)
            if (list.Contains(component) == false) // 재SetInfo 방어
                list.Add(component);
        }

        // 상태가 더 바뀌지 않아도 현재 상태로 1회 평가되도록 — 다음 프레임 (SetInfo 도중 효과 실행 방지)
        if ((mask & (1 << (int)ETriggerType.StateChanged)) != 0 && _pendingStateInit.Contains(component) == false)
            _pendingStateInit.Add(component);
    }

    public void Unregister(InteractionComponent component)
    {
        if (_isDispatching)
        {
            _pendingUnregister.Add(component);
            return;
        }

        for (int t = 0; t < _byTrigger.Length; t++)
            _byTrigger[t].Remove(component); // 디스폰은 드묾 → O(N) 허용
        _pendingStateInit.Remove(component);
    }
    #endregion

    #region 디스패치
    /// <summary>매 프레임. Managers의 Update 구동 지점에서 호출.</summary>
    public void OnUpdate()
    {
        CreatureBase instigator = Managers.Object.PossessedTarget;
        DispatchStateChanged(instigator);
        Dispatch(ETriggerType.Tick, instigator);
    }

    /// <summary>MovementHandler.OnInteractInput에서 호출</summary>
    public void RaiseInput(CreatureBase instigator)
    {
        Dispatch(ETriggerType.InputInteract, instigator);
    }

    /// <summary>
    /// 상태가 바뀌었으면 전체 1회, 아니면 새로 등록된 컴포넌트만 1회.
    /// 효과가 신호를 발생시켜 상태가 바뀌어도 이번 프레임에 다시 디스패치하지 않는다 — 다음 프레임 1회로 합쳐진다.
    /// </summary>
    private void DispatchStateChanged(CreatureBase instigator)
    {
        var state = Managers.GameState;
        if (state == null || state.IsLoaded == false)
            return;

        int version = state.ChangeVersion;
        if (_hasStateVersion == false || version != _lastStateVersion)
        {
            _hasStateVersion = true;
            _lastStateVersion = version;
            _pendingStateInit.Clear(); // 전체 디스패치가 신규 등록분도 포함한다
            Dispatch(ETriggerType.StateChanged, instigator);
            return;
        }

        // 변경 없음 — 비용은 정수 비교 1회
        if (_pendingStateInit.Count == 0)
            return;

        // 상태는 그대로지만 새로 등록된 NPC가 있으면 그 NPC만 1회 평가
        (_pendingStateInit, _pendingStateInitSwap) = (_pendingStateInitSwap, _pendingStateInit);
        DispatchList(_pendingStateInitSwap, ETriggerType.StateChanged, instigator);
        _pendingStateInitSwap.Clear();
    }

    private void Dispatch(ETriggerType trigger, CreatureBase instigator)
    {
        DispatchList(_byTrigger[(int)trigger], trigger, instigator);
    }

    private void DispatchList(List<InteractionComponent> list, ETriggerType trigger, CreatureBase instigator)
    {
        if (list.Count == 0)
            return;

        float time = Time.time;
        _isDispatching = true;

        int count = list.Count; // 디스패치 중 Register된 것은 다음 프레임부터
        for (int i = 0; i < count; i++)
            list[i].OnTrigger(trigger, instigator, time);

        _isDispatching = false;

        if (_pendingUnregister.Count > 0)
        {
            for (int i = 0; i < _pendingUnregister.Count; i++)
                Unregister(_pendingUnregister[i]);
            _pendingUnregister.Clear();
        }
    }
    #endregion

    /// <summary>씬 전환 시. 정의는 유지, 등록만 비운다.</summary>
    public void Clear()
    {
        for (int t = 0; t < _byTrigger.Length; t++)
            _byTrigger[t]?.Clear();
        _pendingUnregister.Clear();
        _pendingStateInit.Clear();
        _pendingStateInitSwap.Clear();
        _isDispatching = false;
        _hasStateVersion = false; // 다음 씬 첫 프레임에 전체 평가
    }
}