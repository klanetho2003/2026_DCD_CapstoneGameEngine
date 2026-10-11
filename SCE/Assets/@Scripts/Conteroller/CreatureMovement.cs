using System;
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

    public void ForceMoveTo(Vector2 direction, float speedWeight = 1f, Action onComplete = null)
    {
        // Lerp 함수를 확용. 현재 위치에서 목적지까지 보간처리 해서 이동하는 것을 추천

        // 1. 방향 제한 (4방향 이동)
        // 2. 타일 중앙 위치로 이동

        // 목적이에서 목적지로 이동하는 기능을 만들면 된다.
        // Lerp라는 보간함수가 있음 (A위치, B위치, 현재 진행도 << 백분율)

        // 0,0 -> 1,1 (진행도가 3이다) >> (0.3, 0.3)

        // 목적지까지 이동
        {
            Move(direction, speedWeight);
        }

        /* trigger 조건
         * 입력을 하고 있는 도중에는 목적지 갱신이 가능하다. && 목적지에 도달해야지 목적지 갱신이 가능하다
         */

        throw new NotImplementedException();
    }
}
