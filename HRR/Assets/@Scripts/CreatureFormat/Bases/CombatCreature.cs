using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using static Define;

public abstract class CombatCreature : CreatureBase, IDamageable
{
    public bool IsDead { get { return _creatureResource.GetResource(EStatResourceType.CurrentHp).CurrentValue <= 0; } }
    private Action<CombatCreature> _onDeath;

    #region Abstract Methods
    public abstract bool IsValidTarget(EObjectType objType);
    protected abstract float CalculateFinalDamage(DamageInfo damageInfo);
    protected abstract void OnAfterDamageApplied(DamageInfo damageInfo, float finalDamage);
    protected abstract void OnDie();
    #endregion

    #region Hit & Hurt Box
    // Collider 기반 판정 영역 캐싱
    public List<HitboxBase> Hitboxes { get; private set; } = new();
    public List<Hurtbox> Hurtboxes { get; private set; } = new();


    private string _defaultHurtboxLayoutKey; // 기본 layout key 캐싱 - RevertToDefaultHurtboxLayout이 사용
    private readonly Dictionary<string, HurtboxGroup> _hurtboxGroups = new();
    private HurtboxGroup _currentHurtboxGroup;
    #endregion

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        return true;
    }

    public override void SetInfo(int objectID)
    {
        base.SetInfo(objectID);

        #region Set Hit&Hurt box

        // 이미 Setting이 되어 있으면 Return
        if (Hitboxes.Count != 0 && _hurtboxGroups.Count != 0)
            return;

        // Hitbox 캐싱
        foreach (var hitbox in GetComponentsInChildren<HitboxBase>(includeInactive: true))
        {
            hitbox.SetOwner(this);
            Hitboxes.Add(hitbox);
        }

        // HurtboxGroup 캐싱
        foreach (var group in GetComponentsInChildren<HurtboxGroup>(includeInactive: true))
        {
            if (string.IsNullOrEmpty(group.GroupId))
            {
                LogPrinter.LogError($"[{gameObject.name}] HurtboxGroup의 GroupId가 비어있습니다: {group.gameObject.name}");
                continue;
            }

            if (_hurtboxGroups.ContainsKey(group.GroupId))
            {
                LogPrinter.LogError($"[{gameObject.name}] HurtboxGroup GroupId 중복: {group.GroupId}");
                continue;
            }

            group.SetOwner(this);
            _hurtboxGroups.Add(group.GroupId, group);

            // 전체 Hurtbox 리스트에도 추가 (외부 조회용)
            foreach (var hurtbox in group.Hurtboxes)
                Hurtboxes.Add(hurtbox);
        }

        // 기본 Hurtbox 설정 (5개 슬롯 보장 + 기본 layout 적용)
        _defaultHurtboxLayoutKey = CreatureData.hurtboxLayoutKey;
        SwitchHurtboxLayout(_defaultHurtboxLayoutKey);
        #endregion
    }

    #region HitBox
    /// <summary>
    /// 현재 활성화된 Hitbox.
    /// Animation Event에서 호출되는 call back함수에서 사용
    /// </summary>
    public ActivableHitbox CurrentActiveHitbox { get; private set; }

    public void SetActiveHitbox(ActivableHitbox hitbox)
    {
        CurrentActiveHitbox = hitbox;
    }

    public void ClearActiveHitbox()
    {
        CurrentActiveHitbox = null;
    }

    /// <summary>
    /// HitboxId로 자식 Hitbox 조회. Animation Event 핸들러가 사용
    /// Hitbox 개수가 작음. 갯수 늘어나면 Dictionary 캐싱 검토
    /// </summary>
    public ActivableHitbox GetHitbox(int hitboxId)
    {
        return Hitboxes.Find(h => h is ActivableHitbox activate && activate.HitboxId == hitboxId) as ActivableHitbox;
    }
    #endregion

    #region Hurt Box
    /// <summary>
    /// Hurtbox 슬롯을 지정된 layout으로 교체.
    /// 호출 진입점:
    /// - SetInfo 시 (기본 layout)
    /// - Animation Event "OnHurtboxLayoutChange" (자세별 layout)
    /// </summary>
    public void SwitchHurtboxLayout(string layoutKey)
    {
        if (string.IsNullOrEmpty(layoutKey))
        {
            LogPrinter.LogError($"[{gameObject.name}] HurtboxLayoutKey가 비어있습니다.");
            return;
        }

        if (_hurtboxGroups.TryGetValue(layoutKey, out var targetGroup) == false)
        {
            LogPrinter.LogError($"[{gameObject.name}] HurtboxLayout 로드 실패 (key={layoutKey}).");
            return;
        }

        foreach (var group in _hurtboxGroups.Values)
            group.SetActive(group == targetGroup);

        _currentHurtboxGroup = targetGroup;
    }

    /// <summary>
    /// 기본 Hurtbox layout으로 복귀.
    /// </summary>
    public void RevertToDefaultHurtboxLayout()
    {
        if (string.IsNullOrEmpty(_defaultHurtboxLayoutKey)) return;
        SwitchHurtboxLayout(_defaultHurtboxLayoutKey);
    }
    #endregion

    #region IDamageable
    public virtual void OnDamaged(DamageInfo damageInfo)
    {   
        if (damageInfo.Attacker == null) return;
        if (damageInfo.Attacker.IsValidTarget(this.CreatureType) == false) return;

        // 데미지 산출
        float calculatedDamage = CalculateFinalDamage(damageInfo);
        float finalDamage = Mathf.Max(0, calculatedDamage);

        var currentHp = _creatureResource.GetResource(EStatResourceType.CurrentHp);

        float handle = currentHp.CurrentValue - finalDamage;
        currentHp.SetCurrent(handle, damageInfo);

        string critTag = damageInfo.IsCritical ? "<color=yellow>[CRIT!]</color> " : "";
        //string weekPointTag = (damageInfo.BodyPart == EBodyPart.Weakpoint) ? "<color=magenta>[WeekPoint!]</color> " : "";
        //{critTag}{weekPointTag}{damageInfo.Attacker.gameObject.name}
        LogPrinter.Log($"<color=cyan>{critTag}{damageInfo.Attacker.gameObject.name} --Hit--> {gameObject.name}\n" +
            $"Damage_{finalDamage:F1} ({damageInfo.Type}/{damageInfo.Element})\n" +
            $"Current_{currentHp.CurrentValue:F0}/{GetStatValue(EStatType.MaxHp).Value:F0}</color>");

        // To Do: Hit 상태 변경
        OnAfterDamageApplied(damageInfo, finalDamage);

        // To Do: HitStop/HitStun/Knockback 처리 진입점이 들어감

        if (IsDead)
            Die();
    }

    
    protected void Die()
    {
        _onDeath?.Invoke(this);
        OnDie();
    }
    #endregion
}