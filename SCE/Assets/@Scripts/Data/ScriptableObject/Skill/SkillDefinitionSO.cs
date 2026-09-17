using System.Collections.Generic;
using UnityEngine;
using static Define;

/// <summary>
/// 한 스킬의 정적 정의. 파일 형태로 저장.
/// 모든 캐릭터가 같은 스킬의 같은 정의를 공유 (메모리 인스턴스 1개).
/// 
/// 사용 흐름:
/// 1. SOdata 폴더에 SO 생성
/// 2. Effects 리스트에 SkillEffectSO 자산들 끌어다 놓기 (조립)
/// 3. Addressable 등록 (key = "Skill_{SkillID}")
/// 4. CombatCreatureData(JSON)의 SkillIDs에 SkillID 추가
/// 5. 런타임에 SkillBook이 ID로 로드하여 SkillInstance 생성
/// </summary>

public abstract class SkillDefinitionSO : ScriptableObject
{
    [Header("Identity")]
    public int SkillID;
    public string SkillNameID;

    [Header("Conditions")]
    [Tooltip("Skill 시전 시 해당 Tag가 있으면 불발")]
    public ECreatureTag InValidTags;

    [Header("Values")]
    public float CooldownTime = 0f;
    public float ManaCost = 0f; // ToDo EStatType.Mana 도입 후 활용
    

    [Header("Casting")]
    [Tooltip("null = 즉발. 지정 시 홀드로 캐스팅 개시")]
    public SkillCastingSO CastingEffects;

    [Header("Animation")]
    [Tooltip("Animator의 State 이름. 비우면 애니메이션 재생 안 함.")]
    public EUserbleAnimState AnimationStateName;

    [Header("Phases (다단계 타임라인)")]
    [Tooltip("비어있으면 단일 동작 스킬 — Animation Event가 종료를 담당. " +
         "1개 이상이면 Runner가 순차 실행하고 마지막 phase 완료가 곧 스킬 종료 " +
         "(이때 Animation Event는 WaitForAnimationEndPhase를 깨우는 신호).")]
    public List<SkillPhaseSO> Phases = new();

    // HurtboxLayout을 바꿔야 하는 경우 animation event로 넣건, phase로 넣자

    [Header("Hit Effects (Hitbox 충돌 시 적용)")]
    [Tooltip("Hitbox와 Hurtbox 충돌 시 적용.")]
    public List<SkillHitEffectSO> HitEffects = new();
}