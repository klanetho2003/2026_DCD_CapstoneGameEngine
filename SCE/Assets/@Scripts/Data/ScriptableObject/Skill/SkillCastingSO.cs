using System.Collections.Generic;
using UnityEngine;
using static Define;

/// <summary>
/// 스킬의 캐스팅(선행 준비) 단계 정의 — SkillDefinitionSO의 부품.
/// null = 즉발 스킬. 존재하면 시전 시퀀스에 casting 단계가 삽입
/// </summary>
[CreateAssetMenu(menuName = "Data/Skill/Casting", fileName = "Casting_")]
public class SkillCastingSO : ScriptableObject
{
    [Header("Presentation")]
    [Tooltip("캐스팅 애니메이션 State (루프 클립). 비우면 재생 안 함.")]
    public EUserbleAnimState AnimationStateName;

    [Tooltip("캐스팅 개시 시 1회 적용 — 차지 글로우 VFX 등")]
    public List<SkillEffectSO> CastingEffects = new();

    [Header("Behavior")]
    [Tooltip("캐스팅 중 마우스 조준 추적 허용")]
    public bool AllowAimTracking = true;
}