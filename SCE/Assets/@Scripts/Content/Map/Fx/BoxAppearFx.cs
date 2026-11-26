using System;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 박스(홀로그램 등)의 등장 · 소멸 움직임만 맡는다. "언제" 등장 · 소멸할지는 부르는 쪽(버튼 로직, 적 도달 이벤트 등)이 정한다.
/// 등장: 아래 변을 고정한 채 위로 쭉 늘어났다가(기본 1.2배) 원래 크기에 안착 — 아래 칸을 침범하지 않는다.
/// 소멸: 아래 변을 고정한 채 아래로 줄어들며 흐려진다.
/// 곡선 값은 "정규화 시간(0~1) → 원래 크기 대비 배율"이다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BoxAppearFx : MonoBehaviour
{
    [Header("대상")]
    [SerializeField, Tooltip("움직일 그림. 비우면 이 오브젝트. 물리 바디가 있는 박스는 자식 Visual을 지정하는 것을 권장")]
    private SpriteRenderer _visual;

    [SerializeField, Tooltip("시작할 때 숨긴 상태로 둘지 (등장 연출로 처음 보이는 박스)")]
    private bool _hiddenOnStart = true;

    [Header("등장")]
    [SerializeField, Min(0.01f), Tooltip("등장 시간(초)")]
    private float _appearDuration = 0.3f;

    [SerializeField, Tooltip("등장 중 세로 배율. 최댓값이 늘어나는 정도(기본 1.2)이며 끝값은 1이어야 원래 크기에 안착한다")]
    private AnimationCurve _appearHeight = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 3.5f), new Keyframe(0.6f, 1.2f, 0f, 0f), new Keyframe(1f, 1f, 0f, 0f));

    [SerializeField, Tooltip("등장 중 가로 배율. 1을 넘기면 옆 칸을 침범한다")]
    private AnimationCurve _appearWidth = new AnimationCurve(
        new Keyframe(0f, 0.7f), new Keyframe(0.6f, 0.92f), new Keyframe(1f, 1f));

    [SerializeField, Tooltip("등장을 시작할 때 재생할 소리 · 이펙트 (박스 아래 변 위치)")]
    private GimmickFxSlot _appearSlot = new GimmickFxSlot { Volume = 1f };

    [SerializeField, Tooltip("원래 크기에 안착했을 때 재생할 소리 · 이펙트 (박스 중심 위치)")]
    private GimmickFxSlot _landSlot = new GimmickFxSlot { Volume = 1f };

    [Header("소멸")]
    [SerializeField, Min(0.01f), Tooltip("소멸 시간(초)")]
    private float _vanishDuration = 0.25f;

    [SerializeField, Tooltip("소멸 중 세로 배율 (1 → 0)")]
    private AnimationCurve _vanishHeight = new AnimationCurve(
        new Keyframe(0f, 1f), new Keyframe(0.25f, 1.08f), new Keyframe(1f, 0f));

    [SerializeField, Tooltip("소멸 중 가로 배율")]
    private AnimationCurve _vanishWidth = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.8f));

    [SerializeField, Tooltip("소멸 중 불투명도 (1 → 0)")]
    private AnimationCurve _vanishAlpha = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));

    [SerializeField, Tooltip("사라질 때 재생할 소리 · 이펙트")]
    private GimmickFxSlot _vanishSlot = new GimmickFxSlot { Volume = 1f };

    [Header("소멸 지연 (부르는 쪽이 원하면 사용)")]
    [SerializeField, Min(0f), Tooltip("RandomVanishDelay의 최솟값(초)")]
    private float _vanishDelayMin = 3f;

    [SerializeField, Min(0f), Tooltip("RandomVanishDelay의 최댓값(초)")]
    private float _vanishDelayMax = 5f;

    /// <summary>움직임의 기준이 되는 원래 로컬 위치다.</summary>
    private Vector3 _restPosition;
    /// <summary>움직임의 기준이 되는 원래 로컬 크기다.</summary>
    private Vector3 _restScale;
    /// <summary>원래 색이며 소멸 때 알파만 바꾼 뒤 되돌린다.</summary>
    private Color _restColor;
    /// <summary>기준 값을 잡아 두었는지 나타낸다.</summary>
    private bool _hasRest;
    /// <summary>진행 중인 등장 · 소멸 트윈이다.</summary>
    private Tween _tween;
    /// <summary>소멸을 기다리는 지연 트윈이다.</summary>
    private Tween _delay;

    /// <summary>등장 또는 소멸이 진행 중인지 나타낸다. 진행 중에는 밀기 같은 입력을 막는 데 쓴다.</summary>
    public bool IsPlaying => _tween != null && _tween.IsActive();
    /// <summary>소멸 지연을 기다리는 중인지 나타낸다.</summary>
    public bool IsVanishPending => _delay != null && _delay.IsActive();
    /// <summary>지금 보이는지 나타낸다.</summary>
    public bool IsVisible => _visual != null && _visual.enabled;
    /// <summary>Inspector의 최솟값~최댓값 사이에서 고른 소멸 지연(초)이다.</summary>
    public float RandomVanishDelay => UnityEngine.Random.Range(_vanishDelayMin, Mathf.Max(_vanishDelayMin, _vanishDelayMax));

    /// <summary>그림을 찾고 처음 상태를 정한다.</summary>
    private void Awake()
    {
        Init();
        if (_hiddenOnStart)
            ResetVisual(false);
    }

    /// <summary>그림과 기준 모양을 한 번 잡는다. 다른 컴포넌트가 이 Awake보다 먼저 부를 수 있어 지연 초기화한다.</summary>
    private void Init()
    {
        if (_visual == null)
            _visual = GetComponent<SpriteRenderer>();
        if (!_hasRest)
            CaptureRest();
    }

    /// <summary>꺼질 때 남은 트윈을 지우고 원래 모양으로 돌린다. 보임 여부는 그대로 둔다.</summary>
    private void OnDisable()
    {
        if (_visual != null)
            ResetVisual(_visual.enabled);
    }

    /// <summary>현재 위치 · 크기 · 색을 움직임의 기준으로 잡는다. 박스가 다른 칸으로 옮겨진 뒤에는 다시 부른다.</summary>
    public void CaptureRest()
    {
        if (_visual == null)
            return;
        Transform target = _visual.transform;
        _restPosition = target.localPosition;
        _restScale = target.localScale;
        _restColor = _visual.color;
        _hasRest = true;
    }

    /// <summary>진행 중인 연출과 지연을 모두 멈추고 원래 모양으로 돌린다.</summary>
    /// <param name="visible">돌린 뒤 보이게 할지 여부다.</param>
    public void ResetVisual(bool visible)
    {
        Init();
        KillAll();
        if (_visual == null || !_hasRest)
            return;
        Apply(1f, 1f, 1f);
        _visual.enabled = visible;
    }

    /// <summary>등장 연출을 재생한다. 끝나면 원래 크기로 보이는 상태다.</summary>
    /// <param name="onComplete">연출이 끝난 뒤 한 번 불린다.</param>
    public void PlayAppear(Action onComplete = null)
    {
        Init();
        if (_visual == null)
            return;
        KillAll();
        Apply(_appearWidth.Evaluate(0f), _appearHeight.Evaluate(0f), 1f);
        _visual.enabled = true;
        _appearSlot.Play(_visual.transform.position);
        _tween = DOTween.To(() => 0f, t => Apply(_appearWidth.Evaluate(t), _appearHeight.Evaluate(t), 1f), 1f, _appearDuration)
            .SetEase(Ease.Linear)
            .SetLink(gameObject)
            .OnComplete(() =>
            {
                Apply(1f, 1f, 1f);
                _tween = null;
                _landSlot.Play(_visual.transform.position);
                onComplete?.Invoke();
            });
    }

    /// <summary>소멸 연출을 재생한다. 끝나면 숨겨지고 모양은 원래대로 돌아간다(다음 등장 준비).</summary>
    /// <param name="onComplete">연출이 끝난 뒤 한 번 불린다. 실제 제거 · 풀 반환 · 맵 해제는 여기서 한다.</param>
    public void PlayVanish(Action onComplete = null)
    {
        Init();
        if (_visual == null || !_visual.enabled)
        {
            onComplete?.Invoke();
            return;
        }
        KillAll();
        _vanishSlot.Play(_visual.transform.position);
        _tween = DOTween.To(() => 0f, t => Apply(_vanishWidth.Evaluate(t), _vanishHeight.Evaluate(t), _vanishAlpha.Evaluate(t)), 1f, _vanishDuration)
            .SetEase(Ease.Linear)
            .SetLink(gameObject)
            .OnComplete(() =>
            {
                _tween = null;
                Apply(1f, 1f, 1f);
                _visual.enabled = false;
                onComplete?.Invoke();
            });
    }

    /// <summary>지정한 시간 뒤에 소멸 연출을 재생한다. 이미 기다리던 소멸은 새 시간으로 바뀐다.</summary>
    /// <param name="delay">기다릴 시간(초)이다. RandomVanishDelay를 넘기면 Inspector 범위에서 고른다.</param>
    /// <param name="onComplete">소멸 연출이 끝난 뒤 한 번 불린다.</param>
    public void PlayVanishAfter(float delay, Action onComplete = null)
    {
        CancelPendingVanish();
        _delay = DOVirtual.DelayedCall(delay, () =>
        {
            _delay = null;
            PlayVanish(onComplete);
        }).SetLink(gameObject);
    }

    /// <summary>기다리던 소멸을 취소한다 (적이 박스를 떠났을 때 등).</summary>
    public void CancelPendingVanish()
    {
        _delay?.Kill();
        _delay = null;
    }

    /// <summary>진행 중인 연출과 지연을 모두 멈춘다.</summary>
    private void KillAll()
    {
        _tween?.Kill();
        _tween = null;
        CancelPendingVanish();
    }

    /// <summary>원래 모양 대비 가로 · 세로 배율과 불투명도를 적용한다. 그림의 아래 변이 원래 자리에 남도록 위치를 보정한다.</summary>
    /// <param name="width">원래 가로 크기 대비 배율이다.</param>
    /// <param name="height">원래 세로 크기 대비 배율이다.</param>
    /// <param name="alpha">원래 불투명도 대비 배율이다.</param>
    private void Apply(float width, float height, float alpha)
    {
        Transform target = _visual.transform;
        target.localScale = new Vector3(_restScale.x * width, _restScale.y * height, _restScale.z);

        // bottom은 그림 중심(피벗) 기준 아래 변의 로컬 높이이며 피벗이 가운데든 아래든 같은 식으로 고정된다.
        float bottom = _visual.sprite != null ? _visual.sprite.bounds.min.y : 0f;
        target.localPosition = _restPosition + new Vector3(0f, bottom * _restScale.y * (1f - height), 0f);

        Color color = _restColor;
        color.a *= alpha;
        _visual.color = color;
    }
}
