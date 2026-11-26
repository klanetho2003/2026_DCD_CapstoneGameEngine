using System;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 부서지는 발판(GridCrumbleTile)의 연출. 같은 오브젝트에 붙이면 GridCrumbleTile이 알아서 부른다.
/// 밟을 때: 새 금 그림이 좌우로 살짝 흔들리며 얹힌다 (픽셀 단위로 움직여 픽셀아트가 번지지 않는다).
/// 부서질 때: 잠시 보여 준 뒤 금 그림과 (지정했다면) 바닥 그림이 함께 흐려진다. 끝나면 GridCrumbleTile이 바닥 타일을 지운다.
/// </summary>
[DisallowMultipleComponent]
public sealed class CrackOverlayFx : MonoBehaviour
{
    [Header("대상")]
    [SerializeField, Tooltip("흔들고 흐려질 금 그림. 비우면 이 오브젝트")]
    private SpriteRenderer _visual;

    [SerializeField, Tooltip("함께 흐려질 바닥 그림 (선택). 실제 맵의 Tilemap 바닥은 연출 뒤 GridCrumbleTile이 지운다")]
    private SpriteRenderer _ground;

    [Header("새 금이 얹힐 때 (밟을 때)")]
    [SerializeField, Min(0.01f), Tooltip("흔들리는 시간(초)")]
    private float _shakeDuration = 0.2f;

    [SerializeField, Min(0f), Tooltip("좌우로 움직이는 폭(픽셀)")]
    private float _shakePixels = 1f;

    [SerializeField, Min(1), Tooltip("좌우 왕복 횟수")]
    private int _shakeCount = 2;

    [SerializeField, Min(1f), Tooltip("움직임을 맞출 픽셀 단위 (스프라이트 Pixels Per Unit)")]
    private float _pixelsPerUnit = 32f;

    [SerializeField, Tooltip("금이 얹힐 때 재생할 소리 · 이펙트")]
    private GimmickFxSlot _stepSlot = new GimmickFxSlot { Volume = 1f };

    [Header("부서질 때 (3단계 뒤 소멸)")]
    [SerializeField, Min(0f), Tooltip("마지막 금을 보여 준 뒤 사라지기 시작할 때까지(초)")]
    private float _breakHold = 0.3f;

    [SerializeField, Min(0.01f), Tooltip("사라지는 시간(초)")]
    private float _breakFadeDuration = 0.2f;

    [SerializeField, Tooltip("사라지는 중 불투명도 (1 → 0)")]
    private AnimationCurve _breakAlpha = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));

    [SerializeField, Tooltip("사라지기 시작할 때 재생할 소리 · 이펙트")]
    private GimmickFxSlot _breakSlot = new GimmickFxSlot { Volume = 1f };

    /// <summary>흔들기 전 원래 로컬 위치다.</summary>
    private Vector3 _restPosition;
    /// <summary>금 그림의 원래 색이다.</summary>
    private Color _visualColor;
    /// <summary>바닥 그림의 원래 색이다.</summary>
    private Color _groundColor;
    /// <summary>진행 중인 흔들림이다.</summary>
    private Tween _shake;
    /// <summary>진행 중인 소멸이다.</summary>
    private Tween _break;
    /// <summary>원래 모양을 잡았는지 나타낸다. 같은 오브젝트의 GridCrumbleTile이 이 Awake보다 먼저 부를 수 있어 지연 초기화한다.</summary>
    private bool _initialized;

    /// <summary>부서지는 연출이 진행 중인지 나타낸다. GridCrumbleTile이 끝을 기다리는 데 쓴다.</summary>
    public bool IsBreaking => _break != null && _break.IsActive();

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
            _restPosition = _visual.transform.localPosition;
            _visualColor = _visual.color;
        }
        if (_ground != null)
            _groundColor = _ground.color;
    }

    /// <summary>꺼질 때 남은 트윈을 지우고 원래 모양으로 돌린다.</summary>
    private void OnDisable()
    {
        ResetVisual();
    }

    /// <summary>새 금이 얹히는 흔들림을 재생한다.</summary>
    public void PlayStep()
    {
        Init();
        if (_visual == null)
            return;
        StopShake();
        _restPosition = _visual.transform.localPosition;
        _stepSlot.Play(_visual.transform.position);
        _shake = DOTween.To(() => 0f, ApplyShake, 1f, _shakeDuration)
            .SetEase(Ease.Linear)
            .SetLink(gameObject)
            .OnComplete(StopShake);
    }

    /// <summary>잠시 뒤 금 그림과 바닥 그림을 흐리게 하며 없앤다.</summary>
    /// <param name="onComplete">다 사라진 뒤 한 번 불린다.</param>
    public void PlayBreak(Action onComplete = null)
    {
        Init();
        if (_visual == null)
        {
            onComplete?.Invoke();
            return;
        }
        _break?.Kill();
        _break = DOTween.Sequence()
            .AppendInterval(_breakHold)
            .AppendCallback(() => _breakSlot.Play(_visual.transform.position))
            .Append(DOTween.To(() => 0f, ApplyFade, 1f, _breakFadeDuration).SetEase(Ease.Linear))
            .SetLink(gameObject)
            .OnComplete(() =>
            {
                _break = null;
                onComplete?.Invoke();
            });
    }

    /// <summary>모든 연출을 멈추고 원래 위치 · 색으로 돌린다 (라운드 초기화, 풀 반환).</summary>
    public void ResetVisual()
    {
        Init();
        StopShake();
        _break?.Kill();
        _break = null;
        if (_visual != null)
            _visual.color = _visualColor;
        if (_ground != null)
        {
            _ground.color = _groundColor;
            _ground.enabled = true;
        }
    }

    /// <summary>흔들림을 멈추고 원래 위치로 돌린다.</summary>
    private void StopShake()
    {
        if (_shake != null && _shake.IsActive())
            _shake.Kill();
        _shake = null;
        if (_visual != null)
            _visual.transform.localPosition = _restPosition;
    }

    /// <summary>정규화 시간의 좌우 흔들림을 픽셀 단위로 맞춰 적용한다. 갈수록 폭이 줄어든다.</summary>
    private void ApplyShake(float t)
    {
        float pixels = Mathf.Sin(t * _shakeCount * Mathf.PI * 2f) * _shakePixels * (1f - t);
        float snapped = Mathf.Round(pixels) / _pixelsPerUnit;
        _visual.transform.localPosition = _restPosition + new Vector3(snapped, 0f, 0f);
    }

    /// <summary>정규화 시간의 불투명도를 금 그림과 바닥 그림에 적용한다.</summary>
    private void ApplyFade(float t)
    {
        float alpha = _breakAlpha.Evaluate(t);
        Color visual = _visualColor;
        visual.a *= alpha;
        _visual.color = visual;
        if (_ground != null)
        {
            Color ground = _groundColor;
            ground.a *= alpha;
            _ground.color = ground;
        }
    }
}
