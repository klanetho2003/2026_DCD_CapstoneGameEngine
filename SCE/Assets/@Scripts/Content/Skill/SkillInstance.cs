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
    /// To Do. UserAim
    /// </summary>
    public bool AllowsAimTracking
    {
        get
        {
            if (IsCasting)
                return Definition.CastingEffects != null && Definition.CastingEffects.AllowAimTracking;
            if (Definition is UserSkillDefinitionSO playerDef)
                return playerDef.AllowAimTrackingWhileRunning;
            return false;
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

    public bool TryBeginCasting()
    {
        if (Definition.CastingEffects == null)
            return false;
        if (CanUse() == false)
            return false;
        if (Owner.HasTag(Definition.InValidTags))
            return false;

        Owner.SkillBook.InterruptRunningSkill(); // 기존 스킬 정리
        IsCasting = true;
        IsRunning = true;
        Owner.AddTag(ECreatureTag.UsingSkill);
        Owner.SkillBook.RegisterRunningSkill(this); // 스킬 등록

        // casting Effect 일괄 적용
        var casting = Definition.CastingEffects;
        if (casting.CastingEffects != null)
        {
            var context = new SkillExecutionContext { Caster = Owner, Skill = this };
            for (int i = 0; i < casting.CastingEffects.Count; i++)
                casting.CastingEffects[i]?.Apply(in context);
        }

        if (Definition.AnimationStateName != EUserbleAnimState.None
            && Owner.CreatureAnim != null
            && Owner.CreatureAnim.AnimationHash.TryGetValue((int)Definition.AnimationStateName, out var hash))
            Owner.CreatureAnim.PlayState(hash);

        return true;
    }

    /// <summary>
    /// Skill Book을 통해서 시전. 직접 호출 금지. Effect 일괄 적용
    /// </summary>
    public bool TryUse()
    {
        bool fromCasting = IsCasting;

        if (fromCasting)
        {
            IsCasting = false;
            if (Definition.AnimationStateName != EUserbleAnimState.None)
                Owner.CreatureAnim?.ClearAnim();
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

        // Animation 재생
        if (Definition.AnimationStateName != EUserbleAnimState.None
            && Owner.CreatureAnim != null
            && Owner.CreatureAnim.AnimationHash.TryGetValue((int)Definition.AnimationStateName, out var hash))
            Owner.CreatureAnim.PlayState(hash);

        // 특정 Target Damage & Effect
        // ApplyTargetedEffects(context);

        // Cooldown 시작
        RemainingCooldown = Definition.CooldownTime;
        OnCooldownChanged?.Invoke(RemainingCooldown);

        Owner.SkillBook.PhaseRunner.Begin(this, Definition.Phases);

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

    public void OnEndSkill(Action additionalFunc = null)
    {
        if (IsRunning == false)
            return;

        IsRunning = false;
        IsCasting = false;

        Owner.SkillBook?.PhaseRunner.StopIfRunning(this);

        // Hitbox
        Owner.CurrentActiveHitbox?.Deactivate();
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
    /// </summary>
    public void HandleAnimationEnd()
    {
        Owner.SkillBook.PhaseRunner.OnAnimationEnd(); // 신호만, 종료 아님
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