using UnityEngine;

/// <summary>지정 시간 대기. Skill_N의 "3초 뒤 예고" 구간 등에 사용.</summary>
[CreateAssetMenu(menuName = "Data/Skill/Phase/Wait Seconds", fileName = "P_Wait_")]
public class WaitSecondsPhaseSO : SkillPhaseSO
{
    [Tooltip("대기 시간 (초)")]
    [SerializeField] private float _duration = 1f;

    public override EPhaseState Tick(SkillPhaseContext ctx, float deltaTime)
    {
        return ctx.PhaseElapsed >= _duration ? EPhaseState.Completed : EPhaseState.Running;
    }
}