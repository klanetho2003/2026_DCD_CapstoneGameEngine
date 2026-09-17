using System.Collections.Generic;
using UnityEngine;
using static Define;

public interface IAnimationEventInvoker
{
    public BaseCreatureAnim Anim { get; }

    /// <summary>
    /// Animation Event "IAnimationEventInvoker.OnSkillStart"
    /// </summary>
    public void OnSkillStart();

    /// <summary>
    /// Animation Event "IAnimationEventInvoker.OnSkillEnd"
    /// </summary>
    public void OnSkillEnd();

    /// <summary>
    /// Animation Event "IAnimationEventInvoker.OnAttackHitboxOn"
    /// </summary>
    public void OnAttackHitboxOn();

    /// <summary>
    /// Animation Event "IAnimationEventInvoker.OnAttackHitboxOff"
    /// </summary>
    public void OnAttackHitboxOff();

    /// <summary>
    /// Animation Event "IAnimationEventInvoker.OnHurtboxLayoutRevert"
    /// 기본 layout(CreatureData.HurtboxLayoutKey)으로 복귀.
    /// 공격/스킬 클립의 마지막 프레임에 걸어 평상시 자세로 자동 복귀.
    /// </summary>
    public void OnHurtboxLayoutRevert();

    /// <summary>
    /// Animation Event "IAnimationEventInvoker.OnHurtboxLayoutChange".
    /// String Parameter로 HurtboxLayoutSO의 Addressable key 전달.
    /// 
    /// 사용 예시 — Animator의 Walk 클립 시작 프레임:
    ///   Function: OnHurtboxLayoutChange
    ///   String:   HurtboxLayout_Goblin_Walk
    /// </summary>
    public void OnHurtboxLayoutChange(string layoutKey);
}

/// <summary>
/// 모든 Creature 애니메이션의 공통 베이스
/// 
/// 공통:
/// - Animator/SpriteRenderer 캐싱
/// - PlayState (same-state 가드 + forceRestart)
/// - Animation Event 핸들러:
///   * Hitbox 활성/비활성 (공격용)
///   * Hurtbox layout 전환 (자세별 피격 부위 변경)
/// 
/// 자식:
/// - 캐릭터별 hash 정의
/// - 캐릭터별 상태 처리 (StateMachine 위임, AI 위임 등)
/// </summary>
public abstract class BaseCreatureAnim : InitBase
{
    protected CreatureBase _owner;
    public virtual CreatureBase Owner => _owner;
    protected Animator _animator;
    protected SpriteRenderer _spriteRenderer;

    private static readonly int _hashEmpty = Animator.StringToHash("Empty");

    public abstract Dictionary<int, int> AnimationHash { get; }

    /// <summary>
    /// 클립 전용 Event 종착점 - 상태 복귀는 State가 결정
    /// </summary>
    public abstract void OnDamagedAnimationEnd();

    public virtual void SetInfo(CreatureBase owner)
    {
        _owner = owner;
        _animator = GetComponent<Animator>();
        _spriteRenderer = GetComponent<SpriteRenderer>();
    }

    /// <summary>Animator 상태 진입.</summary>
    public void PlayState(int stateHash)
    {
        _animator.Play(stateHash);
    }

    /// <summary>Empty로 초기화.</summary>
    public void ClearAnim()
    {
        PlayState(_hashEmpty);
    }
}
