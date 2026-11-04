using UnityEngine;

/// <summary>한 칸을 차지하는 박스의 실제 위치와 맵 등록을 관리하며 스스로 타일을 점령하지 않는다.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
public sealed class GridBox : MonoBehaviour
{
    /// <summary>박스 배치와 이동을 등록한 맵이며 비활성화 중 전역 매니저를 다시 만들지 않는다.</summary>
    private MapManager _map;
    /// <summary>박스의 실제 물리 위치를 이동시킬 중력 없는 키네마틱 바디다.</summary>
    private Rigidbody2D _body;
    /// <summary>등록에 실패한 박스가 보이지 않는 벽이 되지 않도록 제어할 충돌체다.</summary>
    private BoxCollider2D _collider;
    /// <summary>등록에 실패한 박스를 숨기고 올바른 라운드 배치에서 다시 표시할 렌더러다.</summary>
    private SpriteRenderer _renderer;
    /// <summary>라운드 초기화 때 복원할 최초 배치의 월드 위치다.</summary>
    private Vector3 _spawnPosition;
    /// <summary>다른 칸 크기에서도 원래 시각적 비율을 유지할 최초 로컬 크기다.</summary>
    private Vector3 _authoredScale;
    /// <summary>초기 위치와 컴포넌트를 이미 저장했는지 나타낸다.</summary>
    private bool _initialized;

    /// <summary>도착을 완료하여 현재 박스가 차지하는 논리 칸이다.</summary>
    public Vector2Int CurrentCell { get; private set; }
    /// <summary>밀기 중 임시로 차단하는 도착 예정 칸이다.</summary>
    public Vector2Int TargetCell { get; private set; }
    /// <summary>맵에 실제 박스 위치를 정상 등록했는지 나타낸다.</summary>
    public bool HasCell { get; private set; }
    /// <summary>현재 플레이어와 함께 한 칸을 이동 중인지 나타낸다.</summary>
    public bool IsMoving { get; private set; }
    /// <summary>현재 밀기 작업을 관리하며 비활성화 시 취소를 통지할 컴포넌트다.</summary>
    public BoxPushController ActivePush { get; private set; }
    /// <summary>실제 도착 확인에 사용하는 현재 박스 바디의 월드 위치다.</summary>
    public Vector2 Position => _body != null ? _body.position : (Vector2)transform.position;
    /// <summary>라운드 복원 시 플레이어와 겹치는지 검사할 최초 저작 타일 좌표다.</summary>
    public Vector2Int SpawnCell => _map != null && _map.IsLoaded ? _map.WorldToCell(_spawnPosition) : CurrentCell;

    /// <summary>최초 배치와 필요한 물리 컴포넌트를 한 번 준비한다.</summary>
    private void Awake()
    {
        CacheComponents();
    }

    /// <summary>라운드가 켜졌을 때 이미 연결한 맵에 박스를 다시 등록한다.</summary>
    private void OnEnable()
    {
        CacheComponents();
        if (_map != null && _map.IsLoaded)
            RegisterAtSpawn(false);
    }

    /// <summary>맵 로드 뒤 동적으로 생성한 박스도 기존 맵에 연결한다.</summary>
    private void Start()
    {
        if (_map == null && Managers.Map.IsLoaded)
            Initialize(Managers.Map);
    }

    /// <summary>컴포넌트와 최초 저작 위치를 캐시하고 자유 물리 밀림을 사용하지 않도록 설정한다.</summary>
    private void CacheComponents()
    {
        if (_initialized)
            return;
        _initialized = true;
        _spawnPosition = transform.position;
        _authoredScale = transform.localScale;
        _body = GetComponent<Rigidbody2D>();
        _collider = GetComponent<BoxCollider2D>();
        _renderer = GetComponent<SpriteRenderer>();
        _body.bodyType = RigidbodyType2D.Kinematic;
        _body.gravityScale = 0f;
        _body.constraints = RigidbodyConstraints2D.FreezeRotation;
        _body.useFullKinematicContacts = true;
    }

    /// <summary>맵 좌표를 연결하며 현재 활성 라운드의 박스만 최초 칸에 등록한다.</summary>
    /// <param name="map">박스의 논리 칸과 통행 차단을 관리할 현재 맵이다.</param>
    /// <param name="warn">라운드 중간 재배치 전에는 경고를 미루고 최종 배치에서만 보고할지 지정한다.</param>
    public void Initialize(MapManager map, bool warn = true)
    {
        CacheComponents();
        _map = map;
        if (isActiveAndEnabled)
            RegisterAtSpawn(warn);
    }

    /// <summary>기존 등록과 밀기를 정리하여 라운드 초기 배치를 복원할 준비를 한다.</summary>
    public void ReleaseRegistration()
    {
        ActivePush?.CancelPush();
        if (_map != null)
            _map.UnregisterBox(this);
        HasCell = false;
        IsMoving = false;
        ActivePush = null;
    }

    /// <summary>최초 배치 위치를 현재 맵의 칸 중앙에 맞추고 차단 정보에 등록한다.</summary>
    /// <param name="warn">배치가 벽이나 다른 캐릭터와 겹치면 경고할지 나타낸다.</param>
    private void RegisterAtSpawn(bool warn)
    {
        if (_map == null || !_map.IsLoaded || HasCell)
            return;
        CurrentCell = _map.WorldToCell(_spawnPosition);
        TargetCell = CurrentCell;
        _body.position = _map.CellCenterToWorld(CurrentCell);
        transform.localScale = _authoredScale * _map.Grid.CellSize;
        HasCell = _map.TryRegisterBox(this, CurrentCell);
        _body.simulated = HasCell;
        _collider.enabled = HasCell;
        if (_renderer != null)
            _renderer.enabled = HasCell;
        if (!HasCell && warn)
            Debug.LogWarning($"[GridBox] 박스 배치 실패: {CurrentCell}. 벽, 영역, 스폰 위치 및 다른 박스와의 겹침을 확인하세요.", this);
    }

    /// <summary>예약이 성공한 밀기의 도착 칸과 실행 주체를 보관한다.</summary>
    /// <param name="controller">박스와 플레이어를 함께 이동시킬 밀기 실행기다.</param>
    /// <param name="target">이번 박스 이동이 끝날 인접 타일이다.</param>
    internal void BeginPush(BoxPushController controller, Vector2Int target)
    {
        ActivePush = controller;
        TargetCell = target;
        IsMoving = true;
    }

    /// <summary>플레이어의 이번 물리 이동과 같은 양으로 박스 바디를 이동시킨다.</summary>
    /// <param name="position">이번 물리 갱신에서 도달할 박스의 월드 위치다.</param>
    internal void MoveWithPlayer(Vector2 position)
    {
        _body.MovePosition(position);
    }

    /// <summary>맵이 확정한 도착 또는 복귀 칸으로 정렬하고 밀기 상태를 비운다.</summary>
    /// <param name="cell">완료 또는 취소 후 박스가 차지할 논리 칸이다.</param>
    internal void FinishPush(Vector2Int cell)
    {
        CurrentCell = cell;
        TargetCell = cell;
        IsMoving = false;
        ActivePush = null;
        _body.linearVelocity = Vector2.zero;
        if (_map != null && _map.IsLoaded)
        {
            // center는 비활성 바디에서도 Transform까지 일치시킬 완료 또는 복귀 칸 중앙이다.
            Vector2 center = _map.CellCenterToWorld(cell);
            _body.position = center;
            if (!gameObject.activeInHierarchy)
                transform.position = new Vector3(center.x, center.y, transform.position.z);
        }
    }

    /// <summary>맵 또는 라운드에서 사라지는 박스의 밀기와 차단 기록을 해제한다.</summary>
    private void OnDisable()
    {
        ReleaseRegistration();
    }
}
