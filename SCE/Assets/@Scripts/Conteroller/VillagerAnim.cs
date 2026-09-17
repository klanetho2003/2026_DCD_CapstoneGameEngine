using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using static Define;

/// <summary>
/// villager 전용 anim
/// villager의 경우 다른 creature 보다 animation이 많을 것으로
/// 예상되기에 별도로 anim을 구현
/// </summary>
public class VillagerAnim : BaseCreatureAnim, IAnimationEventInvoker
{
    public Villager GetOwnerAsVillger => Owner as Villager;

    public override Dictionary<int, int> AnimationHash => _animationHash;
    private Dictionary<int, int> _animationHash = new Dictionary<int, int>()
    {
        { (int)EUserbleAnimState.Locomotion, Animator.StringToHash("Locomotion")},
        { (int)EUserbleAnimState.Idle, Animator.StringToHash("Idle")},
        { (int)EUserbleAnimState.Attack, Animator.StringToHash("Attack")},
        { (int)EUserbleAnimState.Damaged, Animator.StringToHash("Damaged")},
    };

    // 시선 방향
    private readonly int _hashLookX = Animator.StringToHash("LookX");
    private readonly int _hashLookY = Animator.StringToHash("LookY");

    // 이동 방향
    private readonly int _hashMoveX = Animator.StringToHash("MoveX");
    private readonly int _hashMoveY = Animator.StringToHash("MoveY");

    // 바라보는 방향
    private Vector2 _lastLook = new Vector2(-999, -999);
    private Vector2 _lastMove = new Vector2(-999, -999);

    /// <summary>
    /// 매 프레임 시선과 이동 벡터를 평가하여 Animator에 전달.
    /// </summary>
    public void UpdateLocomotion()
    {
        // 특정 상태일 경우 Locomotion을 덮어쓰지 않음
        if (_owner.HasTag(ECreatureTag.ForceMoving | ECreatureTag.UsingSkill))
            return;

        //Vector2 lookDir = _villager.Aim.LookDirection;
        Vector2 moveDir = GetOwnerAsVillger.Movement.CacheMoveDirection;

        if (moveDir == Vector2.zero)
        {
            _animationHash.TryGetValue((int)EUserbleAnimState.Idle, out var hash);
            PlayState(hash);
        }
        else
        {
            _animationHash.TryGetValue((int)EUserbleAnimState.Locomotion, out var hash);
            PlayState(hash);
        }

        /*if (lookDir != _lastLook)
        {
            _animator.SetFloat(_hashLookX, lookDir.x);
            _animator.SetFloat(_hashLookY, lookDir.y);
            _lastLook = lookDir;
            _spriteRenderer.flipX = (lookDir.x < 0);
        }*/

        if (moveDir != _lastMove)
        {
            _animator.SetFloat(_hashMoveX, moveDir.x);
            _animator.SetFloat(_hashMoveY, moveDir.y);
            _lastMove = moveDir;
        }
    }

    public override void OnDamagedAnimationEnd()
    {
        GetOwnerAsVillger.StateMachine.CurrentInputHandler.OnAnimationEnd(GetOwnerAsVillger);
    }

    #region IAnimationEventInvoker
    public BaseCreatureAnim Anim => GetOwnerAsVillger.Anim;

    void IAnimationEventInvoker.OnSkillStart()
    {
        _owner.AddTag(ECreatureTag.UsingSkill);
    }

    void IAnimationEventInvoker.OnSkillEnd()
    {
        var villager = GetOwnerAsVillger;
        villager.RemoveTag(ECreatureTag.UsingSkill);
        villager.SkillBook.InterruptRunningSkill();
    }

    void IAnimationEventInvoker.OnAttackHitboxOn()
    {
        if (GetOwnerAsVillger == null)
            return;

        if (GetOwnerAsVillger.CurrentActiveHitbox == null)
        {
            LogPrinter.LogWarning(this, $"[{gameObject.name}] OnAttackHitboxOn — CurrentActiveHitbox 없음 (Skill 시전 안 됨?)");
            return;
        }

        GetOwnerAsVillger.CurrentActiveHitbox.Activate();
    }

    void IAnimationEventInvoker.OnAttackHitboxOff()
    {
        GetOwnerAsVillger?.CurrentActiveHitbox?.Deactivate();
    }

    void IAnimationEventInvoker.OnHurtboxLayoutRevert()
    {
        GetOwnerAsVillger?.RevertToDefaultHurtboxLayout();
    }

    void IAnimationEventInvoker.OnHurtboxLayoutChange(string layoutKey)
    {
        if (string.IsNullOrEmpty(layoutKey))
        {
            LogPrinter.LogError($"[{gameObject.name}] OnHurtboxLayoutChange: layoutKey가 비어있습니다.");
            return;
        }

        GetOwnerAsVillger?.SwitchHurtboxLayout(layoutKey);
    }
    #endregion
}
