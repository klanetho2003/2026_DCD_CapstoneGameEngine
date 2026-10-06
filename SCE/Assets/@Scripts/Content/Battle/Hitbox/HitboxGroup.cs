using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class HitboxGroup : InitBase
{
    [Tooltip("이 그룹의 식별자. Skill 부품 OnEffectHitboxActivationSO에서 사용.")]
    [SerializeField]
    private string _groupId;
    public string GroupId { get { return _groupId; } }

    public CombatCreature Owner { get; private set; }

    private readonly List<HitboxBase> _hitboxes = new();
    public IReadOnlyList<HitboxBase> Hitboxes => _hitboxes;

    private HitboxInfo _info;

    #region 판정
    // 피격 객체 Handle
    private readonly HashSet<CombatCreature> _alreadyHitOwners = new();
    public IReadOnlyCollection<CombatCreature> AlreadyHitOwners => _alreadyHitOwners;

    // 현재 frame 내 객체 최우선순위 Hurtbox Handle — dispatch 버퍼
    private readonly Dictionary<CombatCreature, Hurtbox> _pendingBestHurtbox = new();
    public IReadOnlyDictionary<CombatCreature, Hurtbox> PendingBestHurtbox => _pendingBestHurtbox;


    private Coroutine _coResolveCoroutine;
    private static readonly WaitForFixedUpdate _waitFixedUpdate = new();
    #endregion

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        return true;
    }

    public void SetInfo(HitboxInfo info)
    {
        _info = info;
    }

    /// <summary>
    /// CombatCreature.Init에서 호출 — 자식 Hitbox들에 owner 전파.
    /// </summary>
    public void SetOwner(CombatCreature owner)
    {
        if (Owner != owner)
        {
            Owner = owner;
            InitHitBoxes();
        }

        for (int i = 0; i < _hitboxes.Count; i++)
            _hitboxes[i].SetOwner(this);
    }

    private void InitHitBoxes()
    {
        // 자식 Hitbox 캐싱 — 비활성 상태에서도 수집
        foreach (var hitbox in GetComponentsInChildren<HitboxBase>(includeInactive: false))
            _hitboxes.Add(hitbox);

        SetActive(false);
    }

    /// <summary>
    /// 그룹 전체 활성/비활성 — GameObject.SetActive 한 번으로 자식 모두 토글.
    /// </summary>
    public void SetActive(bool active)
    {
        if (gameObject.activeSelf == active)
            return;

        if (active)
        {
            // On
            _alreadyHitOwners.Clear();
            _pendingBestHurtbox.Clear();

            // 회전각 Set
            transform.rotation = _info.LookDirection;
        }
        else
        {
            // Off
            ClearHurtbox();
        }
        

        gameObject.SetActive(active);
    }

    private void ClearHurtbox()
    {
        if (_coResolveCoroutine != null)
        {
            StopCoroutine(_coResolveCoroutine);
            _coResolveCoroutine = null;
        }
        _pendingBestHurtbox.Clear();
    }

    public void TryRegisterCandidate(Collider2D other, HitboxBase hitbox)
    {
        var owner = Owner;
        if (owner == null)
            return;
        if (other.TryGetComponent<Hurtbox>(out var hurtbox) == false)
            return;

        if (hurtbox.Owner == owner)
            return;
        if (owner.IsValidTarget(hurtbox.Owner.CreatureType) == false)
            return;
        if (AlreadyHitOwners.Contains(hurtbox.Owner))
            return;

        // 정밀 모양으로 재판정
        if (hitbox.IsWithinShape(hurtbox) == false)
            return;

        if (PendingBestHurtbox.TryGetValue(hurtbox.Owner, out var existing))
        {
            if (hurtbox.Priority > existing.Priority)
                _pendingBestHurtbox[hurtbox.Owner] = hurtbox;
        }
        else
        {
            _pendingBestHurtbox.Add(hurtbox.Owner, hurtbox);
        }

        // 후보가 등록되었고 실행 중인 코루틴이 없다면 Group 단위 코루틴 시작
        if (_coResolveCoroutine == null)
            _coResolveCoroutine = StartCoroutine(CoResolvePending());
    }

    /// <summary>
    /// jobQueue(_pendingBestHurtbox)에 등록해두었다가 1frame이 지난 시점에 flush하는 구조.
    /// </summary>
    private IEnumerator CoResolvePending()
    {
        yield return _waitFixedUpdate;

        var owner = Owner;
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

            owner.SkillBook.CurrentRunningSkill.OnHitboxCollision(hurtbox);
        }

        ClearHurtbox();
    }
}
