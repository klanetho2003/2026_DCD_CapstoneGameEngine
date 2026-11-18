using System;
using UnityEngine;

/// <summary>
/// 버튼의 눌림 상태와 그림. 상호작용 보유자(버튼 NPC)에 붙인다 — 보유자의 Set(TileEnter + 버튼 조건 · 효과)이 Press를 부른다.
/// 한 번 눌리면 이번 라운드 동안 눌린 채로 남고, 라운드가 바뀌면 눌리지 않은 상태로 돌아간다.
/// </summary>
[DisallowMultipleComponent, RequireComponent(typeof(SpriteRenderer))]
public sealed class GridButton : MonoBehaviour
{
    /// <summary>눌린 상태 그림이다. 비어 있으면 SpriteRenderer의 처음 그림을 유지한다.</summary>
    [SerializeField, Tooltip("눌린 상태 그림. 비우면 SpriteRenderer의 처음 그림을 유지")]
    private Sprite _pressedSprite;

    /// <summary>라운드 알림을 받는 맵이며 비활성화 중 전역 매니저를 다시 만들지 않도록 보관한다.</summary>
    private MapManager _map;
    /// <summary>상태 그림을 보여 주는 렌더러다.</summary>
    private SpriteRenderer _renderer;
    /// <summary>눌리지 않은 상태에 보여 줄 저작 그림이다.</summary>
    private Sprite _releasedSprite;
    /// <summary>라운드 알림 구독 · 해제에 같은 인스턴스를 쓰도록 1회 만든 처리기다.</summary>
    private Action _onRoundChanged;

    /// <summary>이번 라운드에 눌렸는지 나타낸다.</summary>
    public bool IsPressed { get; private set; }

    /// <summary>컴포넌트와 저작 그림을 한 번 캐시한다.</summary>
    private void Awake()
    {
        _renderer = GetComponent<SpriteRenderer>();
        _releasedSprite = _renderer.sprite;
        _onRoundChanged = ResetState;
    }

    /// <summary>스폰(풀에서 꺼냄 포함)될 때 눌리지 않은 상태로 시작하고 라운드 전환을 구독한다.</summary>
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
    }

    /// <summary>누른다 — 눌린 상태와 그림으로 바꾼다. 이미 눌렸으면 아무 일도 없다.</summary>
    public void Press()
    {
        if (IsPressed)
            return;
        IsPressed = true;
        ApplySprite();
    }

    /// <summary>눌리지 않은 처음 상태와 그림으로 돌린다.</summary>
    private void ResetState()
    {
        IsPressed = false;
        ApplySprite();
    }

    /// <summary>눌림 상태에 맞는 그림을 적용한다.</summary>
    private void ApplySprite()
    {
        _renderer.sprite = IsPressed && _pressedSprite != null ? _pressedSprite : _releasedSprite;
    }
}
