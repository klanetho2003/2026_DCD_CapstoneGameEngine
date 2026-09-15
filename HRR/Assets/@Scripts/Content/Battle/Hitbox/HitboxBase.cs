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
    protected static readonly WaitForFixedUpdate _waitFixedUpdate = new();

    protected CombatCreature _owner;
    protected Collider2D _collider;

    // 피격 객체 Handle
    protected readonly HashSet<CombatCreature> _alreadyHitOwners = new();

    // 현재 frame 내 객체 최우선순위 Hurtbox Handle — dispatch 버퍼
    protected readonly Dictionary<CombatCreature, Hurtbox> _pendingBestHurtbox = new();

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
    public virtual void SetOwner(CombatCreature owner)
    {
        _owner = owner;

        // 전투 관련 sheet하나 파서 hitbox layer 값 넣어야 할 듯
        this.gameObject.layer = (_owner.CreatureData.creatureType == EObjectType.Villager)
            ? (int)ELayer.Player_HitBox
            : (int)ELayer.Monster_HitBox;
    }

    /// <summary>
    /// Animation Event "OnAttackHitboxOn"의 종착점.
    /// 활성 사이클 시작: 이전 히트 기록을 비우고 Collider 활성화.
    /// </summary>
    public virtual void Activate()
    {
        _alreadyHitOwners.Clear();
        _pendingBestHurtbox.Clear();
        _collider.enabled = true;
    }

    /// <summary>
    /// Animation Event "OnAttackHitboxOff"의 종착점.
    /// 활성 사이클 종료. 새 사이클이 시작될 때까지 판정 발생 안 함.
    /// </summary>
    public virtual void Deactivate()
    {
        _collider.enabled = false;

        // 미처리 후보 폐기
        ClearHurtbox();
    }

    protected void ClearHurtbox()
    {
        if (_coResolveCoroutine != null)
        {
            StopCoroutine(_coResolveCoroutine);
            _coResolveCoroutine = null;
        }
        _pendingBestHurtbox.Clear();
    }

    /// <summary>
    /// 1 FixedUpdate 지연 — 같은 cycle 내 모든 OnTriggerEnter가 후보 등록을 마친 후
    /// 1개씩 dispatch.
    /// </summary>
    protected virtual IEnumerator CoResolvePending(Action onCompelete = null)
    {
        yield return _waitFixedUpdate;

        foreach (var pair in _pendingBestHurtbox)
        {
            var victim = pair.Key;
            var hurtbox = pair.Value;

            // yield 중 객체 사망 등 상태 변화 check
            if (victim.IsDead)
                continue;
            if (_alreadyHitOwners.Contains(victim))
                continue;

            _alreadyHitOwners.Add(victim);
            
            var damageInfo = DamageCalculator.Calculate(
                _owner,
                damageMultiplier: 1,
                flatDamage: 0,
                damageType: EDamageType.None,
                element: EElement.None,
                hurtbox: hurtbox);
                /*hitStopDuration: _hitStopDuration,
                hitVfxKey: _hitVfxKey,
                cameraShakeStrength: _cameraShakeStrength);*/

            hurtbox.OnHit(damageInfo);
        }

        ClearHurtbox();

        onCompelete?.Invoke();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryRegisterCandidate(other);
    }

    protected void TryRegisterCandidate(Collider2D other)
    {
        if (_owner == null)
            return;
        if (other.TryGetComponent<Hurtbox>(out var hurtbox) == false)
            return;

        if (hurtbox.Owner == _owner)
            return;
        if (_owner.IsValidTarget(hurtbox.Owner.CreatureType) == false)
            return;
        if (_alreadyHitOwners.Contains(hurtbox.Owner))
            return;

        // 정밀 모양으로 재판정
        if (IsWithinShape(hurtbox) == false)
            return;

        if (_pendingBestHurtbox.TryGetValue(hurtbox.Owner, out var existing))
        {
            if (hurtbox.Priority > existing.Priority)
                _pendingBestHurtbox[hurtbox.Owner] = hurtbox;
        }
        else
        {
            _pendingBestHurtbox.Add(hurtbox.Owner, hurtbox);
        }

        if (_coResolveCoroutine == null)
            _coResolveCoroutine = StartCoroutine(CoResolvePending());
    }

    protected virtual bool IsWithinShape(Hurtbox hurtbox) { return true; }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        var col = GetComponent<Collider2D>();
        if (col == null)
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