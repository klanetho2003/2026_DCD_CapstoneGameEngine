using System;
using UnityEngine;
using UnityEngine.AdaptivePerformance.Provider;
using UnityEngine.UIElements;
using static Define;
using static UnityEngine.UI.GridLayoutGroup;

/// <summary>
/// Effect Apply 시 전달되는 정보.
/// Skill 실행 시 한 번 생성되어 모든 Effect에 전달.
/// </summary>
public struct SkillExecutionContext
{
    public CombatCreature Caster;
    public CombatCreature Target;
    public SkillInstance Skill;
}

/// <summary>
/// 캐릭터별 스킬 런타임 상태.
/// 
/// - RemainingCooldown: 쿨다운 잔여 시간 (스킬 상태)
/// - 캐릭터의 시전 상태는 ECreatureTag.Attacking/Casting
/// </summary>
public class SkillInstance
{
    public SkillDefinitionSO Definition { get; }
    public CombatCreature Owner { get; }

    public float RemainingCooldown { get; private set; }
    public bool IsReady { get { return RemainingCooldown <= 0f; } }
    public bool IsRunning { get; private set; } = false;
    public bool IsCasting { get; private set; }

    /// <summary>
    /// Cooldown 값 변경 시 Invoke. UI 갱신 등에 사용.
    /// </summary>
    public event Action<float> OnCooldownChanged;

    public SkillInstance(SkillDefinitionSO definition, CombatCreature owner)
    {
        Definition = definition;
        Owner = owner;
        RemainingCooldown = 0f;
    }

    /// <summary>
    /// 이 스킬이 외부 캔슬을 허용하는가
    /// </summary>
    public bool CanBeCancelled
    {
        get
        {
            if (IsCasting)  // 시전 전이면 cancel 가능
                return true;

            if (Definition is UserSkillDefinitionSO playerDef)
                return playerDef.IsCanCancelThisSkill;

            return true;    // Monster 스킬 >> 캔슬 게이트는 BT(Maintain)가 담당하므로 항상 허용
        }
    }

    /// <summary>
    /// 매 프레임 호출 (SkillBook이 위임). Cooldown 감소.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (RemainingCooldown <= 0f)
            return;

        RemainingCooldown -= deltaTime;
        if (RemainingCooldown < 0f)
            RemainingCooldown = 0f;

        OnCooldownChanged?.Invoke(RemainingCooldown);
    }

    public bool TryCasting()
    {
        var castingPhases = Definition.CastingPhases;

        if (castingPhases != null && castingPhases.Count == 0)
            return TryUse(); // Casting이 없으면 실 사용부로 이전

        if (CanUse() == false)
            return false;
        if (Owner.HasTag(Definition.InValidTags))
            return false;

        Owner.SkillBook.InterruptRunningSkill(); // 기존 스킬 정리
        IsCasting = true;
        IsRunning = true;
        Owner.AddTag(ECreatureTag.UsingSkill);
        Owner.SkillBook.RegisterRunningSkill(this); // 스킬 등록

        // CastingPhases가 있을 때만 시퀀스 구동.
        // 비어 있으면 외부 TryUse 호출을 기다린다.
        Owner.SkillBook.PhaseRunner.Begin(this, castingPhases);

        return true;
    }

    /// <summary>
    /// Skill Book을 통해서 시전. 직접 호출 금지. Effect 일괄 적용
    /// </summary> 
    private bool TryUse()
    {
        bool fromCasting = IsCasting;

        if (fromCasting)
        {
            IsCasting = false;
            Owner.CreatureAnim?.ClearAnim(); // 초기화
        }
        else
        {
            if (CanUse() == false)
            {
                LogPrinter.Log($"[Skill] {Owner.gameObject.name} — SkillID {Definition.SkillID} 시전 불가 (IsReady={IsReady})");
                return false;
            }

            if (Owner.HasTag(Definition.InValidTags))
            {
                LogPrinter.Log($"[Skill] {Owner.gameObject.name} — SkillID {Definition.SkillID} 시전 불가. 이유 > Tag");
                return false;
            }

            Owner.SkillBook.InterruptRunningSkill(); // 해제
            IsRunning = true;
            Owner.AddTag(ECreatureTag.UsingSkill);
            Owner.SkillBook.RegisterRunningSkill(this); // 등록
        }

        // Effect 일괄 적용
        var context = new SkillExecutionContext
        {
            Caster = Owner,
            Skill = this,
        };

        // 특정 Target Damage & Effect
        // ApplyTargetedEffects(context);

        // Cooldown 시작
        RemainingCooldown = Definition.CooldownTime;
        OnCooldownChanged?.Invoke(RemainingCooldown);

        Owner.SkillBook.PhaseRunner.Begin(this, Definition.UsePhases);

        LogPrinter.Log($"[Skill] {Owner.gameObject.name} >>> SkillID {Definition.SkillID} ({Definition.SkillNameID}) 시전");

        return true;
    }

    /// <summary>
    /// Hitbox 충돌 시 Hitbox가 호출. HitEffects 순회하며 적용.
    /// </summary>
    public void OnHitboxCollision(Hurtbox hurtbox)
    {
        if (Definition.HitEffects == null) return;

        for (int i = 0; i < Definition.HitEffects.Count; i++)
            Definition.HitEffects[i]?.ApplyOnHit(this, hurtbox);
    }

    /// <summary>
    /// 시전 가능 여부.
    /// </summary>
    private bool CanUse()
    {
        if (Owner == null || Owner.IsDead)
            return false;
        if (IsReady == false)
            return false;
        return true;
    }

    public bool TryCancel()
    {
        if (CanBeCancelled == false)
            return false;

        OnEndSkill();

        return true;
    }

    /// <summary>
    /// PhaseRunner가 시퀀스를 자연 완료했을 때 호출. 중단(캔슬/사망)은 이 경로로 오지 않는다.
    /// casting 시퀀스였다면 TryUse로, TryUse 시퀀스였다면 스킬 종료 단계로 이동한다.
    /// </summary>
    public void OnPhaseSequenceComplete()
    {
        if (IsCasting == false)
        {
            OnEndSkill();
            return;
        }

        // casting 완료 >> 본 시전. TryUse 내부에서 fromCasting 경로를 탄다.
        if (TryUse() == false)
            OnEndSkill();   // 방어. TryUse가 IsCasting을 먼저 false로 만들므로 재진입 없음
    }

    public void OnEndSkill(Action additionalFunc = null)
    {
        if (IsRunning == false)
            return;

        IsRunning = false;
        IsCasting = false;

        Owner.SkillBook?.PhaseRunner.StopIfRunning(this);

        // Hitbox
        Owner.ActivaedHitboxGroup?.SetActive(false);
        Owner.ClearActiveHitbox();
        // Hurtbox
        Owner.CreatureAnim?.ClearAnim();
        Owner.RevertToDefaultHurtboxLayout();

        // 추가 후처리
        additionalFunc?.Invoke();

        // clear
        Owner.RemoveTag(ECreatureTag.UsingSkill);
        Owner.SkillBook.UnregisterRunningSkill(this);
    }

    /// <summary>
    /// Animation Event "OnAnimationEnd"의 스킬 수신점.
    /// 시퀀스가 이 스킬에 관해 실행 중이면 신호만 전달하고, 종료 여부는 phase가 판단한다.
    /// </summary>
    public void HandleAnimationEnd()
    {
        var runner = Owner.SkillBook.PhaseRunner;

        if (runner.IsActive && runner.RunningSkill == this)
        {
            // phase에 animation 끝났다고만 알려주고
            // 실질적인 종료는 phase 부품 중 하나인 WaitForAnimationEndPhase에서 정료
            runner.OnAnimationEnd();
            return;
        }

        // fall back (phase가 없는 skill인 경우 여기에서 종료 처리)
        OnEndSkill();
    }

    /// <summary>
    /// Pool 재사용 시 호출.
    /// </summary>
    public void Reset()
    {
        RemainingCooldown = 0f;
        IsRunning = false;
        IsCasting = false;
        OnCooldownChanged = null;
    }
}