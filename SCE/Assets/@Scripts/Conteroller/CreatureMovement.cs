using System;
using UnityEngine;
using static Define;

/// <summary>캐릭터의 방향 입력과 타일 중앙 사이의 한 칸 이동을 관리한다.</summary>
public class CreatureMovement : InitBase
{
    /// <summary>목적지에 도착했다고 판단할 수 있는 최대 위치 오차다.</summary>
    private const float POSITION_EPSILON = 0.001f;

    /// <summary>이동 속도와 애니메이션용 이동 방향을 제공하는 소유 캐릭터다.</summary>
    private IMovable _owner;

    /// <summary>다음 한 칸 이동에 사용할 현재 입력 방향이며, 입력 해제 시 0이 된다.</summary>
    private Vector2 _inputDirection;

    /// <summary>일반 입력으로 시작할 다음 한 칸 이동의 속도 배율이다.</summary>
    private float _inputSpeedWeight = 1f;

    /// <summary>현재 한 칸 이동의 보간 기준이 되는 고정 시작 위치다.</summary>
    private Vector2 _startPosition;

    /// <summary>현재 한 칸 이동이 끝나야 하는 타일 중앙의 월드 좌표다.</summary>
    private Vector2 _destination;

    /// <summary>현재 한 칸 이동을 시작한 이후의 누적 물리 시간이다.</summary>
    private float _elapsed;

    /// <summary>현재 한 칸의 거리를 이동 속도로 나눈 예정 이동 시간이다.</summary>
    private float _duration;

    /// <summary>현재 한 칸 이동에 적용하는 최대 월드 이동 속도다.</summary>
    private float _stepSpeed;

    /// <summary>현재 목적지에 실제로 도착한 뒤 한 번 실행할 완료 콜백이다.</summary>
    private Action _onComplete;

    /// <summary>일반 입력에 의한 이동인지 나타내며, 입력 유지 시 연속 이동을 허용한다.</summary>
    private bool _isInputDriven;

    /// <summary>실제 위치와 속도를 제어하는 캐릭터의 2D 물리 컴포넌트다.</summary>
    public Rigidbody2D RB2D { get; private set; }

    /// <summary>현재 캐릭터가 한 칸의 목적지를 향해 이동 중인지 나타낸다.</summary>
    public bool IsMoving { get; private set; }

    /// <summary>소유 캐릭터의 스탯에서 제공하는 기본 이동 속도다.</summary>
    public float MoveSpeed => _owner.MoveSpeed;

    /// <summary>애니메이션에서 사용할 실제 이동 방향이며, 소유자가 없으면 0이다.</summary>
    public Vector2 CacheMoveDirection =>
        _owner != null ? _owner.CacheMoveDirection : Vector2.zero;

    /// <summary>공통 초기화 후 이동에 사용할 Rigidbody2D를 한 번 찾아 보관한다.</summary>
    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        RB2D = GetComponent<Rigidbody2D>();
        return true;
    }

    /// <summary>이전 이동을 정리하고 소유자를 연결한 뒤 스폰 위치를 타일 중앙에 맞춘다.</summary>
    /// <param name="owner">이동 속도와 이동 방향을 공유할 소유 캐릭터다.</param>
    public void SetInfo(IMovable owner)
    {
        ClearMovement();
        _owner = owner;

        if (RB2D != null && Managers.Map.IsLoaded)
        {
            // 스폰 위치가 포함된 타일의 정수 좌표다.
            Vector2Int cell = Managers.Map.WorldToCell(RB2D.position);
            RB2D.position = Managers.Map.CellCenterToWorld(cell);
        }
    }

    /// <summary>4방향 입력을 저장하고 정지 중이면 다음 한 칸 이동을 요청한다.</summary>
    /// <param name="direction">원본 입력 방향이며, 0은 이번 이동 완료 후 정지를 의미한다.</param>
    /// <param name="speedWeight">다음 한 칸 이동에 적용할 기본 속도의 배율이다.</param>
    public void Move(Vector2 direction, float speedWeight = 1f)
    {
        if (_owner == null || RB2D == null || !isActiveAndEnabled)
            return;

        _inputDirection = ToFourDirection(direction);
        _inputSpeedWeight = Mathf.Max(0f, speedWeight);
        _isInputDriven = true;

        // 새 입력은 저장하지만 진행 중인 한 칸의 목적지는 바꾸지 않는다.
        if (IsMoving)
            return;

        TryStartNextStep();
    }

    /// <summary>인접한 한 칸의 목적지 중앙으로 이동을 시작하고 도착 콜백을 보관한다.</summary>
    /// <param name="destination">이동할 인접 타일에 포함되는 월드 좌표다.</param>
    /// <param name="speedWeight">이번 한 칸 이동에 적용할 기본 속도의 배율이다.</param>
    /// <param name="onComplete">목적지 도착 시 실행하며, 이동 거절 시에는 실행하지 않는다.</param>
    public void ForceMoveTo(
        Vector2 destination,
        float speedWeight = 1f,
        Action onComplete = null)
    {
        if (_owner == null || RB2D == null || !isActiveAndEnabled)
            return;

        if (IsMoving || !Managers.Map.IsLoaded)
            return;

        // 이동을 시작하는 현재 타일의 정수 좌표다.
        Vector2Int currentCell = Managers.Map.WorldToCell(RB2D.position);

        // 요청한 월드 좌표가 포함된 목적지 타일의 정수 좌표다.
        Vector2Int targetCell = Managers.Map.WorldToCell(destination);

        // 현재 타일과 목적지 타일 사이의 좌표 차이다.
        Vector2Int difference = targetCell - currentCell;

        // 대각선과 두 칸 이상의 이동을 거절하기 위한 타일 거리다.
        int cellDistance = Mathf.Abs(difference.x) + Mathf.Abs(difference.y);

        if (cellDistance != 1 || !CanEnterCell(targetCell))
            return;

        // 기본 이동 속도에 이번 요청의 배율을 적용한 실제 속도다.
        float speed = MoveSpeed * Mathf.Max(0f, speedWeight);
        if (speed <= 0f)
            return;

        _startPosition = RB2D.position;
        _destination = Managers.Map.CellCenterToWorld(targetCell);
        _stepSpeed = speed;
        _duration = Vector2.Distance(_startPosition, _destination) / speed;
        _elapsed = 0f;
        _onComplete = onComplete;

        IsMoving = true;
        _isInputDriven = false;
        RB2D.linearVelocity = Vector2.zero;
        _owner.CacheMoveDirection = (_destination - _startPosition).normalized;
    }

    /// <summary>실제 도착을 확인하고 입력 유지에 따른 연속 이동과 보간 속도를 갱신한다.</summary>
    private void FixedUpdate()
    {
        if (_owner == null || RB2D == null)
            return;

        if (!Managers.Map.IsLoaded)
        {
            ClearMovement();
            return;
        }

        if (IsMoving)
        {
            // 지난 물리 업데이트 이후 실제 위치와 목적지 사이의 제곱 거리다.
            float distanceSquared = (RB2D.position - _destination).sqrMagnitude;
            if (distanceSquared <= POSITION_EPSILON * POSITION_EPSILON)
                CompleteStep();
        }

        // 완료 콜백에서 디스폰된 경우에는 추가 이동을 진행하지 않는다.
        if (_owner == null || !isActiveAndEnabled)
            return;

        if (!IsMoving)
        {
            if (_isInputDriven)
                TryStartNextStep();

            if (!IsMoving)
            {
                RB2D.linearVelocity = Vector2.zero;
                _owner.CacheMoveDirection = Vector2.zero;
                return;
            }
        }

        _elapsed = Mathf.Min(_elapsed + Time.fixedDeltaTime, _duration);

        // 고정된 시작점과 목적지 사이의 보간에 사용할 0~1 진행도다.
        float progress = _elapsed / _duration;

        // 이번 물리 업데이트가 끝날 때 도달하려는 보간 위치다.
        Vector2 nextPosition = Vector2.Lerp(_startPosition, _destination, progress);

        // 실제 현재 위치에서 보간 위치까지 한 물리 주기 동안 이동할 속도다.
        Vector2 velocity = (nextPosition - RB2D.position) / Time.fixedDeltaTime;
        RB2D.linearVelocity = Vector2.ClampMagnitude(velocity, _stepSpeed);
    }

    /// <summary>저장된 입력으로 다음 타일을 계산하고 진입 가능한 경우 한 칸 이동을 시작한다.</summary>
    private void TryStartNextStep()
    {
        if (IsMoving || _inputDirection == Vector2.zero)
            return;

        if (!Managers.Map.IsLoaded)
            return;

        // 다음 이동을 출발할 현재 타일의 정수 좌표다.
        Vector2Int currentCell = Managers.Map.WorldToCell(RB2D.position);

        // 저장된 4방향 입력을 타일 좌표의 한 칸 차이로 바꾼 값이다.
        Vector2Int offset = new Vector2Int(
            Mathf.RoundToInt(_inputDirection.x),
            Mathf.RoundToInt(_inputDirection.y));

        // 현재 타일에서 선택한 방향으로 한 칸 떨어진 목적지 타일이다.
        Vector2Int targetCell = currentCell + offset;
        if (!CanEnterCell(targetCell))
            return;

        // 목적지 타일의 중앙에 해당하는 월드 좌표다.
        Vector2 destination = Managers.Map.CellCenterToWorld(targetCell);
        ForceMoveTo(destination, _inputSpeedWeight);

        // 일반 입력으로 시작했으므로 도착 후에도 입력 유지 여부를 확인한다.
        _isInputDriven = true;
    }

    /// <summary>도착 오차를 중앙으로 보정하고 이동을 마친 뒤 완료 콜백을 한 번 실행한다.</summary>
    private void CompleteStep()
    {
        RB2D.position = _destination;
        RB2D.linearVelocity = Vector2.zero;
        IsMoving = false;
        _owner.CacheMoveDirection = Vector2.zero;

        // 재진입하더라도 같은 완료 콜백을 중복 실행하지 않도록 보관한 값이다.
        Action callback = _onComplete;
        _onComplete = null;
        callback?.Invoke();
    }

    /// <summary>맵 범위와 벽 데이터, 현재 라운드의 영역을 검사하여 타일 진입 여부를 반환한다.</summary>
    /// <param name="cell">진입 가능 여부를 검사할 타일의 정수 좌표다.</param>
    private bool CanEnterCell(Vector2Int cell)
    {
        // 타일 크기와 범위, 벽 정보가 저장된 현재 맵 데이터다.
        var map = Managers.Map.CurrentMap;
        if (map == null)
            return false;

        if (cell.x < 0 || cell.x >= map.Grid.Width ||
            cell.y < 0 || cell.y >= map.Grid.Height)
            return false;

        if (map.CollisionRows[cell.y][cell.x] == MAP_TOOL_WALL)
            return false;

        if (Managers.Stage.IsRunning && Managers.Map.IsOutsideArea(cell))
            return false;

        // 박스, 다른 캐릭터, 다른 캐릭터가 선점한 칸의 판정은 후속 작업이다.
        return true;
    }

    /// <summary>원본 입력을 상하좌우 중 하나로 제한하며 같은 크기의 대각선 입력은 가로를 우선한다.</summary>
    /// <param name="direction">키보드나 게임패드에서 전달된 원본 방향 벡터다.</param>
    private static Vector2 ToFourDirection(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return Vector2.zero;

        if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
            return new Vector2(Mathf.Sign(direction.x), 0f);

        return new Vector2(0f, Mathf.Sign(direction.y));
    }

    /// <summary>이동 상태와 입력, 완료 콜백, 물리 속도, 애니메이션용 방향을 초기화한다.</summary>
    private void ClearMovement()
    {
        IsMoving = false;
        _isInputDriven = false;
        _inputDirection = Vector2.zero;
        _inputSpeedWeight = 1f;
        _elapsed = 0f;
        _duration = 0f;
        _stepSpeed = 0f;
        _onComplete = null;

        if (RB2D != null)
            RB2D.linearVelocity = Vector2.zero;

        if (_owner != null)
            _owner.CacheMoveDirection = Vector2.zero;
    }

    /// <summary>비활성화 또는 풀 반환 시 이전 이동과 콜백을 정리하고 소유자 연결을 해제한다.</summary>
    private void OnDisable()
    {
        ClearMovement();
        _owner = null;
    }
}
