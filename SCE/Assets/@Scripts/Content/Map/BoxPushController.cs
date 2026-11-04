using UnityEngine;
using static Define;

/// <summary>방향 입력에 따른 박스와 플레이어의 한 칸 이동을 하나의 작업으로 관리한다.</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(100)]
public sealed class BoxPushController : MonoBehaviour
{
    /// <summary>실제 박스와 플레이어의 도착을 인정할 위치 오차다.</summary>
    private const float ARRIVAL_EPSILON = 0.001f;
    /// <summary>물리 충돌로 함께 움직이지 못하면 밀기를 취소할 최대 대기 시간이다.</summary>
    private const float STALL_LIMIT = 0.35f;
    /// <summary>이 밀기 실행기를 사용하는 플레이어다.</summary>
    private Villager _owner;
    /// <summary>입력 방향과 실제 한 칸 이동을 제공하는 플레이어 이동 컴포넌트다.</summary>
    private CreatureMovement _movement;
    /// <summary>박스 위치와 이동 중 임시 차단을 제공하는 현재 맵이다.</summary>
    private MapManager _map;
    /// <summary>현재 함께 이동 중인 박스다.</summary>
    private GridBox _box;
    /// <summary>밀기 시작 순간의 플레이어 바디 위치다.</summary>
    private Vector2 _playerStart;
    /// <summary>밀기 시작 순간의 박스 바디 위치다.</summary>
    private Vector2 _boxStart;
    /// <summary>이번에 움직이는 박스의 원래 논리 칸이다.</summary>
    private Vector2Int _boxOrigin;
    /// <summary>이번 박스 이동의 상하좌우 한 칸 방향이다.</summary>
    private Vector2Int _direction;
    /// <summary>밀기 중 플레이어가 실제로 움직였는지 비교할 직전 위치다.</summary>
    private Vector2 _previousPlayerPosition;
    /// <summary>플레이어 물리 위치가 멈춘 누적 시간이다.</summary>
    private float _stalledSeconds;
    /// <summary>플레이어의 성공한 중앙 도착 콜백을 받았는지 나타낸다.</summary>
    private bool _playerArrived;
    /// <summary>이번 밀기 시작에서만 공통 이동기의 잠금을 통과하도록 허용한다.</summary>
    public bool IsStartingPushStep { get; private set; }
    /// <summary>두 물체의 이동과 임시 차단이 정리되기 전까지 다른 밀기를 막는 상태다.</summary>
    public bool IsPushing { get; private set; }

    /// <summary>실제 플레이어와 공통 이동기, 현재 맵을 연결하고 이전 밀기를 정리한다.</summary>
    /// <param name="owner">방향 입력으로 박스를 밀 플레이어다.</param>
    public void SetInfo(Villager owner)
    {
        CancelPush();
        _owner = owner;
        _movement = owner != null ? owner.Movement : null;
        _map = Managers.Map;
    }

    /// <summary>현재 유지 중인 방향으로 앞의 박스 하나를 밀 수 있는지 부작용 없이 검사한다.</summary>
    public bool CanPush()
    {
        if (IsPushing || _owner == null || !_owner.IsPossessed || _owner.IsDead ||
            _movement == null || !_movement.HasCell || _movement.IsMoving ||
            _map == null || !_map.IsLoaded || Managers.Stage.Phase != EStagePhase.Playing ||
            _owner.HasTag(ECreatureTag.UsingSkill | ECreatureTag.Stunned | ECreatureTag.ForceMoving))
            return false;
        // direction은 입력 해제 시 0으로 돌아가는 실제 현재 방향이다.
        Vector2Int direction = Vector2Int.RoundToInt(_movement.InputDirection);
        // box는 플레이어 앞 한 칸에 실제로 등록되어 있는 박스다.
        return direction != Vector2Int.zero &&
            _map.TryGetBox(_movement.CurrentCell + direction, out GridBox box) &&
            _map.CanPushBox(box, _movement, direction);
    }

    /// <summary>유효한 방향 입력과 빈 목적지를 다시 확인한 뒤 한 칸 밀기를 시작한다.</summary>
    public bool TryPush()
    {
        if (!CanPush())
            return false;
        _direction = Vector2Int.RoundToInt(_movement.InputDirection);
        if (!_map.TryGetBox(_movement.CurrentCell + _direction, out _box) ||
            !_map.TryReserveBoxPush(_box, _movement, _direction))
        {
            _box = null;
            return false;
        }
        _boxOrigin = _box.CurrentCell;
        _playerStart = _movement.RB2D.position;
        _boxStart = _box.Position;
        _previousPlayerPosition = _playerStart;
        _stalledSeconds = 0f;
        _playerArrived = false;
        IsPushing = true;
        _box.BeginPush(this, _boxOrigin + _direction);
        IsStartingPushStep = true;
        // speedWeight는 기존 플레이어의 최종 이동 속도를 바꾸지 않고 한 칸 이동에 전달할 배율이다.
        float speedWeight = _movement.MoveSpeed > 0f
            ? _movement.EffectiveMoveSpeed / _movement.MoveSpeed : 0f;
        // started는 플레이어의 박스 원래 칸 진입까지 허용되었는지 나타낸다.
        bool started = _movement.TryMoveToCell(_boxOrigin, speedWeight, OnPlayerArrived);
        IsStartingPushStep = false;
        if (!started)
        {
            Finish(false);
            return false;
        }
        return true;
    }

    /// <summary>공통 이동기의 속도 계산 뒤 박스를 같은 거리만큼 이동시키고 실제 완료를 확인한다.</summary>
    private void FixedUpdate()
    {
        if (!IsPushing)
            return;
        if (_owner == null || _owner.IsDead || !_owner.IsPossessed || _box == null ||
            !_box.isActiveAndEnabled || _map == null || !_map.IsLoaded ||
            Managers.Stage.Phase != EStagePhase.Playing ||
            !_map.IsReservedBoxPushValid(_box, _movement) || _movement.IsReturning)
        {
            CancelPush();
            return;
        }
        // destination은 박스가 실제로 도착해야 하는 다음 칸 중앙이다.
        Vector2 destination = _map.CellCenterToWorld(_boxOrigin + _direction);
        if (_playerArrived && (_box.Position - destination).sqrMagnitude <= ARRIVAL_EPSILON * ARRIVAL_EPSILON)
        {
            Finish(true);
            return;
        }
        if (!_playerArrived && !_movement.IsMoving)
        {
            CancelPush();
            return;
        }
        _stalledSeconds = (_movement.RB2D.position - _previousPlayerPosition).sqrMagnitude <
            ARRIVAL_EPSILON * ARRIVAL_EPSILON ? _stalledSeconds + Time.fixedDeltaTime : 0f;
        _previousPlayerPosition = _movement.RB2D.position;
        if (_stalledSeconds >= STALL_LIMIT)
        {
            CancelPush();
            return;
        }
        // nextPlayer는 공통 이동기가 이번 물리 갱신에 도달하려는 실제 이동 위치다.
        Vector2 nextPlayer = _movement.RB2D.position + _movement.RB2D.linearVelocity * Time.fixedDeltaTime;
        // progress는 진행 방향으로 이동한 실제 거리를 한 칸 크기로 나눈 0~1 진행도다.
        float progress = Mathf.Clamp01(Vector2.Dot(nextPlayer - _playerStart, (Vector2)_direction) / _map.Grid.CellSize);
        _box.MoveWithPlayer(Vector2.Lerp(_boxStart, destination, progress));
    }

    /// <summary>플레이어의 도착 선점이 끝났음을 기록하며 박스의 실제 도착까지 잠금은 유지한다.</summary>
    private void OnPlayerArrived()
    {
        _playerArrived = true;
    }

    /// <summary>외부 정지나 사망 시 중간 밀기를 정리하고 플레이어와 박스의 칸을 일치시킨다.</summary>
    /// <param name="cancelPlayerMovement">거짓이면 호출자가 공통 이동기의 정지를 직접 마무리한다.</param>
    public void CancelPush(bool cancelPlayerMovement = true)
    {
        if (!IsPushing)
            return;
        // commit은 이미 도착한 플레이어 칸에 박스를 되돌려 겹치지 않도록 하는 완료 보정 여부다.
        bool commit = _movement != null && _movement.HasCell && _movement.CurrentCell == _boxOrigin;
        Finish(commit);
        if (cancelPlayerMovement && _movement != null)
            _movement.CancelToCurrentCell();
    }

    /// <summary>박스의 논리 위치와 임시 차단을 확정 또는 복원한 뒤 일반 방향 이동을 다시 허용한다.</summary>
    /// <param name="commit">참이면 목적지에 확정하고 거짓이면 출발 칸으로 복원한다.</param>
    private void Finish(bool commit)
    {
        // box는 상태를 먼저 해제한 뒤 맵에 전달할 이번 밀기의 박스다.
        GridBox box = _box;
        IsPushing = false;
        IsStartingPushStep = false;
        _box = null;
        if (_map != null && box != null)
            _map.FinishBoxPush(box, _movement, commit);
        if (_movement != null)
            _movement.ResumeInputAfterPush();
    }

    /// <summary>플레이어가 비활성화될 때 박스와 임시 차단이 남지 않게 한다.</summary>
    private void OnDisable()
    {
        CancelPush();
    }
}
