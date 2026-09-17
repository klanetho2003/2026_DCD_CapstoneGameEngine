using UnityEngine;

/// <summary>
/// Animation Event(OnSkillAnimationEnd) 신호를 기다림.
/// </summary>
[CreateAssetMenu(menuName = "Data/Skill/Phase/Wait Animation End", fileName = "P_WaitAnimEnd_")]
public class WaitForAnimationEndPhaseSO : SkillPhaseSO
{
    [Tooltip("안전망 타임아웃(초). 0 이하면 무제한.")]
    [SerializeField] private float _timeout = 10f;

    public override EPhaseState Tick(SkillPhaseContext ctx, float deltaTime)
    {
        if (ctx.AnimationEndReceived)
        {
            ctx.AnimationEndReceived = false; // 초기화 - 다음 wait phase를 위해
            return EPhaseState.Completed;
        }

        if (_timeout > 0f && ctx.PhaseElapsed >= _timeout)
        {
            LogPrinter.LogWarning(this, $"[Phase] AnimationEnd 타임아웃 — SkillID {ctx.Skill.Definition.SkillID}. Event 누락 여부 확인.");
            return EPhaseState.Completed;
        }

        return EPhaseState.Running;
    }
}