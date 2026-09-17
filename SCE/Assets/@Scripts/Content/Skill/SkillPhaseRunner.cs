using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 다단계 스킬의 구동자. SkillBook당 1개 소유, Update에서 Tick.
/// 
/// - Animation Event에 의존. 스킬을 직접 끝내지 못함.
/// - 외부 중단(캔슬/사망/새 스킬)은 OnEndSkill >> StopIfRunning 경로로 들어와
///   현재 phase의 OnInterrupt 정리 후 정지
/// </summary>
public class SkillPhaseRunner
{
    private readonly SkillPhaseContext _ctx = new();
    private IReadOnlyList<SkillPhaseSO> _phases;
    private int _index = -1;

    public bool IsActive { get; private set; }
    public SkillInstance RunningSkill { get; private set; }

    #region Fall Back
    private static SkillPhaseSO[] _fallbackPhases;

    private static IReadOnlyList<SkillPhaseSO> GetFallbackPhases()
    {
        return _fallbackPhases ??= new SkillPhaseSO[]
        {
            ScriptableObject.CreateInstance<WaitForAnimationEndPhaseSO>() // timeout 기본값 사용
        };
    }
    #endregion

    /// <summary>Phases 보유 스킬이면, SkillInstance.TryUse에서 호출.</summary>
    public void Begin(SkillInstance skill, IReadOnlyList<SkillPhaseSO> phases)
    {
        // 방어
        if (IsActive)
            StopIfRunning(RunningSkill);

        if (phases == null || phases.Count == 0)
            phases = GetFallbackPhases();

        _ctx.Reset(skill.Owner, skill);
        _phases = phases;
        RunningSkill = skill;
        IsActive = true;

        EnterPhase(0);
        Tick(0f);
    }

    /// <summary>SkillBook.Update에서 매 프레임.</summary>
    public void Tick(float deltaTime)
    {
        if (IsActive == false)
            return;

        _ctx.PhaseElapsed += deltaTime;

        while (IsActive)
        {
            // 작동 중인 phase가 있을 시 넘어가지 않음
            if (_phases[_index].Tick(_ctx, deltaTime) == EPhaseState.Running)
                return;

            int next = _index + 1;
            if (next < _phases.Count)
            {
                EnterPhase(next);
                deltaTime = 0f; // 새 phase는 이번 프레임 시간 소비 없이 진입 — 즉발만 연쇄, Wait는 다음 프레임부터 계측
                continue;
            }

            var finished = RunningSkill;
            Deactivate();
            finished.OnEndSkill();
            return;
        }
    }

    /// <summary>CombatCreature.InterruptRunningSkill >> SkillInstance.HandleAnimationEnd에 위임.</summary>
    public void OnAnimationEnd()
    {
        if (IsActive == false)
            return;

        _ctx.AnimationEndReceived = true;   // wait phase가 소비
    }

    /// <summary>
    /// 외부 중단 경로. OnEndSkill이 호출 — 해당 스킬의 실행일 때만 정지.
    /// </summary>
    public void StopIfRunning(SkillInstance skill)
    {
        if (IsActive == false) return;
        if (RunningSkill != skill) return;

        _phases[_index].OnInterrupt(_ctx);
        Deactivate();
    }

    private void EnterPhase(int index)
    {
        _index = index;
        _ctx.PhaseElapsed = 0f;
        _ctx.PhaseData = null;

        _phases[index].OnEnter(_ctx);
    }

    private void Deactivate()
    {
        _ctx.RunCleanups();

        IsActive = false;
        RunningSkill = null;
        _phases = null;
        _index = -1;
    }
}