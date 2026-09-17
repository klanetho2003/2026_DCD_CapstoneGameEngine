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

    public void Init()
    {
        for (int i = 0; i < _byTrigger.Length; i++)
            _byTrigger[i] = new List<InteractionComponent>(64);
    }

    #region 정의
    /// <summary>JSON 텍스트 1개 = Set 1개. 프로젝트의 TextAsset 로딩 경로에서 호출.</summary>
    public bool LoadSet(string json)
    {
        InteractionSetDefinition set = InteractionLoader.Parse(json);
        return set != null && AddSet(set);
    }

    public bool AddSet(InteractionSetDefinition set)
    {
        if (set == null) return false;
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
        int mask = component.TriggerMask;
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
    }
    #endregion

    #region 디스패치
    /// <summary>매 프레임. Managers의 Update 구동 지점에서 호출.</summary>
    public void OnUpdate()
    {
        Dispatch(ETriggerType.Tick, Managers.Object.PossessedTarget);
    }

    /// <summary>MovementHandler.OnInteractInput에서 호출</summary>
    public void RaiseInput(CreatureBase instigator)
    {
        Dispatch(ETriggerType.InputInteract, instigator);
    }

    private void Dispatch(ETriggerType trigger, CreatureBase instigator)
    {
        List<InteractionComponent> list = _byTrigger[(int)trigger];
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

    /// <summary>씬 전환 시. 정의는 유지, 등록만 비운다 (정의는 씬과 무관한 데이터).</summary>
    public void Clear()
    {
        for (int t = 0; t < _byTrigger.Length; t++)
            _byTrigger[t]?.Clear();
        _pendingUnregister.Clear();
        _isDispatching = false;
    }
}