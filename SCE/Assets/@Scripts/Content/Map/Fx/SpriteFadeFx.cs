using System;
using DG.Tweening;
using UnityEngine;

/// <summary>그림 하나를 흐리게 하며 없앤다 (박스 생성 위치 문양 등). 끝나면 숨긴다.</summary>
[DisallowMultipleComponent]
public sealed class SpriteFadeFx : MonoBehaviour
{
    [SerializeField, Tooltip("흐려질 그림. 비우면 이 오브젝트")]
    private SpriteRenderer _visual;

    [SerializeField, Min(0.01f), Tooltip("사라지는 시간(초)")]
    private float _fadeDuration = 0.2f;

    [SerializeField, Tooltip("사라지는 중 불투명도 (1 → 0)")]
    private AnimationCurve _fadeAlpha = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));

    [SerializeField, Tooltip("사라지는 중 크기 배율. 1로 두면 크기 변화 없음")]
    private AnimationCurve _fadeScale = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1.15f));

    [SerializeField, Tooltip("사라질 때 재생할 소리 · 이펙트")]
    private GimmickFxSlot _fadeSlot = new GimmickFxSlot { Volume = 1f };

    /// <summary>원래 색이다.</summary>
    private Color _restColor;
    /// <summary>원래 로컬 크기다.</summary>
    private Vector3 _restScale;
    /// <summary>진행 중인 트윈이다.</summary>
    private Tween _tween;
    /// <summary>원래 모양을 잡았는지 나타낸다. 다른 컴포넌트가 이 Awake보다 먼저 부를 수 있어 지연 초기화한다.</summary>
    private bool _initialized;

    /// <summary>사라지는 중인지 나타낸다.</summary>
    public bool IsPlaying => _tween != null && _tween.IsActive();
    /// <summary>사라지는 데 걸리는 시간(초)이다. 다음 연출을 겹쳐 시작할 시점 계산에 쓴다.</summary>
    public float Duration => _fadeDuration;

    /// <summary>그림과 원래 모양을 잡는다.</summary>
    private void Awake()
    {
        Init();
    }

    /// <summary>그림과 원래 모양을 한 번 잡는다.</summary>
    private void Init()
    {
        if (_initialized)
            return;
        _initialized = true;
        if (_visual == null)
            _visual = GetComponent<SpriteRenderer>();
        if (_visual != null)
        {
            _restColor = _visual.color;
            _restScale = _visual.transform.localScale;
        }
    }

    /// <summary>꺼질 때 남은 트윈을 지운다.</summary>
    private void OnDisable()
    {
        _tween?.Kill();
        _tween = null;
    }

    /// <summary>흐려지며 사라진다. 끝나면 숨긴다.</summary>
    /// <param name="onComplete">끝난 뒤 한 번 불린다.</param>
    public void PlayFadeOut(Action onComplete = null)
    {
        Init();
        if (_visual == null || !_visual.enabled)
        {
            onComplete?.Invoke();
            return;
        }
        _tween?.Kill();
        _fadeSlot.Play(_visual.transform.position);
        _tween = DOTween.To(() => 0f, Apply, 1f, _fadeDuration)
            .SetEase(Ease.Linear)
            .SetLink(gameObject)
            .OnComplete(() =>
            {
                _tween = null;
                _visual.enabled = false;
                onComplete?.Invoke();
            });
    }

    /// <summary>원래 모양으로 되돌리고 다시 보인다.</summary>
    public void ResetVisual()
    {
        Init();
        _tween?.Kill();
        _tween = null;
        if (_visual == null)
            return;
        _visual.color = _restColor;
        _visual.transform.localScale = _restScale;
        _visual.enabled = true;
    }

    /// <summary>정규화 시간의 불투명도와 크기를 적용한다.</summary>
    private void Apply(float t)
    {
        Color color = _restColor;
        color.a *= _fadeAlpha.Evaluate(t);
        _visual.color = color;
        _visual.transform.localScale = _restScale * _fadeScale.Evaluate(t);
    }
}
