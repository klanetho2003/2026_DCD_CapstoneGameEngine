using Data;
using System;
using System.Collections.Generic;
using UnityEngine;
using static Define;

/// <summary>
/// 슬롯 → 스킬 ID 매핑.
/// 
/// 런타임 변경 가능 — 장비 교체, 스킬 트리 진행 등에서 활용.
/// 키 binding과는 독립적 — SkillSlot은 논리적 슬롯, 키는 사용자 설정.
/// </summary>
public class SkillSlots
{
    private readonly Dictionary<ESkillSlot, int> _slotToSkillID = new();

    /// <summary>
    /// 슬롯 변경 시 발화. UI 갱신용.
    /// </summary>
    public event Action<ESkillSlot, int> OnSlotChanged;

    public void SetInfo(Villager owner)
    {
        _slotToSkillID.Clear();

        if (string.IsNullOrEmpty(owner.Data.skillSlotLayoutKey))
            return;

        var layout = Managers.Resource.Load<SkillSlotLayoutSO>(owner.Data.skillSlotLayoutKey);
        if (layout == null)
        {
            LogPrinter.LogError($"[{owner.gameObject.name}] SkillSlotLayout 로드 실패: {owner.Data.skillSlotLayoutKey}");
            return;
        }

        foreach (var assignment in layout.Assignments)
            _slotToSkillID[assignment.Slot] = assignment.SkillID;
    }

    public void AssignSlot(ESkillSlot slot, int skillID)
    {
        _slotToSkillID[slot] = skillID;
        OnSlotChanged?.Invoke(slot, skillID);
    }

    public int GetSkillID(ESkillSlot slot)
    {
        return _slotToSkillID.TryGetValue(slot, out int id) ? id : 0;
    }

    public void Clear()
    {
        _slotToSkillID.Clear();
        OnSlotChanged = null;
    }
}