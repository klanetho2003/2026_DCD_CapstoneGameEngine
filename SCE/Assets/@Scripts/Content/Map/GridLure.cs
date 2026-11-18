using System;
using UnityEngine;

/// <summary>
/// 유인 객체의 활성 상태와 그림, 몬스터가 따라갈 거리 지도. 상호작용 보유자(유인 NPC)에 붙인다 — 짝 버튼의 Set(유인 켜기)이 Activate를, 자기 Set(유인 끄기)이 Deactivate를 부른다.
/// 켜지는 순간 있던 모든 몬스터가 이 칸으로 한 칸씩 걸어오고 (MonsterLureFollower), 꺼지면 이 객체로 오던 몬스터는 모두 원래 행동 트리로 돌아간다.
/// 라운드가 바뀌면 꺼진 상태로 돌아간다.
/// </summary>
[DisallowMultipleComponent, RequireComponent(typeof(SpriteRenderer))]
public sealed class GridLure : MonoBehaviour
{
    /// <summary>활성 상태 그림이다. 비어 있으면 SpriteRenderer의 처음 그림을 유지한다.</summary>
    [SerializeField, Tooltip("활성 상태 그림. 비우면 SpriteRenderer의 처음 그림을 유지")]
    private Sprite _activeSprite;

    /// <summary>칸 계산과 라운드 알림을 받는 맵이며 비활성화 중 전역 매니저를 다시 만들지 않도록 보관한다.</summary>
    private MapManager _map;
    /// <summary>몬스터 목록을 제공하며 비활성화 중 전역 매니저를 다시 만들지 않도록 보관한다.</summary>
    private ObjectManager _objects;
    /// <summary>상태 그림을 보여 주는 렌더러다.</summary>
    private SpriteRenderer _renderer;
    /// <summary>비활성 상태에 보여 줄 저작 그림이다.</summary>
    private Sprite _inactiveSprite;
    /// <summary>라운드 알림 구독 · 해제에 같은 인스턴스를 쓰도록 1회 만든 처리기다.</summary>
    private Action _onRoundChanged;
    /// <summary>이 객체의 칸까지 몬스터가 따라갈 거리 지도다.</summary>
    private readonly TileDistanceField _field = new TileDistanceField();

    /// <summary>몬스터를 끌어당기는 중인지 나타낸다.</summary>
    public bool IsActive { get; private set; }
    /// <summary>마지막으로 켜질 때 계산한 이 객체의 칸이다.</summary>
    public Vector2Int Cell => _field.Goal;

    /// <summary>컴포넌트와 저작 그림을 한 번 캐시한다.</summary>
    private void Awake()
    {
        _renderer = GetComponent<SpriteRenderer>();
        _inactiveSprite = _renderer.sprite;
        _onRoundChanged = Deactivate;
    }

    /// <summary>스폰(풀에서 꺼냄 포함)될 때 꺼진 상태로 시작하고 라운드 전환을 구독한다.</summary>
    private void OnEnable()
    {
        _map = Managers.Map;
        _objects = Managers.Object;
        _map.RoundChanged += _onRoundChanged;
        IsActive = false;
        ApplySprite();
    }

    /// <summary>디스폰 시 따라오던 몬스터를 놓아주고 구독을 해제한다. 보관한 매니저만 쓴다.</summary>
    private void OnDisable()
    {
        Deactivate();
        if (_map != null)
            _map.RoundChanged -= _onRoundChanged;
    }

    /// <summary>켠다 — 지금 있는 모든 몬스터가 이 칸으로 오기 시작한다. 이미 켜졌으면 아무 일도 없다. O(몬스터 수)</summary>
    public void Activate()
    {
        if (IsActive || _map == null || !_map.IsLoaded)
            return;
        _field.SetGoal(_map.WorldToCell(transform.position));
        if (!_field.TryGetDistance(_map, Cell, out _))
            LogPrinter.LogWarning($"[GridLure] '{name}' — 유인 칸 {Cell}에 갈 수 없어 몬스터가 오지 않음 (벽 · 박스 · 라운드 영역 확인)");
        IsActive = true;
        ApplySprite();
        // monster는 켜지는 순간 존재하는 몬스터이며 실제 이동은 각자의 다음 갱신에서 시작한다.
        foreach (MonsterBase monster in _objects.Monsters)
        {
            if (monster != null)
                monster.Lure.Follow(this);
        }
    }

    /// <summary>끈다 — 이 객체로 오던 몬스터는 모두 원래 행동 트리로 돌아간다. 이미 꺼졌으면 아무 일도 없다. O(몬스터 수)</summary>
    public void Deactivate()
    {
        if (!IsActive)
            return;
        IsActive = false;
        ApplySprite();
        if (_objects == null)
            return;
        // monster는 이 객체를 따라오던 몬스터일 수 있으며 다른 유인을 따라가는 몬스터는 그대로 둔다.
        foreach (MonsterBase monster in _objects.Monsters)
        {
            if (monster != null)
                monster.Lure.Release(this);
        }
    }

    /// <summary>칸에서 이 객체까지의 걸음 수를 읽는다. 닿을 수 없으면 거짓이다.</summary>
    /// <param name="cell">몬스터가 선점한 현재 칸이다.</param>
    /// <param name="distance">이 객체까지의 걸음 수다.</param>
    public bool TryGetDistance(Vector2Int cell, out int distance)
    {
        return _field.TryGetDistance(_map, cell, out distance);
    }

    /// <summary>이 객체에 한 걸음 더 가까운 이웃 가운데 지금 들어갈 수 있는 칸의 방향을 고른다.</summary>
    /// <param name="cell">몬스터가 선점한 현재 칸이다.</param>
    /// <param name="canEnter">다른 캐릭터의 선점까지 확인할 몬스터의 통행 조회다.</param>
    /// <param name="direction">고른 방향이며 실패하면 0이다.</param>
    public bool TryGetStep(Vector2Int cell, Func<Vector2Int, bool> canEnter, out Vector2Int direction)
    {
        return _field.TryGetStep(_map, cell, canEnter, out direction);
    }

    /// <summary>활성 상태에 맞는 그림을 적용한다.</summary>
    private void ApplySprite()
    {
        _renderer.sprite = IsActive && _activeSprite != null ? _activeSprite : _inactiveSprite;
    }
}
