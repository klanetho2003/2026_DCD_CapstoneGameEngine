using UnityEngine;
using static Define;

public class CreatureMovement : InitBase
{
    private IMovable _owner; 

    public Rigidbody2D RB2D { get; private set; }

    public float MoveSpeed => _owner.MoveSpeed;

    public Vector2 CacheMoveDirection => _owner.CacheMoveDirection;

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        RB2D = GetComponent<Rigidbody2D>();

        return true;
    }

    public void SetInfo(IMovable owner)
    {
        _owner = owner;
    }

    public void Move(Vector2 direction, float speedWeight = 1f)
    {
        /*if (_owner is not CreatureBase creature)
            return;
        if (creature.IsDead) return;*/

        //CacheMoveDirection = direction.normalized; // 방향 캐싱

        Vector2 effective = direction * speedWeight;
        _owner.CacheMoveDirection = effective;

        RB2D.linearVelocity = effective * MoveSpeed; // rigidbody 내부에서 delta time을 이미 처리
    }
}
