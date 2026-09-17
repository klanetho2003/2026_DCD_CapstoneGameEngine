using System.Collections.Generic;
using UnityEngine;
using static LogPrinter;

/// <summary>
/// 캐릭터의 스킬 컨테이너 + 사용 진입점. 캐릭터당 1개의 MonoBehaviour
/// 
/// 책임:
/// - CreatureData.SkillIDs에서 SkillDefinitionSO 로드
/// - SkillInstance 생성+관리
/// - 매 프레임 모든 Skill의 Cooldown Tick
/// - 외부의 TryUseSkill 요청 처리
/// </summary>
public class SkillBook : MonoBehaviour
{
    private CombatCreature _owner;
    private readonly Dictionary<int, SkillInstance> _skillDic = new();
    private readonly List<SkillInstance> _activeCooldowns = new();

    /// <summary>
    /// 모든 SkillInstance에 접근. 외부에서 순회 + UI 표시 등에 사용.
    /// </summary>
    public IReadOnlyDictionary<int, SkillInstance> SkillDic { get { return _skillDic; } }

    public SkillPhaseRunner PhaseRunner { get { return _phaseRunner ??= new SkillPhaseRunner(); } }
    private SkillPhaseRunner _phaseRunner;

    #region Current Running Skill
    public SkillInstance CurrentRunningSkill { get; private set; }
    public void RegisterRunningSkill(SkillInstance skill) { CurrentRunningSkill = skill; }

    public void UnregisterRunningSkill(SkillInstance skill)
    {
        if (CurrentRunningSkill == skill)   // 이미 다른 스킬로 교체됐으면 건드리지 않음
            CurrentRunningSkill = null;
    }

    /// <summary>실행 중 스킬 강제 종료. 새 스킬 시전 직전 / 사망 시 호출.</summary>
    public void InterruptRunningSkill() { CurrentRunningSkill?.HandleAnimationEnd(); }
    #endregion

    public void SetInfo(CombatCreature owner, IList<int> skillIDs)
    {
        _owner = owner;
        Clear();

        if (skillIDs == null)
            return;

        foreach (int id in skillIDs)
        {
            string key = $"Skill_" + id.ToString();
            var def = Managers.Resource.Load<SkillDefinitionSO>(key);
            if (def == null)
            {
                LogError($"[SkillBook] SkillDefinitionSO 로드 실패: {key}");
                continue;
            }
            _skillDic[id] = new SkillInstance(def, owner);
        }
    }

    private void Update()
    {
        if (_owner == null || _owner.IsDead)
            return;

        _phaseRunner?.Tick(Time.deltaTime); // phase 진행 트래킹

        // Cool Time 관리
        if (_activeCooldowns.Count == 0)
            return;
        float dt = Time.deltaTime;
        for (int i = _activeCooldowns.Count - 1; i >= 0; i--)
        {
            var skill = _activeCooldowns[i];
            skill.Tick(dt);

            if (skill.IsReady)
                ListUtil.FastRemoveAt(_activeCooldowns, i);
        }
    }

    /// <summary>
    /// 스킬 시전 시도. Player의 입력 핸들러나 Monster의 BT가 호출.
    /// </summary>
    public bool TryUseSkill(int skillID)
    {
        if (_skillDic.TryGetValue(skillID, out var skill) == false)
            return false;
        if (skill.TryUse() == false)
            return false;

        // Cooldown 시작 — Active 리스트에 등록
        if (_activeCooldowns.Contains(skill) == false)
            _activeCooldowns.Add(skill);

        return true;
    }

    public SkillInstance GetSkill(int skillID)
    {
        return _skillDic.TryGetValue(skillID, out var skill) ? skill : null;
    }

    /// <summary>
    /// Pool 재사용 시 호출 — CombatCreature.SetInfo에서 자동 호출됨.
    /// </summary>
    public void Clear()
    {
        InterruptRunningSkill();

        foreach (var skill in _skillDic.Values)
            skill.Reset();
        _skillDic.Clear();
        _activeCooldowns.Clear();
    }
}