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

    /// <summary>소유자 등록 시 연결한 맵이며 비활성화 중 전역 매니저를 다시 만들지 않도록 보관한다.</summary>
    private MapManager _map;

    /// <summary>실제 도착을 완료하여 현재 선점하고 있는 타일이다.</summary>
    private Vector2Int _currentCell;

    /// <summary>이번 한 칸 이동이 향하는 타일이며 출발 시 선점하지 않는다.</summary>
    private Vector2Int _targetCell;

    /// <summary>공격용 트리거를 제외하고 실제 타일 진입을 감지할 이동 충돌체다.</summary>
    private Collider2D _movementCollider;

    /// <summary>이번 목적지에 실제로 진입하여 점령 처리를 한 번 완료했는지 나타낸다.</summary>
    private bool _capturedTarget;

    /// <summary>현재 맵에 출발 또는 도착 칸을 정상 등록했는지 나타낸다.</summary>
    public bool HasCell { get; private set; }

    /// <summary>도착 선점이 등록된 현재 칸을 제공한다.</summary>
    public Vector2Int CurrentCell => _currentCell;

    /// <summary>진행 중인 이동의 목적지 칸을 제공한다.</summary>
    public Vector2Int TargetCell => _targetCell;

    /// <summary>충돌이나 진입 거절 이후 원래 선점한 칸으로 복귀 중인지 나타낸다.</summary>
    public bool IsReturning { get; private set; }

    /// <summary>동일 목적지 충돌 시 플레이어에게 우선권을 주기 위한 소유자 분류다.</summary>
    public bool IsPlayerControlled => _owner is Villager;

    /// <summary>점령에 사용할 진영이며 플레이어와 몬스터가 아닌 이동 주체는 중립이다.</summary>
    public TileOwner TerritoryOwner => _owner is Villager ? TileOwner.Player :
        _owner is MonsterBase ? TileOwner.Monster : TileOwner.Neutral;

    /// <summary>사망 통지 도중에도 죽은 캐릭터의 이전 선점이 새 스폰을 막지 않도록 생존을 확인한다.</summary>
    public bool CanOccupyCell
    {
        get
        {
            // creature는 체력 자원이 있는 전투 캐릭터인 경우 생존 여부를 조회할 소유자다.
            return _owner != null && !(_owner is CombatCreature creature && creature.IsDead);
        }
    }

    /// <summary>물리 장애물 때문에 실제 위치가 움직이지 않은 누적 시간이다.</summary>
    private float _stalledSeconds;

    /// <summary>물리 진행 정체를 감지하기 위한 직전 위치다.</summary>
    private Vector2 _previousPhysicsPosition;

    /// <summary>실제 위치와 속도를 제어하는 캐릭터의 2D 물리 컴포넌트다.</summary>
    public Rigidbody2D RB2D { get; private set; }

    /// <summary>현재 캐릭터가 한 칸의 목적지를 향해 이동 중인지 나타낸다.</summary>
    public bool IsMoving { get; private set; }

    /// <summary>소유 캐릭터의 스탯에서 제공하는 기본 이동 속도다.</summary>
    public float MoveSpeed => _owner.MoveSpeed;

    /// <summary>진행 중인 한 칸 또는 다음 일반 입력에 적용하는 실제 월드 이동 속도다.</summary>
    public float EffectiveMoveSpeed => IsMoving ? _stepSpeed : MoveSpeed * _inputSpeedWeight;

    /// <summary>애니메이션에서 사용할 실제 이동 방향이며, 소유자가 없으면 0이다.</summary>
    public Vector2 CacheMoveDirection =>
        _owner != null ? _owner.CacheMoveDirection : Vector2.zero;

    /// <summary>공통 초기화 후 실제 이동 바디와 진입 판정용 일반 충돌체를 한 번 찾아 보관한다.</summary>
    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        RB2D = GetComponent<Rigidbody2D>();
        // collider는 이동 판정에 사용할 루트의 일반 충돌체 후보이며 공격용 트리거는 제외한다.
        foreach (Collider2D collider in GetComponents<Collider2D>())
        {
            if (!collider.isTrigger && collider.enabled)
            {
                _movementCollider = collider;
                break;
            }
        }
        return true;
    }

    /// <summary>이전 이동을 정리하고 소유자를 연결한 뒤 스폰 위치를 타일 중앙에 맞춘다.</summary>
    /// <param name="owner">이동 속도와 이동 방향을 공유할 소유 캐릭터다.</param>
    public void SetInfo(IMovable owner)
    {
        ReleaseClaim();
        ClearMovement();
        _owner = owner;
        _map = Managers.Map;
        _inputSpeedWeight = owner is Villager ? Villager.DEFAULT_MOVE_SPEED_WEIGHT : 1f;

        if (RB2D != null && _map.IsLoaded)
        {
            // 스폰 위치가 포함된 타일의 정수 좌표다.
            Vector2Int cell = _map.WorldToCell(RB2D.position);
            RB2D.position = _map.CellCenterToWorld(cell);
            _currentCell = cell;
            _targetCell = cell;
            HasCell = _map.TryClaimCell(cell, this);
            if (HasCell)
                _map.TryCaptureCell(cell, this);
            if (!HasCell)
                Debug.LogWarning($"[Movement] 스폰 타일 선점 실패: {cell}", this);
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
        if (_map != null && _map.IsLoaded)
            TryMoveToCell(_map.WorldToCell(destination), speedWeight, onComplete);
    }

    /// <summary>인접 타일 이동을 시작할 수 있는지 반환하며 완료 전에는 목적지를 선점하지 않는다.</summary>
    /// <param name="targetCell">이번에 실제로 도착하려는 인접 타일이다.</param>
    /// <param name="speedWeight">이번 이동에 적용할 기본 속도 배율이다.</param>
    /// <param name="onComplete">선점 갱신까지 성공한 실제 도착 후 실행할 콜백이다.</param>
    public bool TryMoveToCell(Vector2Int targetCell, float speedWeight = 1f, Action onComplete = null)
    {
        if (_owner == null || RB2D == null || !isActiveAndEnabled ||
            !HasCell || IsMoving || _map == null || !_map.IsLoaded)
            return false;
        if (_map.HasTerritoryRound && !_map.IsTerritoryOpen)
            return false;

        // 현재 타일과 목적지 타일 사이의 좌표 차이다.
        Vector2Int difference = targetCell - _currentCell;

        // 대각선과 두 칸 이상의 이동을 거절하기 위한 타일 거리다.
        int cellDistance = Mathf.Abs(difference.x) + Mathf.Abs(difference.y);

        if (cellDistance != 1 || !CanEnterCell(targetCell))
            return false;

        // 기본 이동 속도에 이번 요청의 배율을 적용한 실제 속도다.
        float speed = MoveSpeed * Mathf.Max(0f, speedWeight);
        if (speed <= 0f)
            return false;

        _startPosition = RB2D.position;
        _destination = _map.CellCenterToWorld(targetCell);
        _targetCell = targetCell;
        _stepSpeed = speed;
        _duration = Mathf.Max(Time.fixedDeltaTime, Vector2.Distance(_startPosition, _destination) / speed);
        _elapsed = 0f;
        _onComplete = onComplete;

        IsMoving = true;
        IsReturning = false;
        _isInputDriven = false;
        _stalledSeconds = 0f;
        _previousPhysicsPosition = RB2D.position;
        _capturedTarget = false;
        RB2D.linearVelocity = Vector2.zero;
        _owner.CacheMoveDirection = (_destination - _startPosition).normalized;
        return true;
    }

    /// <summary>실제 도착을 확인하고 입력 유지에 따른 연속 이동과 보간 속도를 갱신한다.</summary>
    private void FixedUpdate()
    {
        if (_owner == null || RB2D == null)
            return;

        if (_map == null || !_map.IsLoaded)
        {
            ReleaseClaim();
            ClearMovement();
            return;
        }

        if (!HasCell)
            return;

        if (_map.HasTerritoryRound && !_map.IsTerritoryOpen)
        {
            CancelToCurrentCell();
            return;
        }

        // creature는 전투 캐릭터일 때 사망 여부를 검사할 소유자다.
        if (_owner is CombatCreature creature && creature.IsDead)
        {
            CancelToCurrentCell();
            return;
        }

        if (IsMoving && !IsReturning &&
            (!CanEnterCell(_targetCell) || _map.ShouldYieldDestination(this, _targetCell)))
            BeginReturn();

        if (IsMoving && !IsReturning)
            CaptureCurrentTileEntry();

        if (IsMoving)
        {
            // 지난 물리 업데이트 이후 실제 위치와 목적지 사이의 제곱 거리다.
            float distanceSquared = (RB2D.position - _destination).sqrMagnitude;
            if (distanceSquared <= POSITION_EPSILON * POSITION_EPSILON)
                CompleteStep();
            else if (!IsReturning)
            {
                _stalledSeconds = (RB2D.position - _previousPhysicsPosition).sqrMagnitude <
                    POSITION_EPSILON * POSITION_EPSILON
                    ? _stalledSeconds + Time.fixedDeltaTime : 0f;
                if (_stalledSeconds >= 0.35f)
                    BeginReturn();
            }
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
        _previousPhysicsPosition = RB2D.position;
    }

    /// <summary>저장된 입력으로 다음 타일을 계산하고 진입 가능한 경우 한 칸 이동을 시작한다.</summary>
    private void TryStartNextStep()
    {
        if (IsMoving || _inputDirection == Vector2.zero)
            return;

        if (!HasCell || _map == null || !_map.IsLoaded)
            return;

        // 다음 이동을 출발할 현재 타일의 정수 좌표다.
        Vector2Int currentCell = _currentCell;

        // 저장된 4방향 입력을 타일 좌표의 한 칸 차이로 바꾼 값이다.
        Vector2Int offset = new Vector2Int(
            Mathf.RoundToInt(_inputDirection.x),
            Mathf.RoundToInt(_inputDirection.y));

        // 현재 타일에서 선택한 방향으로 한 칸 떨어진 목적지 타일이다.
        Vector2Int targetCell = currentCell + offset;
        if (!CanEnterCell(targetCell))
            return;

        // 목적지 타일의 중앙에 해당하는 월드 좌표다.
        Vector2 destination = _map.CellCenterToWorld(targetCell);
        ForceMoveTo(destination, _inputSpeedWeight);

        // 일반 입력으로 시작했으므로 도착 후에도 입력 유지 여부를 확인한다.
        _isInputDriven = true;
    }

    /// <summary>도착 오차를 중앙으로 보정하고 이동을 마친 뒤 완료 콜백을 한 번 실행한다.</summary>
    private void CompleteStep()
    {
        if (!IsReturning && !_map.TryClaimCell(_targetCell, this))
        {
            BeginReturn();
            return;
        }

        if (!IsReturning)
        {
            _map.ReleaseCell(_currentCell, this);
            _currentCell = _targetCell;
            if (!_capturedTarget)
                _map.TryCaptureCell(_currentCell, this);
        }
        RB2D.position = _destination;
        RB2D.linearVelocity = Vector2.zero;
        IsMoving = false;
        IsReturning = false;
        _owner.CacheMoveDirection = Vector2.zero;

        // 재진입하더라도 같은 완료 콜백을 중복 실행하지 않도록 보관한 값이다.
        Action callback = _onComplete;
        _onComplete = null;
        callback?.Invoke();
    }

    /// <summary>실제로 이동한 충돌체가 목적지 내부에 진입하면 중앙 도착보다 먼저 점령한다.</summary>
    public void CaptureCurrentTileEntry()
    {
        if (_map != null && _map.IsLoaded && HasCell && RB2D != null && IsMoving && !IsReturning)
            UpdateTerritoryEntry();
    }

    /// <summary>목적지 경계 안으로 실제 진입했는지 검사하고 한 번의 이동에 한 번만 점령한다.</summary>
    private void UpdateTerritoryEntry()
    {
        if (_capturedTarget || !_map.IsTerritoryOpen)
            return;
        // direction은 현재 선점 칸에서 인접 목적지로 향하는 한 칸 방향이다.
        Vector2Int direction = _targetCell - _currentCell;
        if (Vector2.Dot(RB2D.position - _startPosition, (Vector2)direction) <= POSITION_EPSILON)
            return;
        // center는 진입을 감지할 목적지 셀의 월드 중심이다.
        Vector2 center = _map.CellCenterToWorld(_targetCell);
        // leadingPoint는 목적지에 가장 가까운 실제 이동 충돌체 표면이며 충돌체가 없으면 판정 위치다.
        Vector2 leadingPoint = _movementCollider != null && _movementCollider.enabled
            ? _movementCollider.ClosestPoint(center) : RB2D.position;
        // penetration은 목적지의 진입 경계보다 안쪽으로 들어간 깊이며 단순 경계 접촉은 제외한다.
        float penetration = direction.x != 0
            ? (leadingPoint.x - center.x) * direction.x + _map.Grid.CellSize * 0.5f
            : (leadingPoint.y - center.y) * direction.y + _map.Grid.CellSize * 0.5f;
        if (penetration > POSITION_EPSILON)
            _capturedTarget = _map.TryCaptureCell(_targetCell, this);
    }

    /// <summary>맵 범위와 벽 데이터, 현재 라운드의 영역을 검사하여 타일 진입 여부를 반환한다.</summary>
    /// <param name="cell">진입 가능 여부를 검사할 타일의 정수 좌표다.</param>
    private bool CanEnterCell(Vector2Int cell)
    {
        return _map != null && _map.CanEnterCell(cell, this);
    }

    /// <summary>실패한 도착 콜백을 버리고 여전히 자신이 선점한 출발 칸으로 부드럽게 복귀한다.</summary>
    private void BeginReturn()
    {
        _onComplete = null;
        IsReturning = true;
        _startPosition = RB2D.position;
        _destination = _map.CellCenterToWorld(_currentCell);
        _targetCell = _currentCell;
        _elapsed = 0f;
        _duration = Mathf.Max(Time.fixedDeltaTime,
            Vector2.Distance(_startPosition, _destination) / Mathf.Max(0.01f, _stepSpeed));
        _owner.CacheMoveDirection = (_destination - _startPosition).normalized;
    }

    /// <summary>라운드 정지나 사망 시 진행 중인 이동을 취소하고 선점 중인 칸 중앙에 정렬한다.</summary>
    public void CancelToCurrentCell()
    {
        ClearMovement();
        if (HasCell && _map != null && _map.IsLoaded && RB2D != null)
            RB2D.position = _map.CellCenterToWorld(_currentCell);
    }

    /// <summary>외부 위치 변경 시 기존 이동을 취소하고 도착 칸의 선점을 함께 갱신한다.</summary>
    /// <param name="worldPosition">새로 배치할 위치가 포함된 타일의 월드 좌표다.</param>
    public bool TryPlaceAt(Vector2 worldPosition)
    {
        if (_map == null || !_map.IsLoaded || RB2D == null || _owner == null)
            return false;
        // 외부 배치 요청을 타일 중앙 좌표로 변환하기 위한 목적지 칸이다.
        Vector2Int cell = _map.WorldToCell(worldPosition);
        if (!_map.TryClaimCell(cell, this))
            return false;
        if (HasCell && cell != _currentCell)
            _map.ReleaseCell(_currentCell, this);
        ClearMovement();
        _currentCell = cell;
        _targetCell = cell;
        HasCell = true;
        RB2D.position = _map.CellCenterToWorld(cell);
        _map.TryCaptureCell(cell, this);
        return true;
    }

    /// <summary>맵을 다시 조회하지 않고 자신이 등록한 기존 타일 선점만 해제한다.</summary>
    private void ReleaseClaim()
    {
        // 마지막 물리 진행 직후 정산용 디스폰이 발생해도 이미 진입한 타일의 점령은 반영한다.
        CaptureCurrentTileEntry();
        if (HasCell && _map != null)
            _map.ReleaseCell(_currentCell, this);
        HasCell = false;
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
        IsReturning = false;
        _isInputDriven = false;
        _inputDirection = Vector2.zero;
        _inputSpeedWeight = _owner is Villager ? Villager.DEFAULT_MOVE_SPEED_WEIGHT : 1f;
        _elapsed = 0f;
        _duration = 0f;
        _stepSpeed = 0f;
        _onComplete = null;

        _capturedTarget = false;

        if (RB2D != null)
            RB2D.linearVelocity = Vector2.zero;

        if (_owner != null)
            _owner.CacheMoveDirection = Vector2.zero;
    }

    /// <summary>비활성화 또는 풀 반환 시 이전 이동과 콜백을 정리하고 소유자 연결을 해제한다.</summary>
    private void OnDisable()
    {
        ReleaseClaim();
        ClearMovement();
        _owner = null;
    }
}
