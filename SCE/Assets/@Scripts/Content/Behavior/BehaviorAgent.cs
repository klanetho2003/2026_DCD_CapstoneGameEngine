using UnityEngine;

/// <summary>
/// 트리를 실행하는 몸 — 실행기와 노드가 몸에서 읽고 시키는 창구. 게임은 몬스터, 시험은 가짜.
/// 노드는 몸의 형식 (MonsterBase)을 직접 보지 않는다 — 몸이 더 할 일 (이동 · 스킬)은 이 창구를 넓힌 창구로 묻는다.
/// </summary>
public interface IBehaviorAgent
{
    /// <summary>지금 자리 (월드)</summary>
    Vector2 Position { get; }

    /// <summary>생애 번호 (CreatureBase.SpawnSerial) — 바뀌면 새 생애다 (집 자리 · 평가 흩뜨림을 다시 정한다). 깨우기는 그대로</summary>
    int SpawnSerial { get; }

    /// <summary>새 행동을 시작하지 않는 동안 — 스킬 · 기절 · 강제 이동 중</summary>
    bool IsActionLocked { get; }

    /// <summary>고른 태그 중 하나라도 있는가 — CreatureBase.HasTag와 같은 판정</summary>
    bool HasTag(Define.ECreatureTag tags);

    /// <summary>목적지 이동을 멈춘다</summary>
    void StopMoving();

    /// <summary>Blackboard 키의 처음 값 출처 (BehaviorBlackboardKey.Seed) → 정수. 모르는 출처면 false</summary>
    bool TryGetIntSeed(string seed, out int value);
}

/// <summary>트리 밖 — 실행기 · 노드가 읽는 게임 상태. 게임은 Managers의 지금 것, 시험은 가짜. 노드는 Managers를 직접 읽지 않는다</summary>
public interface IBehaviorWorld
{
    /// <summary>조종 캐릭터 — 거리 LOD의 기준 · 대상 찾기. 없으면 null</summary>
    CreatureBase PossessedTarget { get; }
}