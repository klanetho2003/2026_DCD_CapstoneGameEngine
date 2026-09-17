using System;
using UnityEngine;
using static Define;

/// <summary>
/// 상호작용 능력 = GAS의 AbilitySystemComponent.
/// CreatureBase 계열에 붙는 컴포넌트.
/// 정의(InteractionSetDefinition)는 공유하고 상태(InteractionRuntime[])만 이 인스턴스가 가진다.
/// </summary>
[DisallowMultipleComponent]
public sealed class InteractionComponent : InitBase
{
    private CreatureBase _owner;
    private InteractionSetDefinition _set;
    private InteractionRuntime[] _runtimes = Array.Empty<InteractionRuntime>();

    /// <summary>이 NPC가 가진 트리거 종류 비트. InteractionManager가 리스트 등록에 사용.</summary>
    public int TriggerMask { get; private set; }

    /// <summary>트리거 존 안의 대상. 단일 점유 가정 (빙의 대상만 추적).</summary>
    private CreatureBase _zoneOccupant;

    public bool IsInZone(CreatureBase creature)
    {
        return creature != null && _zoneOccupant == creature;
    }

    /// <summary>NPC.SetInfo에서 호출. 같은 Set으로 재스폰이면 배열을 재사용한다 (풀링 시 GC 0).</summary>
    public void SetInfo(CreatureBase owner, InteractionSetDefinition set)
    {
        bool sameSet = (_set == set) && (_owner == owner) && (set != null);

        Clear();
        _owner = owner;
        _set = set;

        if (set == null)
            return;

        if (sameSet == false)
        {
            InteractionDefinition[] defs = set.Interactions;
            _runtimes = new InteractionRuntime[defs.Length];
            for (int i = 0; i < defs.Length; i++)
                _runtimes[i] = new InteractionRuntime(defs[i]); // effect instance 생성
        }
        else
        {
            for (int i = 0; i < _runtimes.Length; i++)
                _runtimes[i].Reset();
        }

        int mask = 0;
        for (int i = 0; i < _runtimes.Length; i++)
            mask |= 1 << (int)_runtimes[i].Definition.Trigger;
        TriggerMask = mask;

        Managers.Interaction.Register(this);
    }

    /// <summary>디스폰/파괴 시. 켜져 있는 Latched 효과를 끈다</summary>
    public void Clear()
    {
        if (_owner != null && _runtimes.Length > 0)
        {
            InteractionContext ctx = new InteractionContext(this, _owner, null, ETriggerType.Tick, Time.time);
            for (int i = 0; i < _runtimes.Length; i++)
            {
                if (_runtimes[i].IsActive)
                    Deactivate(_runtimes[i], in ctx);
            }
            Managers.Interaction?.Unregister(this);
        }

        // sameSet 재사용을 위해 _runtimes 배열은 유지. 정의가 바뀌면 SetInfo가 새로 만든다.
        TriggerMask = 0;
        _zoneOccupant = null;
    }

    private void OnDisable()
    {
        Clear();
    }

    #region Trigger 진입점
    /// <summary>InteractionManager가 Tick / InputInteract 디스패치 시 호출</summary>
    public void OnTrigger(ETriggerType trigger, CreatureBase instigator, float time)
    {
        // 빙의된 Villager 자신은 촉발 주체가 될 수 없다.
        // early return이 아니라 instigator를 null로 바꿔 평가를 계속하는 이유:
        // 켜져 있던 Latched 효과가 조건 실패로 정상 Deactivate 되어야 하기 때문.
        if (instigator == _owner)
            instigator = null;

        InteractionContext ctx = new InteractionContext(this, _owner, instigator, trigger, time);
        EvaluateAll(trigger, in ctx);
    }

    /*/// <summary>NPC.OnTriggerEnter2D에서 호출</summary>
    public void OnZoneEnter(CreatureBase creature)
    {
        _zoneOccupant = creature;
        OnTrigger(ETriggerType.Zone, creature, Time.time); // 진입 후 상태로 평가 → InZone == true
    }

    /// <summary>NPC.OnTriggerExit에서 호출</summary>
    public void OnZoneExit(CreatureBase creature)
    {
        if (_zoneOccupant == creature)
            _zoneOccupant = null;
        OnTrigger(ETriggerType.Zone, creature, Time.time); // 이탈 후 상태로 평가 → InZone == false
    }*/

    private void OnTriggerEnter2D(Collider2D other)
    {
        CreatureBase target = Managers.Object.PossessedTarget;
        if (target == null || other.gameObject != target.gameObject)
            return;

        _zoneOccupant = target;
        OnTrigger(ETriggerType.Zone, target, Time.time); // 진입 후 상태로 평가 → InZone == true
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        CreatureBase target = Managers.Object.PossessedTarget;
        if (target == null || other.gameObject != target.gameObject)
            return;

        if (_zoneOccupant == target)
            _zoneOccupant = null;
        OnTrigger(ETriggerType.Zone, target, Time.time); // 이탈 후 상태로 평가 → InZone == false
    }
    #endregion

    #region 평가
    private void EvaluateAll(ETriggerType trigger, in InteractionContext ctx)
    {
#if UNITY_EDITOR
        if (InteractionDiagnostics.IsWatching(this))
            BeginDiagnose(trigger, ctx.Time);
#endif

        for (int i = 0; i < _runtimes.Length; i++)
        {
            InteractionRuntime rt = _runtimes[i];
            if (rt.Definition.Trigger != trigger) // Definition은 instance가 하나이고 pointer로 참조 중
                continue;
            Evaluate(rt, in ctx, i);
        }
    }

    /// <summary>
    /// 실행 조건을 check하고, 조건에 부합할 시 interaction 적용. 부합하지 않을 시 pass
    /// </summary>
    /// <param name="rt">주체 npc가 가지고 있는 interaction 목록 중 지금 trigger에 적합한 instance</param>
    /// <param name="ctx">현재 상황 정보를 담고 있는 구조체</param>
    /// <param name="index">debug에 사용되는 값</param>
    private void Evaluate(InteractionRuntime rt, in InteractionContext ctx, int index)
    {
        // rt.Definition : 여러 마리 NPC들이 가르키는 공용 원본 포인터(조건들이 있음)
        InteractionDefinition def = rt.Definition;

        // Check Condition
#if UNITY_EDITOR
        bool conditionsPass = InteractionDiagnostics.IsWatching(this)
            ? EvaluateConditionsDiagnosed(def.Conditions, in ctx, index)
            : AllConditions(def.Conditions, in ctx);
#else
        bool conditionsPass = AllConditions(def.Conditions, in ctx);
#endif

        // 여기서부터 실행처리 시작
        if (def.Mode == EActivationMode.Latched)
        {
            if (rt.IsActive) // 켜져 있으면 조건만 본다. 쿨다운&횟수는 새로 켤 수 있는가에만 관여.
            {
                if (conditionsPass == false)
                    Deactivate(rt, in ctx);
            }
            else if (conditionsPass && PassesGate(rt, def, ctx.Time))
            {
                Activate(rt, in ctx);
            }
        }
        else if (conditionsPass && PassesGate(rt, def, ctx.Time))
        {
            Activate(rt, in ctx);
        }

#if UNITY_EDITOR
        RecordResult(index, conditionsPass, rt.IsActive);
#endif
    }

    /// <summary>interaction 내부에서 관리하는 조건에 부합하는지 check</summary>
    private static bool PassesGate(InteractionRuntime rt, InteractionDefinition def, float time)
    {
        if (time < rt.CooldownUntil)
            return false;
        if (def.MaxActivations > 0 && rt.ActivationCount >= def.MaxActivations)
            return false;

        return true;
    }

    private static bool AllConditions(InteractionCondition[] conditions, in InteractionContext ctx)
    {
        for (int i = 0; i < conditions.Length; i++)
        {
            if (conditions[i].Evaluate(in ctx) == false)
                return false; // early-out — 배열 순서가 곧 평가 순서
        }
        return true;
    }

    private static void Activate(InteractionRuntime rt, in InteractionContext ctx)
    {
        rt.IsActive = true; // Fire 모드에서는 읽지 않지만 대입은 무해
        rt.ActivationCount++;
        rt.CooldownUntil = ctx.Time + rt.Definition.Cooldown;

        InteractionEffect[] effects = rt.Effects;
        for (int i = 0; i < effects.Length; i++)
            effects[i].OnActivate(in ctx);

        if (rt.Definition.Mode == EActivationMode.Fire)
            rt.IsActive = false;
    }

    private static void Deactivate(InteractionRuntime rt, in InteractionContext ctx)
    {
        rt.IsActive = false;

        InteractionEffect[] effects = rt.Effects;
        for (int i = effects.Length - 1; i >= 0; i--) // 켠 순서의 역순으로 끈다
            effects[i].OnDeactivate(in ctx);
    }
    #endregion

#if UNITY_EDITOR
    #region 진단 (에디터 전용)
    /// <summary>진단 시에는 early-out 없이 모든 조건을 평가한다 — 어느 조건이 막는지 봐야 하기 때문.</summary>
    private bool EvaluateConditionsDiagnosed(InteractionCondition[] conditions, in InteractionContext ctx, int index)
    {
        var results = InteractionDiagnostics.Current.ConditionResults;
        while (results.Count <= index) results.Add(null);
        if (results[index] == null || results[index].Length != conditions.Length)
            results[index] = new bool[conditions.Length];

        bool all = true;
        bool[] slot = results[index];
        for (int i = 0; i < conditions.Length; i++)
        {
            bool value = conditions[i].Evaluate(in ctx);
            slot[i] = value;
            if (value == false) all = false;
        }
        return all;
    }

    private void BeginDiagnose(ETriggerType trigger, float time)
    {
        InteractionDiagnostics.Record record = InteractionDiagnostics.Current;
        record.LastEvaluateTime = time;
        record.LastFrame = Time.frameCount;

        while (record.Pass.Count < _runtimes.Length) record.Pass.Add(false);
        while (record.Active.Count < _runtimes.Length) record.Active.Add(false);
    }

    private void RecordResult(int index, bool pass, bool active)
    {
        if (InteractionDiagnostics.IsWatching(this) == false) return;
        InteractionDiagnostics.Record record = InteractionDiagnostics.Current;
        if (index < record.Pass.Count) record.Pass[index] = pass;
        if (index < record.Active.Count) record.Active[index] = active;
    }

    public InteractionRuntime GetRuntime(int index)
    {
        return (uint)index < (uint)_runtimes.Length ? _runtimes[index] : null;
    }
    public int RuntimeCount { get { return _runtimes.Length; } }
    public InteractionSetDefinition Set { get { return _set; } }
    #endregion
#endif
}