using System;
using System.Collections.Generic;
using UnityEngine;

public enum EPhaseState
{
    Running,     // 진행 중 — 다음 Tick에 재호출
    Completed,   // 완료 — Runner가 다음 phase로 진행
}

/// <summary>
/// 다단계 스킬 타임라인의 단계
/// </summary>
public abstract class SkillPhaseSO : ScriptableObject
{
    /// <summary>phase 진입 시 1회.</summary>
    public virtual void OnEnter(SkillPhaseContext ctx) { }

    /// <summary>매 프레임. Completed 반환 시 Runner가 다음 phase로.</summary>
    public abstract EPhaseState Tick(SkillPhaseContext ctx, float deltaTime);

    /// <summary>
    /// 중단되었을 경우(캔슬/사망/새 스킬) 시 정리.
    /// 자연 완료(Completed 반환) 시에는 호출되지 않음.
    /// </summary>
    public virtual void OnInterrupt(SkillPhaseContext ctx) { }
}

/// <summary>
/// 스킬 실행 1회분의 가변 상태. Runner가 소유&재사용
/// </summary>
public class SkillPhaseContext
{
    public CombatCreature Caster;
    public SkillInstance Skill;

    /// <summary>phase 진입 후 경과 시간. Runner가 진입 시 0, 매 Tick 누적.</summary>
    public float PhaseElapsed;

    /// <summary>
    /// WaitForAnimationEndPhase가 소비(false로).
    /// </summary>
    public bool AnimationEndReceived;

    /// <summary>phase 전환 시 Runner가 null 처리.</summary>
    public object PhaseData;

    /// <summary>
    /// 스킬 종료 시 정리 작업. VFX 등 실행 중 스폰한 자원의 회수 등록
    /// </summary>
    private readonly List<Action> _cleanups = new(4);
    public void RegisterCleanup(Action func) { _cleanups.Add(func); }

    public void RunCleanups()
    {
        for (int i = _cleanups.Count - 1; i >= 0; i--)   // 등록 역순 - 자원 해제
            _cleanups[i]?.Invoke();
        _cleanups.Clear();
    }

    public void Reset(CombatCreature caster, SkillInstance skill)
    {
        RunCleanups();  // 방어
        Caster = caster;
        Skill = skill;
        PhaseElapsed = 0f; AnimationEndReceived = false; PhaseData = null;
    }
}