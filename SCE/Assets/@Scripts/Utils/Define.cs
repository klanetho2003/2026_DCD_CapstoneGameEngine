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
        Movement,
        UI,
        Combat,
        Count // 배열 크기용. 실제 컨텍스트로 사용 금지
    }

    public enum EUserInputState
    {
        Movement = 0,
        Combat,
        None,
        Count // 배열 크기용.
    }
    #endregion

    public enum ERound
    {
        A = 0,
        B = 1,
        C = 2,
    }

    public const int ROUND_COUNT = 3;

    // ── 맵 저작 이름 규약 — MapExporter(읽기)와 MapView(런타임 숨김)가 공유 ──
    public const string MAP_TILEMAP_COLLISION = "Tilemap_Collision";
    public const string MAP_TILEMAP_TERRAIN = "Tilemap_Terrain";
    public const string MAP_TILEMAP_STAGE_OBJECT = "Tilemap_Stage_Object"; // 스폰 마커
    public const string MAP_TILEMAP_MAP_OBJECT = "Tilemap_Map_Object";     // 스폰 마커
    public const string MAP_TILEMAP_WAVE_PREFIX = "Tilemap_Wave_";         // 스폰 마커

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

    public enum ETileType
    {
        None,
        SpawnPoint,
        Wall
    }

    /// <summary>구역 종류. 입력 ActionMap 등 구역별 처리의 분기 기준.</summary>
    public enum EZoneType
    {
        Battlefield = 0, // 기본값 — 기존 맵(설정 없는 JSON)은 전장으로 해석된다
        Village,
    }

    public const char MAP_TOOL_NONE = '0';
    public const char MAP_TOOL_WALL = 'W';
    public const char MAP_PLAYER_SPAWNPOINT = 'P';

    public const int RESTART_DELAY = 3;
    public const int STAGE_REGION_NONE = -1;

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
        StateChanged,    // 게임 상태가 바뀐 프레임에 1회 (+ 새로 등록된 NPC는 다음 프레임 1회)
        SpawnRequest,   // 스폰 직전 (Instantiate 전). 상태 조건 + 스폰 요청 효과만
        Spawned,        // 스폰 직후 (SetInfo 완료 후). CreatureSpawnHelper 경유 스폰에서 호출
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

    /// <summary>비교 연산. [InspectorName]은 에디터 표시 이름일 뿐 — JSON에는 영문 이름(GreaterOrEqual 등)이 그대로 쓰인다.</summary>
    public enum EComparison
    {
        [InspectorName("< 미만")] Less = 0,
        [InspectorName("≤ 이하")] LessOrEqual,
        [InspectorName("> 초과")] Greater,
        [InspectorName("≥ 이상")] GreaterOrEqual,
        [InspectorName("= 같음")] Equal,
    }

    /// <summary>조건의 상대 비용 힌트. 툴의 정렬&경고용, 런타임 미사용</summary>
    public enum ENodeCost { Cheap = 0, Moderate, Expensive }
    #endregion

    #region Game State

    /// <summary>
    /// 게임 이벤트 종류 — 데이터 규칙(JSON)이 이 이름으로 이벤트를 지정한다.
    /// 새 이벤트 => 값 1개 + IGameEvent를 구현한 readonly struct 1개.
    /// </summary>
    public enum EGameEventType
    {
        [InspectorName("(선택 필요)")] None = 0,
        [InspectorName("일차 진행")] DayAdvanced,    // RuleKey => 새 일차
        [InspectorName("개체 사망")] CreatureDied,   // RuleKey => 죽은 대상의 templateID
        [InspectorName("신호")] Signal,              // 기획 정의 신호. RuleKey => 신호 ID (상호작용 효과 등에서 발생)
        [InspectorName("(개수 — 선택 금지)")] Count
    }

    /// <summary>상태 키의 주기 초기화 정책. 일차가 넘어갈 때 적용된다.</summary>
    public enum EStateResetPolicy
    {
        [InspectorName("초기화 안 함")] None = 0,   // 초기화하지 않음 — 업적, 누적 카운터
        [InspectorName("매일")] EveryDay,            // 일차가 넘어갈 때마다 기본값으로
        [InspectorName("N일마다")] EveryNDays,       // (일차 - 1)이 Interval의 배수인 일차에 진입할 때 기본값으로 초기화. 예: Interval 7 → 8, 15, 22일차
    }

    /// <summary>상태 규칙의 값 변경 연산.</summary>
    public enum EStateOp
    {
        [InspectorName("= 입력값으로")] Set = 0,
        [InspectorName("+ 입력값만큼")] Add,
        [InspectorName("큰 값 유지 (max)")] Max,
        [InspectorName("작은 값 유지 (min)")] Min,
    }

    /// <summary>효과가 실행되는 단계.</summary>
    public enum EEffectPhase
    {
        Instance = 0,   // 스폰된 인스턴스 대상 (기존 효과 전부)
        SpawnRequest,   // 스폰 요청 수정 — Instantiate 전
    }
    #endregion

    public const float DIRECTION_EPSILON_SQR = 0.0001f;

}
