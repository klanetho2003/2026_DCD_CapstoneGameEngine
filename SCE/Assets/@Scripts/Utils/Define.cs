using System;
using UnityEngine;

/// <summary>
/// 공용 Enum 혹은 상수 정의 Class
/// </summary>

public static class Define
{
    #region Object
    public enum EObjectType
    {
        None = -1,
        Villager,
        Monster,
        NPC,
        Projectile,
    }

    public enum EUserbleAnimState
    {
        None,
        Locomotion,
        Idle,
        Attack,
        Damaged,
    }
    #endregion

    #region Scene
    public enum EScene
    {
        Unknown = -1,

        TitleScene,
        GameScene,
    }
    #endregion

    #region UI
    public enum UIEvent
    {
        None = 0,
        PointerEnter,
        PointerExit,
        Click,
        PointerDown,
        PointerUp,
        BeginDrag,
        Drag,
        EndDrag
    }
    #endregion

    #region Input
    public enum EActionMap
    {
        None = 0,
        UI,
        Movement,
        Count // 배열 크기용. 실제 컨텍스트로 사용 금지
    }

    public enum EUserInputState
    {
        Movement = 0,
        Damaged,
        Count // 배열 크기용.
    }
    #endregion

    /// <summary>
    /// 모든 Creature가 가질 수 있는 행동/상태 태그. Flag로 다중 동시 보유 가능.
    /// 
    /// 사용 예:
    ///   creature.AddTag(ECreatureTag.Attacking);
    ///   if (creature.HasTag(ECreatureTag.Invincible)) { ... }
    /// </summary>
    [Flags]
    public enum ECreatureTag
    {
        None = 0,
        UsingSkill = 1 << 0,
        Invincible = 1 << 1,
        Healing = 1 << 2,
        Stunned = 1 << 3,
        ForceMoving = 1 << 4,
        // ..
    }

    public enum ELayer
    {
        Default = 0,
        TransparentFX = 1,
        IgnoreRaycast = 2,
        Dummy1 = 3,
        Water = 4,
        UI = 5,
        ShadowMap = 6,
        Creature = 7,
        //
        Player_HitBox = 10,
        Monster_HitBox = 11,
        //
        Player_Hurtbox = 20,
        Monster_Hurtbox = 21,
    }

    #region Creature Battle

    public enum EStatType
    {
        MaxHp,
        MaxMp,
        Atk,
        Def,
        MoveSpeed,
        //AttackRange,

        CritRate,        // (0.0 ~ 1.0)
        CritDamage,      // (배율, 예: 1.5 = 150%)
    }

    public enum EStatResourceType
    {
        CurrentHp
    }

    public enum EStatModType
    {
        None = 0,
        Add = 100,          // Base에 더함. 예: +5
        PercentAdd = 200,   // 같은 그룹 합산 후 한 번 곱. ex, +10% +20% → ×1.30
        PercentMult = 300,  // 각각 독립 곱. ex, +10% +20% → ×1.10 ×1.20
    }

    public enum EDamageType
    {
        None = 0,
        Physical,
        Magical,
        True,            // 방어 무시 (장차 사용)
    }

    public enum EElement
    {
        None = 0,
        Fire,
        Ice,
        Lightning,
        Holy,
        Dark,
    }

    public enum EBodyPart
    {
        Body,
        Head,
        Arm,
        Leg,
        Weakpoint,   // 약점 / 특수 부위
    }

    public enum ESkillSlot
    {
        None = -1,
        SlotA = 0,
        SlotB = 1,
        SlotC = 2,
        SlotD = 3,

        SlotLShift = 4,
        //...
    }
    #endregion

    #region Interaction
    /// <summary>평가를 일으키는 사건. Condition(상태)과 구분된다.</summary>
    public enum ETriggerType
    {
        Tick = 0,       // 매 프레임 (InteractionManager.Update)
        InputInteract,  // Interact 키
        Zone,           // NPC 트리거 콜라이더 진입/이탈 양쪽 사건. 진입/이탈 구분은 InZone 조건으로
        Count
    }

    public enum EActivationMode
    {
        Latched = 0, // 조건이 참인 동안 활성. 진입 시 OnActivate, 이탈 시 OnDeactivate
        Fire         // 트리거마다 조건 통과 시 OnActivate 1회
    }

    /// <summary>조건/효과가 참조하는 대상</summary>
    public enum ETargetRef
    {
        Owner = 0,   // 상호작용 보유 NPC
        Instigator,  // 촉발 주체 (ex. Possessed Villager)
        ObjectId     // 특정 오브젝트 (보스 등) — ObjectId 필드 필요
    }

    public enum EComparison { Less = 0, LessOrEqual, Greater, GreaterOrEqual, Equal }

    /// <summary>조건의 상대 비용 힌트. 툴의 정렬&경고용, 런타임 미사용</summary>
    public enum ENodeCost { Cheap = 0, Moderate, Expensive }
    #endregion

    public const float DIRECTION_EPSILON_SQR = 0.0001f;

}
