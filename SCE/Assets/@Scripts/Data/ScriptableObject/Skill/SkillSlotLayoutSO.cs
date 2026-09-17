using System;
using System.Collections.Generic;
using UnityEngine;
using static Define;

[CreateAssetMenu(menuName = "Data/Skill Slot Layout", fileName = "SlotLayout_")]
public class SkillSlotLayoutSO : ScriptableObject
{
    [Serializable]
    public struct SlotAssignment
    {
        public ESkillSlot Slot;
        public int SkillID;
    }

    [Tooltip("초기 슬롯 배치. 런타임에 SkillSlots.AssignSlot으로 변경 가능.")]
    public List<SlotAssignment> Assignments = new();
}