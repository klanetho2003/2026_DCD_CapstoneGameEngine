using Data;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static Define;
using static LogPrinter;

/// <summary>
/// 공격 판정 영역. Animation Event로 Activate/Deactivate 되며,
/// 활성 중에 만난 Hurtbox에 데미지 정보를 전달한다.
/// 
/// 책임:
/// - 다단 히트 방지 (활성 사이클 내 같은 Hurtbox 한 번만)
/// - 크리티컬은 공격자 측 책임
/// - DamageInfo 생성 후 전달
/// 
/// 의도하지 않은 책임:
/// - 방어력 차감 (피격자의 CalculateFinalDamage가 처리)
/// </summary>
[RequireComponent(typeof(Collider2D))]
public abstract class HitboxBase : InitBase
{
    protected HitboxGroup _group;
    protected Collider2D _collider;

    // 한 frame당 코루틴 1개만
    protected Coroutine _coResolveCoroutine;

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        _collider = GetComponent<Collider2D>();
        _collider.isTrigger = true;

        return true;
    }

    /// <summary>
    /// CombatCreature가 자기 자식 Hitbox들을 캐싱할 때 호출.
    /// </summary>
    public virtual void SetOwner(HitboxGroup group)
    {
        _group = group;

        // 전투 관련 sheet하나 파서 hitbox layer 값 넣어야 할 듯
        this.gameObject.layer = (_group.Owner.CreatureType == EObjectType.Villager)
            ? (int)ELayer.Player_HitBox
            : (int)ELayer.Monster_HitBox;
    }

    public virtual bool IsWithinShape(Hurtbox hurtbox) { return true; }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // HitboxBase에서 감지 후 등록 처리는 Group에 위임
        _group.TryRegisterCandidate(other, this);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        var col = GetComponent<Collider2D>();
        if (col == null || col.isActiveAndEnabled == false)
            return;

        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.4f); // red

        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = col.transform.localToWorldMatrix;

        if (col is BoxCollider2D box)
        {
            Gizmos.DrawCube(box.offset, box.size);

            Gizmos.color = new Color(Gizmos.color.r, Gizmos.color.g, Gizmos.color.b, 1f);
            Gizmos.DrawWireCube(box.offset, box.size);
        }
        else if (col is CircleCollider2D sphere)
        {
            Gizmos.DrawSphere(sphere.offset, sphere.radius);
        }

        Gizmos.matrix = oldMatrix;
    }
#endif
}