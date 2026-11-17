using UnityEngine;

/// <summary>
/// 부서지는 발판의 상태와 그림. 상호작용 보유자(발판 NPC)에 붙인다 — 보유자의 Set(TileExit + 발판 조건 · 효과)이 Step · Break를 부른다.
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

    /// <summary>타일 데이터 제거와 라운드 알림을 받는 맵이며 비활성화 중 전역 매니저를 다시 만들지 않도록 보관한다.</summary>
    private MapManager _map;
    /// <summary>밟은 횟수의 그림을 보여 주는 렌더러다.</summary>
    private SpriteRenderer _renderer;
    /// <summary>부서지는 애니메이션을 재생할 선택 컴포넌트이며 없을 수 있다.</summary>
    private Animator _animator;
    /// <summary>밟은 횟수별 그림이 없을 때 유지할 저작 그림이다.</summary>
    private Sprite _authoredSprite;
    /// <summary>라운드 알림 구독 · 해제에 같은 인스턴스를 쓰도록 1회 만든 처리기다.</summary>
    private System.Action _onRoundChanged;
    /// <summary>부서지는 애니메이션이 끝나기를 기다리는 중인지 나타낸다.</summary>
    private bool _isBreaking;
    /// <summary>부서지며 타일 데이터에서 뺀 칸이며 그림을 지울 때 쓴다.</summary>
    private Vector2Int _brokenCell;

    /// <summary>이번 라운드에 밟힌 횟수다.</summary>
    public int StepCount { get; private set; }
    /// <summary>부서져 칸이 타일 데이터에서 빠졌는지 나타낸다.</summary>
    public bool IsBroken { get; private set; }

    /// <summary>컴포넌트와 저작 그림을 한 번 캐시한다.</summary>
    private void Awake()
    {
        _renderer = GetComponent<SpriteRenderer>();
        _authoredSprite = _renderer.sprite;
        _animator = GetComponent<Animator>();
        _onRoundChanged = ResetState;
    }

    /// <summary>스폰(풀에서 꺼냄 포함)될 때 처음 상태로 시작하고 라운드 전환을 구독한다.</summary>
    private void OnEnable()
    {
        _map = Managers.Map;
        _map.RoundChanged += _onRoundChanged;
        ResetState();
    }

    /// <summary>디스폰 시 구독을 해제한다. 보관한 맵만 쓰고 전역 매니저는 다시 찾지 않는다.</summary>
    private void OnDisable()
    {
        if (_map != null)
            _map.RoundChanged -= _onRoundChanged;
        _isBreaking = false;
    }

    /// <summary>부서지는 애니메이션이 끝나면 그림을 지운다. 기다리는 중이 아니면 비교 1회로 끝난다.</summary>
    private void Update()
    {
        if (_isBreaking && IsBreakAnimationFinished())
            Vanish();
    }

    /// <summary>밟은 횟수와 부서짐을 처음으로 돌리고 처음 그림을 보인다.</summary>
    private void ResetState()
    {
        StepCount = 0;
        IsBroken = false;
        _isBreaking = false;
        _renderer.enabled = true;
        if (_animator != null && _animator.isActiveAndEnabled)
            _animator.Rebind(); // 이전 라운드의 Break 재생을 처음 상태로
        ApplyStepSprite();
    }

    /// <summary>한 번 밟는다 — 밟은 횟수를 1 올리고 그 횟수의 그림으로 바꾼다. 부서졌으면 아무 일도 없다.</summary>
    public void Step()
    {
        if (IsBroken)
            return;
        StepCount++;
        ApplyStepSprite();
    }

    /// <summary>부순다 — 칸은 바로 타일 데이터에서 빠지고, 그림은 Break 애니메이션이 끝난 뒤(없으면 바로) 사라진다.</summary>
    /// <param name="cell">발판이 놓인 칸이다.</param>
    public void Break(Vector2Int cell)
    {
        if (IsBroken || _map == null)
            return;
        IsBroken = true;
        _brokenCell = cell;
        _map.RemoveTile(cell);

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
        if (_map != null)
            _map.ClearGroundTile(_brokenCell);
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
