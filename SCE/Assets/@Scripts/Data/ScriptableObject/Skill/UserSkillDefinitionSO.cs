using UnityEngine;
using static Define;

[CreateAssetMenu(menuName = "Data/Skill/Player Skill", fileName = "Skill_숫자ID")]
public class UserSkillDefinitionSO : SkillDefinitionSO
{
    [Header("Cancel Player Skill")]
    [Tooltip("Skill 시전 시 시전 중이던 Skill을 캔슬할 수 있는지 여부")]
    public bool IsCanCancelThisSkill;
    [Tooltip("Skill 시전 시 시전 중이던 Skill을 캔슬할 수 있는지 여부")]
    public bool IsCanCancelOtherSkill = false;

    [Tooltip("실행 중 마우스 조준 추적 여부")]
    public bool AllowAimTrackingWhileRunning = false;
}
