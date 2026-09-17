using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static Define;
using static UnityEditor.Experimental.GraphView.GraphView;

/// <summary>
/// Hurtbox 묶음. SwitchHurtboxLayout(string) 호출 시 GroupId 매칭으로 활성/비활성 전환.
/// 
/// - 자기 자식 Hurtbox 캐싱 + owner 전파
/// - Hurtbox 그룹 활성/비활성 토글
/// </summary>
public class HurtboxGroup : InitBase
{
    [Tooltip("이 그룹의 식별자. SwitchHurtboxLayout 호출 시 사용.")]
    [SerializeField]
    private string _groupId;
    public string GroupId { get { return _groupId; } }

    private readonly List<Hurtbox> _hurtboxes = new();
    public IReadOnlyList<Hurtbox> Hurtboxes => _hurtboxes;
    public CombatCreature Owner { get; private set; }

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        return true;
    }

    /// <summary>
    /// CombatCreature.Init에서 호출 — 자식 Hurtbox들에 owner 전파.
    /// </summary>
    public void SetOwner(CombatCreature owner)
    {
        if (Owner != owner)
        {
            Owner = owner;
            InitHurtBoxes();
        }

        for (int i = 0; i < _hurtboxes.Count; i++)
            _hurtboxes[i].SetInfo(this);
    }

    private void InitHurtBoxes()
    {
        // 자식 Hurtbox 캐싱 — 비활성 상태에서도 수집
        foreach (var hurtbox in GetComponentsInChildren<Hurtbox>(includeInactive: false))
            _hurtboxes.Add(hurtbox);

    }

    /// <summary>
    /// 그룹 전체 활성/비활성 — GameObject.SetActive 한 번으로 자식 모두 토글.
    /// </summary>
    public void SetActive(bool active)
    {
        if (gameObject.activeSelf == active) return;
        gameObject.SetActive(active);
    }
}