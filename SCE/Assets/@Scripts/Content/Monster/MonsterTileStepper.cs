using System;
using UnityEngine;

/// <summary>몬스터 한 개의 한 칸 출발과 도착 결과, 정지 중 시선을 관리하며 행동 트리의 칸 이동 노드가 쓰도록 몬스터마다 하나를 둔다.</summary>
public sealed class MonsterTileStepper
{
    /// <summary>한 칸 이동을 실행하고 도착을 알려줄 몬스터다.</summary>
    private readonly MonsterBase _owner;

    /// <summary>한 칸 도착 때마다 새 델리게이트를 만들지 않기 위한 공통 이동 완료 콜백이다.</summary>
    private readonly Action _onMovementArrived;

    /// <summary>이번 출발을 요청한 쪽이 실제 도착 뒤 받을 통지이며 없으면 null이다.</summary>
    private Action _onArrived;

    /// <summary>정지 중에도 유지되는 논리적인 시선 및 직진 방향이다.</summary>
    public Vector2Int Facing { get; private set; } = Vector2Int.right;

    /// <summary>시작한 이동의 성공 콜백을 아직 기다리는지 나타낸다.</summary>
    public bool IsAwaitingArrival { get; private set; }

    /// <summary>마지막으로 시작한 한 칸이 선점까지 마친 실제 도착으로 끝났는지 나타내며 출발, 취소, 초기화에서 거짓이 된다.</summary>
    public bool LastStepArrived { get; private set; }

    /// <summary>몬스터가 지금 선점한 타일이다.</summary>
    public Vector2Int CurrentCell => _owner.Movement.CurrentCell;

    /// <summary>순찰 방향 규칙에 넘길 이 몬스터의 공통 통행 조회이며 생성 시 한 번만 만든다.</summary>
    public Func<Vector2Int, bool> CanEnter { get; }

    /// <summary>도착 후 Dictionary의 키로 기록할 실제 출발 타일이다.</summary>
    public Vector2Int StepOrigin { get; private set; }

    /// <summary>도착 후 Dictionary의 값으로 기록할 실제 이동 방향이다.</summary>
    public Vector2Int StepDirection { get; private set; }

    /// <summary>출발을 실행할 몬스터와 재사용 콜백을 연결한다.</summary>
    /// <param name="owner">공통 이동 컴포넌트와 이동 속도를 제공할 몬스터다.</param>
    public MonsterTileStepper(MonsterBase owner)
    {
        _owner = owner;
        _onMovementArrived = OnMovementArrived;
        CanEnter = cell => Managers.Map.CanEnterCell(cell, _owner.Movement);
    }

    /// <summary>새 생애나 배치 변경 시 도착 대기를 비우고 필요하면 초기 방향인 오른쪽으로 되돌린다.</summary>
    /// <param name="resetFacing">참이면 초기 방향인 오른쪽으로 되돌린다.</param>
    public void Reset(bool resetFacing)
    {
        IsAwaitingArrival = false;
        LastStepArrived = false;
        _onArrived = null;
        if (resetFacing)
            Facing = Vector2Int.right;
    }

    /// <summary>한 칸 이동의 기록 후보를 보관하고 플레이어의 실제 속도에 맞춰 이동을 요청한다.</summary>
    /// <param name="direction">현재 칸에서 도착하려는 인접 칸의 방향이다.</param>
    /// <param name="onArrived">선점까지 완료한 실제 도착 뒤 한 번 실행할 통지이며 없으면 null이다.</param>
    public bool TryStartStep(Vector2Int direction, Action onArrived)
    {
        if (IsAwaitingArrival || _owner.MoveSpeed <= 0f)
            return false;
        StepOrigin = _owner.Movement.CurrentCell;
        StepDirection = direction;
        LastStepArrived = false;
        _onArrived = onArrived;

        // 플레이어의 현재 최종 이동 속도를 비교할 빙의 대상이다.
        Villager player = Managers.Object.PossessedTarget;
        // 플레이어가 없으면 기본 속도, 있으면 동일한 실제 속도를 적용할 배율이다.
        float speedWeight = player != null && player.Movement != null
            ? player.Movement.EffectiveMoveSpeed / _owner.MoveSpeed : 1f;
        IsAwaitingArrival = _owner.Movement.TryMoveToCell(
            StepOrigin + direction, speedWeight, _onMovementArrived);
        if (IsAwaitingArrival)
            Facing = direction;
        else
            _onArrived = null;
        return IsAwaitingArrival;
    }

    /// <summary>시작한 이동이 양보나 물리 장애물로 도착 없이 끝났으면 한 번 참을 반환하고 도착 대기를 푼다.</summary>
    public bool TryConsumeCancelled()
    {
        if (!IsAwaitingArrival || _owner.Movement.IsMoving)
            return false;
        IsAwaitingArrival = false;
        LastStepArrived = false;
        _onArrived = null;
        return true;
    }

    /// <summary>라운드 정지나 사망 시 진행 중인 이동을 취소하고 선점 중인 칸 중앙에 정렬한다.</summary>
    public void Cancel()
    {
        _owner.Movement.CancelToCurrentCell();
        IsAwaitingArrival = false;
        LastStepArrived = false;
        _onArrived = null;
    }

    /// <summary>공통 이동 컴포넌트의 도착 콜백이며 대기를 푼 뒤 요청한 쪽에 한 번 알린다.</summary>
    private void OnMovementArrived()
    {
        IsAwaitingArrival = false;
        LastStepArrived = true;
        // 통지 안에서 다음 출발을 요청해도 그 통지를 지우지 않도록 먼저 비운다.
        Action callback = _onArrived;
        _onArrived = null;
        callback?.Invoke();
    }
}
