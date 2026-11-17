using UnityEngine;

/// <summary>
/// 부서지는 발판. 박스(GridBox)처럼 맵 프리팹(라운드 루트 아래)에 직접 놓으면 자기 칸을 맵에 등록한다.
/// 언제 밟히고 언제 부서질지는 스스로 정하지 않는다 — 이동한 캐릭터의 상호작용 카드(TileExit + 발판 조건 · 효과)가 Step · Break를 부른다.
/// 밟을 때마다 밟은 횟수의 그림으로 바뀐다. 부서지면 그 칸은 바로 이번 라운드의 타일 데이터에서 빠지고(아무도 들어올 수 없고 점령도 지워짐),
/// 그림은 Animator에 Break 상태가 있으면 그 재생이 끝난 뒤, 없으면 바로 바닥 타일과 함께 사라진다. 라운드가 바뀌면 처음 상태로 돌아간다.
/// </summary>
[DisallowMultipleComponent, RequireComponent(typeof(SpriteRenderer))]
public sealed class GridCrumbleTile : MonoBehaviour
{
    /// <summary>부서지는 애니메이션 상태 이름이다. Animator에 이 상태가 없으면 애니메이션 없이 바로 사라진다.</summary>
    private static readonly int s_breakState = Animator.StringToHash("Break");

    /// <summary>밟은 횟수별 그림이다. 0번 = 처음, 1번 = 한 번 밟은 뒤 … 횟수가 그림 수보다 많으면 마지막 그림을 쓴다.</summary>
    [SerializeField, Tooltip("밟은 횟수별 그림. 0번 = 처음, 1번 = 한 번 밟은 뒤 … 비우면 SpriteRenderer의 처음 그림을 유지")]
    private Sprite[] _stepSprites;

    /// <summary>칸 등록과 타일 데이터 제거를 맡는 맵이며 비활성화 중 전역 매니저를 다시 만들지 않도록 보관한다.</summary>
    private MapManager _map;
    /// <summary>밟은 횟수의 그림을 보여 주는 렌더러다.</summary>
    private SpriteRenderer _renderer;
    /// <summary>부서지는 애니메이션을 재생할 선택 컴포넌트이며 없을 수 있다.</summary>
    private Animator _animator;
    /// <summary>라운드 초기화 때 칸을 다시 계산할 최초 배치의 월드 위치다.</summary>
    private Vector3 _spawnPosition;
    /// <summary>다른 칸 크기에서도 원래 비율을 유지할 최초 로컬 크기다.</summary>
    private Vector3 _authoredScale;
    /// <summary>밟은 횟수별 그림이 없을 때 유지할 저작 그림이다.</summary>
    private Sprite _authoredSprite;
    /// <summary>초기 위치와 컴포넌트를 이미 저장했는지 나타낸다.</summary>
    private bool _initialized;
    /// <summary>부서지는 애니메이션이 끝나기를 기다리는 중인지 나타낸다.</summary>
    private bool _isBreaking;

    /// <summary>발판이 놓인 논리 칸이다.</summary>
    public Vector2Int Cell { get; private set; }
    /// <summary>맵에 칸을 정상 등록했는지 나타낸다.</summary>
    public bool HasCell { get; private set; }
    /// <summary>이번 라운드에 밟힌 횟수다.</summary>
    public int StepCount { get; private set; }
    /// <summary>부서져 칸이 타일 데이터에서 빠졌는지 나타낸다.</summary>
    public bool IsBroken { get; private set; }

    /// <summary>최초 배치와 필요한 컴포넌트를 한 번 준비한다.</summary>
    private void Awake()
    {
        CacheComponents();
    }

    /// <summary>라운드가 켜졌을 때 이미 연결한 맵에 발판을 다시 등록한다.</summary>
    private void OnEnable()
    {
        CacheComponents();
        if (_map != null && _map.IsLoaded)
            RegisterAtSpawn();
    }

    /// <summary>맵 로드 뒤 동적으로 생성한 발판도 기존 맵에 연결한다.</summary>
    private void Start()
    {
        if (_map == null && Managers.Map.IsLoaded)
            Initialize(Managers.Map);
    }

    /// <summary>부서지는 애니메이션이 끝나면 그림을 지운다. 기다리는 중이 아니면 비교 1회로 끝난다.</summary>
    private void Update()
    {
        if (_isBreaking && IsBreakAnimationFinished())
            Vanish();
    }

    /// <summary>맵 또는 라운드에서 사라지는 발판의 등록을 해제한다.</summary>
    private void OnDisable()
    {
        ReleaseRegistration();
    }

    /// <summary>컴포넌트와 최초 저작 위치 · 크기 · 그림을 캐시한다.</summary>
    private void CacheComponents()
    {
        if (_initialized)
            return;
        _initialized = true;
        _spawnPosition = transform.position;
        _authoredScale = transform.localScale;
        _renderer = GetComponent<SpriteRenderer>();
        _authoredSprite = _renderer.sprite;
        _animator = GetComponent<Animator>();
    }

    /// <summary>맵 좌표를 연결하며 현재 활성 라운드의 발판만 최초 칸에 등록한다.</summary>
    /// <param name="map">발판의 칸 등록과 타일 데이터 제거를 맡을 현재 맵이다.</param>
    public void Initialize(MapManager map)
    {
        CacheComponents();
        _map = map;
        if (isActiveAndEnabled)
            RegisterAtSpawn();
    }

    /// <summary>등록을 해제하여 라운드 초기 배치를 다시 등록할 준비를 한다.</summary>
    public void ReleaseRegistration()
    {
        if (_map != null)
            _map.UnregisterCrumbleTile(this);
        HasCell = false;
        _isBreaking = false;
    }

    /// <summary>최초 배치 위치를 칸 중앙에 맞추고 처음 상태로 등록한다.</summary>
    private void RegisterAtSpawn()
    {
        if (_map == null || !_map.IsLoaded || HasCell)
            return;
        Cell = _map.WorldToCell(_spawnPosition);
        // center는 발판을 정렬할 칸 중심의 월드 좌표다.
        Vector2 center = _map.CellCenterToWorld(Cell);
        transform.position = new Vector3(center.x, center.y, _spawnPosition.z);
        transform.localScale = _authoredScale * _map.Grid.CellSize;
        ResetState();
        HasCell = _map.TryRegisterCrumbleTile(this, Cell);
        _renderer.enabled = HasCell;
        if (!HasCell)
            Debug.LogWarning($"[GridCrumbleTile] 발판 배치 실패: {Cell}. 이 라운드의 영역 · 벽과 다른 발판과의 겹침을 확인하세요.", this);
    }

    /// <summary>밟은 횟수와 부서짐을 처음으로 돌리고 처음 그림을 보인다.</summary>
    private void ResetState()
    {
        StepCount = 0;
        IsBroken = false;
        _isBreaking = false;
        if (_animator != null && _animator.isActiveAndEnabled)
            _animator.Rebind(); // 이전 라운드의 Break 재생을 처음 상태로
        ApplyStepSprite();
    }

    /// <summary>한 번 밟는다 — 밟은 횟수를 1 올리고 그 횟수의 그림으로 바꾼다. 부서졌거나 등록되지 않았으면 아무 일도 없다.</summary>
    public void Step()
    {
        if (!HasCell || IsBroken)
            return;
        StepCount++;
        ApplyStepSprite();
    }

    /// <summary>부순다 — 칸은 바로 타일 데이터에서 빠지고, 그림은 Break 애니메이션이 끝난 뒤(없으면 바로) 사라진다.</summary>
    public void Break()
    {
        if (!HasCell || IsBroken || _map == null)
            return;
        IsBroken = true;
        _map.RemoveTile(Cell);

        if (_animator != null && _animator.isActiveAndEnabled && _animator.HasState(0, s_breakState))
        {
            _animator.Play(s_breakState, 0, 0f);
            _animator.Update(0f); // 같은 프레임에 Break 상태로 들어가 끝남 판정이 바로 맞도록
            _isBreaking = true;
            return;
        }
        Vanish();
    }

    /// <summary>Break 상태 재생이 끝났거나 Break를 벗어났는지 확인한다.</summary>
    private bool IsBreakAnimationFinished()
    {
        if (_animator == null || !_animator.isActiveAndEnabled)
            return true;
        if (_animator.IsInTransition(0))
            return false;
        // state는 Break 재생 진행도를 읽을 현재 상태다.
        AnimatorStateInfo state = _animator.GetCurrentAnimatorStateInfo(0);
        return state.shortNameHash != s_breakState || state.normalizedTime >= 1f;
    }

    /// <summary>발판 그림과 그 칸의 바닥 타일을 지운다.</summary>
    private void Vanish()
    {
        _isBreaking = false;
        _renderer.enabled = false;
        if (_map != null && _map.IsLoaded)
            _map.ClearGroundTile(Cell);
    }

    /// <summary>밟은 횟수에 맞는 그림을 적용한다. 그림 목록이 비었으면 저작 그림을 유지한다.</summary>
    private void ApplyStepSprite()
    {
        if (_stepSprites == null || _stepSprites.Length == 0)
        {
            _renderer.sprite = _authoredSprite;
            return;
        }
        // sprite는 밟은 횟수에 맞는 그림이며 횟수가 더 많으면 마지막 그림이다.
        Sprite sprite = _stepSprites[Mathf.Min(StepCount, _stepSprites.Length - 1)];
        if (sprite != null)
            _renderer.sprite = sprite;
    }
}
