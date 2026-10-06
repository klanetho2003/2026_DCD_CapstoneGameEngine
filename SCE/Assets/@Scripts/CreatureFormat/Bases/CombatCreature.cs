using Data;
using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using static Define;
using static UnityEngine.Rendering.DebugUI;

public abstract class CombatCreature : CreatureBase, IDamageable
{
    /// <summary>한 생애에 사망 처리 1회. 풀 재사용 시 SetInfo에서 초기화.</summary>
    private bool _isDeathProcessed;
    public bool IsDead { get { return _creatureResource.GetResource(EStatResourceType.CurrentHp).CurrentValue <= 0; } }
    private Action<CombatCreature> _onDeath;
    public void SetDeathListener(Action<CombatCreature> listener) { _onDeath += listener; } // To Do 연결되어 있는 event 일괄 자동 해제 구현 필요


    // Skill
    public SkillBook SkillBook { get; private set; }

    #region Abstract Methods
    public abstract bool IsValidTarget(EObjectType objType);
    protected abstract float CalculateFinalDamage(DamageInfo damageInfo);
    protected abstract void OnAfterDamageApplied(DamageInfo damageInfo, float finalDamage);
    protected abstract void OnDie();
    #endregion

    #region Hit & Hurt Box
    public List<HitboxBase> Hitboxes { get; private set; } = new();
    private readonly Dictionary<string, HitboxGroup> _hitboxGroups = new();


    public List<Hurtbox> Hurtboxes { get; private set; } = new();
    private string _defaultHurtboxLayoutKey; // 기본 layout key 캐싱 - RevertToDefaultHurtboxLayout이 사용
    private readonly Dictionary<string, HurtboxGroup> _hurtboxGroups = new();
    private HurtboxGroup _currentHurtboxGroup;
    #endregion

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        // gameObject가 켜져 있는 동안에는 계속 물리 Check
        GetComponent<Rigidbody2D>().sleepMode = RigidbodySleepMode2D.NeverSleep;

        // SkillBook 캐싱 (스킬 없는 객체일 경우 Comopenet 추가하지 말 것)
        SkillBook = GetComponent<SkillBook>();

        return true;
    }

    public override void SetInfo(int objectID)
    {
        base.SetInfo(objectID);

        _isDeathProcessed = false; // 풀 재사용 시에 초기화

        var data = GetCreatureData<CombatCreatureData>();

        #region Set Hit&Hit box

        // To Do 전생 기억해서 세팅 유지해도 되면 skip
        _hitboxGroups.Clear();
        Hitboxes.Clear();

        _hurtboxGroups.Clear();
        Hurtboxes.Clear();
        
        // Hitbox 캐싱
        foreach (var group in GetComponentsInChildren<HitboxGroup>(includeInactive: true))
        {
            if (string.IsNullOrEmpty(group.GroupId))
            {
                LogPrinter.LogError($"[{gameObject.name}] HitboxGroup의 GroupId가 비어있습니다: {group.gameObject.name}");
                continue;
            }

            if (_hitboxGroups.ContainsKey(group.GroupId))
            {
                LogPrinter.LogError($"[{gameObject.name}] HitboxGroup GroupId 중복: {group.GroupId}");
                continue;
            }

            group.SetOwner(this);
            _hitboxGroups.Add(group.GroupId, group);

            // 전체 Hitbox 리스트에도 추가 (외부 조회용)
            foreach (var hitbox in group.Hitboxes)
                Hitboxes.Add(hitbox);
        }

        // HurtboxGroup 캐싱
        foreach (var group in GetComponentsInChildren<HurtboxGroup>(includeInactive: true))
        {
            if (string.IsNullOrEmpty(group.GroupId))
            {
                LogPrinter.LogError($"[{TemplateId}] HurtboxGroup의 GroupId가 비어있습니다: {group.gameObject.name}");
                continue;
            }

            if (_hurtboxGroups.ContainsKey(group.GroupId))
            {
                LogPrinter.LogError($"[{TemplateId}] HurtboxGroup GroupId 중복: {group.GroupId}");
                continue;
            }

            group.SetOwner(this);
            _hurtboxGroups.Add(group.GroupId, group);

            // 전체 Hurtbox 리스트에도 추가 (외부 조회용)
            foreach (var hurtbox in group.Hurtboxes)
                Hurtboxes.Add(hurtbox);
        }

        // 기본 Hurtbox 설정 (5개 슬롯 보장 + 기본 layout 적용)
        _defaultHurtboxLayoutKey = data.hurtboxLayoutKey;
        SwitchHurtboxLayout(_defaultHurtboxLayoutKey);
        #endregion

        #region Skill
        // SkillBook 초기화 - CreatureData.SkillIDs에서 스킬 로드
        if (SkillBook != null)
            SkillBook.SetInfo(this, data.SkillIDs);
        #endregion
    }

    #region HitBox
    /// <summary>
    /// 현재 활성화된 Hitbox.
    /// Animation Event에서 호출되는 call back함수에서 사용
    /// </summary>
    public HitboxGroup ActivaedHitboxGroup { get; private set; }

    public void SetActiveHitbox(HitboxGroup hitboxGroup, HitboxInfo info)
    {
        ActivaedHitboxGroup = hitboxGroup;
        ActivaedHitboxGroup.SetInfo(info);
    }

    public void ClearActiveHitbox()
    {
        ActivaedHitboxGroup = null;
    }

    /// <summary>
    /// HitboxId로 자식 Hitbox 조회. Animation Event 핸들러가 사용
    /// Hitbox 개수가 작음. 갯수 늘어나면 Dictionary 캐싱 검토
    /// </summary>
    public HitboxGroup GetHitbox(string hitboxGroupId)
    {
        if (_hitboxGroups.TryGetValue(hitboxGroupId, out var group) == false)
            return null;

        return group;
        //return Hitboxes.Find(h => h is ActivableHitbox activate && activate.HitboxId == hitboxId) as ActivableHitbox;
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

        if (IsDead) // on die가 다수 호출되는 것을 방지하기 위해 flag를 넣어야하는가
            Die(damageInfo.Attacker);
    }

    /// <summary>
    /// 주의:
    /// 버스가 다른 이벤트를 처리하는 도중에 사망이 일어나면
    /// CreatureDiedEvent는 대기열에 들어가고 OnDie가 먼저 실행됩니다.
    /// 그래서 리스너가 받을 때 Victim이 이미 디스폰(비활성)되어 있을 수 있습니다.
    /// 식별은 이벤트에 값으로 담긴 TemplateId를 쓰고, Victim 참조는 보관하지 마세요.
    /// </summary>
    protected void Die(CombatCreature killer = null)
    {
        if (_isDeathProcessed)
            return;
        _isDeathProcessed = true;

        RaiseGameEvent(new CreatureDiedEvent(this, killer, TemplateId));
        _onDeath?.Invoke(this);
        OnDie();
    }
    #endregion
}