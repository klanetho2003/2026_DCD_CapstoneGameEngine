using UnityEngine;
using static Define;

/// <summary>
/// villager 전용 anim
/// villager의 경우 다른 creature 보다 animation이 많을 것으로
/// 예상되기에 별도로 anim을 구현
/// </summary>
public class VillagerAnim : BaseCreatureAnim
{
    private Villager _villager;

    // Get Helper
    public int HashLocomotion { get { return _hashLocomotion; } }
    public int HashIdle { get { return _hashIdle; } }
    public int HashAttack { get { return _hashAttack; } }
    public int HashDamaged { get { return _hashDamaged; } }

    // StringToHash 캐싱
    private readonly int _hashLocomotion = Animator.StringToHash("Locomotion"); // BlendTree 노드 이름
    private readonly int _hashIdle = Animator.StringToHash("Idle"); // BlendTree 노드 이름
    private readonly int _hashAttack = Animator.StringToHash("Attack");
    private readonly int _hashDamaged = Animator.StringToHash("Damaged");

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
    /// Player 전용 SetInfo. base에 _owner 저장 + Player ref 따로 보관.
    /// </summary>
    public void SetInfo(Villager owner)
    {
        base.SetInfo(owner);
        _villager = owner;
    }

    /// <summary>
    /// 매 프레임 시선과 이동 벡터를 평가하여 Animator에 전달.
    /// </summary>
    public void UpdateLocomotion()
    {
        // 특정 상태일 경우 Locomotion을 덮어쓰지 않음
        if (_owner.HasTag(ECreatureTag.ForceMoving))
            return;

        //Vector2 lookDir = _villager.Aim.LookDirection;
        Vector2 moveDir = _villager.Movement.CacheMoveDirection;

        if (moveDir == Vector2.zero)
            PlayState(HashIdle);
        else
            PlayState(HashLocomotion);

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
        _villager.StateMachine.CurrentInputHandler.OnAnimationEnd(_villager);
    }
}
