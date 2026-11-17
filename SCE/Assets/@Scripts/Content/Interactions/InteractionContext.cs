using UnityEngine;
using static Define;

/// <summary>
/// 평가 1회에 필요한 정보. 트리거 발생 지점에서 만들어 Condition/Effect까지 한 번에 진행.
/// </summary>
public readonly struct InteractionContext
{
    public readonly CreatureBase Owner;          // 상호작용 보유자 (NPC/Villager/Monster 무관)
    public readonly InteractionComponent Source; // 이번 평가를 수행 중인 컴포넌트
    public readonly CreatureBase Instigator;     // 촉발 주체. null 가능 (빙의 대상 없음)
    public readonly ETriggerType Trigger;
    public readonly float Time;                  // 쿨다운 기준 시각 (Time.time)

    /// <summary>상태 조건이 읽는 저장소. 전역 조회 대신 명시적으로 전달 — 테스트 가능, 스폰 요청 단계(Step 7)와 공유.</summary>
    public GameStateManager State { get { return Managers.GameState; } }

    /// <summary>스폰 요청 단계에서만 값이 있다. 그 외에는 null.</summary>
    public readonly SpawnRequest SpawnRequest;

    /// <summary>트리거가 일어난 칸 (TileExit: 보유자가 방금 떠난 칸). HasCell이 거짓이면 의미 없는 값이다.</summary>
    public readonly Vector2Int Cell;
    /// <summary>이 평가에 트리거 칸이 있는지다. 칸을 넘기는 트리거(TileExit)에서만 참이다.</summary>
    public readonly bool HasCell;

    public InteractionContext(InteractionComponent source, CreatureBase owner, CreatureBase instigator,
    ETriggerType trigger, float time, SpawnRequest spawnRequest = null)
    {
        Source = source;
        Owner = owner;
        Instigator = instigator;
        Trigger = trigger;
        Time = time;
        SpawnRequest = spawnRequest;
        Cell = default;
        HasCell = false;
    }

    /// <summary>칸을 넘기는 트리거(TileExit)용. 촉발 주체는 없다 — 보유자 자신의 이동이다.</summary>
    public InteractionContext(InteractionComponent source, CreatureBase owner, ETriggerType trigger, float time, Vector2Int cell)
    {
        Source = source;
        Owner = owner;
        Instigator = null;
        Trigger = trigger;
        Time = time;
        SpawnRequest = null;
        Cell = cell;
        HasCell = true;
    }

    /// <summary>ETargetRef를 실제 대상으로 해석</summary>
    public CreatureBase Resolve(ETargetRef target, int objectId)
    {
        switch (target)
        {
            case ETargetRef.Owner: return Owner;
            case ETargetRef.Instigator: return Instigator;
            //case ETargetRef.ObjectId: return Managers.Object.FindCreature(objectId);
            default: return null;
        }
    }
}