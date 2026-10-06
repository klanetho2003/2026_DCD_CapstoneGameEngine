using UnityEngine;
using static Define;

[CreateAssetMenu(menuName = "Data/Skill/Player Skill", fileName = "Skill_숫자ID")]
public class UserSkillDefinitionSO : SkillDefinitionSO
{
    [Header("Cancel Player Skill")]
    [Tooltip("Skill 시전 시 시전 중이던 Skill을 캔슬할 수 있는지 여부")]
    public bool IsCanCancelThisSkill;
    [Tooltip("Skill 시전 시 시전 중이던 Skill을 캔슬할 수 있는지 여부")]
    public bool Legacy_IsCanCancelOtherSkill = false;
}
