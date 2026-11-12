using DG.Tweening;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// StarfieldBackground가 만든 별 중 몇 개를 골라 DOTween으로 반짝이게 한다.
/// StarfieldBackground와 같은 오브젝트에 붙이는 것을 권장하지만, Background 칸에 연결하거나 씬에서 자동으로 찾아서도 동작한다.
/// 별 생성이 끝난 뒤(IsBuilt)에만 반짝임을 시작하며, 별을 새로 만들거나 위치·기본 색을 바꾸지 않는다.
/// </summary>
[DisallowMultipleComponent]
public class StarTwinkler : MonoBehaviour
{
    /// <summary>진행 중인 반짝임 하나의 상태다.</summary>
    private class Twinkle
    {
        /// <summary>반짝이는 별이다.</summary>
        public StarData Star;
        /// <summary>반짝임 한 번(밝아졌다가 돌아오기)에 걸리는 시간(초)이다.</summary>
        public float Duration;
        /// <summary>가장 밝을 때의 투명도다.</summary>
        public float PeakAlpha;
        /// <summary>복구용 원래 점 스프라이트다.</summary>
        public Sprite OriginalSprite;
        /// <summary>복구용 원래 크기(1칸)다.</summary>
        public Vector2 OriginalSize;
        /// <summary>지금 십자 모양으로 보이는 중인지 나타낸다.</summary>
        public bool ShowingCross;
        /// <summary>투명도를 바꾸는 DOTween 트윈이다.</summary>
        public Tween Tween;
    }

    /// <summary>십자 스프라이트(3×3)가 점 스프라이트(1×1)보다 몇 배 큰지다.</summary>
    private const int CROSS_SIZE_IN_CELLS = 3;

    [Tooltip("반짝이게 할 별 배경. 비워 두면 같은 오브젝트 → 부모 → 씬 전체 순서로 자동으로 찾는다.")]
    public StarfieldBackground background;

    [Tooltip("반짝임 켜기/끄기")]
    public bool twinkleEnabled = true;

    [Header("동시에 반짝이는 별")]
    [Tooltip("동시에 반짝일 수 있는 별의 최대 개수 (이 수를 넘지 않음)")]
    [Min(0)] public int maxConcurrent = 5;

    [Header("반짝임 한 번에 걸리는 시간 (초, 매번 랜덤)")]
    [Min(0.1f)] public float minDuration = 1.5f;
    [Min(0.1f)] public float maxDuration = 4f;

    [Header("새 반짝임 시작 간격 (초, 매번 랜덤)")]
    [Min(0.05f)] public float minInterval = 0.4f;
    [Min(0.05f)] public float maxInterval = 1.2f;

    [Header("밝기")]
    [Tooltip("가장 밝을 때의 투명도. 1 = 완전히 밝게")]
    [Range(0f, 1f)] public float peakAlpha = 1f;

    [Tooltip("기본 밝기가 이 값 이하인 별을 우선해서 고른다 (변화 폭이 커 보이도록). 해당 별이 없으면 나머지에서 고른다.")]
    [Range(0f, 1f)] public float dimStarThreshold = 0.7f;

    [Header("쉬는 시간")]
    [Tooltip("반짝임이 끝난 별이 다시 뽑히기까지 기다리는 시간 (초)")]
    [Min(0f)] public float restTime = 2f;

    [Header("최고점 확장 (십자 모양)")]
    [Tooltip("가장 밝은 순간에 점 → 1칸 팔이 달린 십자(+) 모양으로 잠깐 바뀐다")]
    public bool crossAtPeak = true;

    [Tooltip("십자 스프라이트 (UI/Sprites/Common/star_cross.png). 팔 밝기(0.6)는 PNG에 들어 있다.")]
    public Sprite crossSprite;

    [Tooltip("밝기 곡선 값(0~1)이 이 이상일 때만 십자로 보인다. 1에 가까울수록 더 짧게 보인다.")]
    [Range(0.5f, 1f)] public float crossThreshold = 0.85f;

    [Header("디버그")]
    [Tooltip("콘솔에 동작 로그를 출력한다 ([StarTwinkler]로 검색)")]
    public bool debugLog = false;

    [Tooltip("켜면 반짝이는 중인 별이 빨간색으로 보인다. 끄면 원래 색으로 돌아간다.")]
    public bool debugColor = false;

    [Tooltip("요약 로그 간격 (초)")]
    [Min(1f)] public float summaryInterval = 5f;

    /// <summary>지금 반짝이는 별들이다.</summary>
    private readonly List<Twinkle> _active = new List<Twinkle>();
    /// <summary>별마다 쉬는 시간이 끝나는 시각(unscaled)이다.</summary>
    private readonly Dictionary<StarData, float> _restUntil = new Dictionary<StarData, float>();
    /// <summary>뽑기용 후보 중 어두운 별이다.</summary>
    private readonly List<StarData> _dimCandidates = new List<StarData>();
    /// <summary>뽑기용 후보 중 나머지 별이다.</summary>
    private readonly List<StarData> _otherCandidates = new List<StarData>();

    /// <summary>생성 완료 이벤트를 구독 중인 배경이다.</summary>
    private StarfieldBackground _subscribed;
    /// <summary>Start가 지났는지 나타낸다.</summary>
    private bool _started;
    /// <summary>다음 반짝임까지 남은 시간(초)이다.</summary>
    private float _spawnTimer;
    /// <summary>다음 요약 로그까지 남은 시간(초)이다.</summary>
    private float _summaryTimer;
    /// <summary>지금까지 시작한 반짝임 수다.</summary>
    private int _totalTwinkles;

    /// <summary>연결된 배경이 없으면 찾는다.</summary>
    private void Awake()
    {
        ResolveBackground();
    }

    /// <summary>배경의 생성 완료 이벤트를 구독하고 타이머를 초기화한다.</summary>
    private void OnEnable()
    {
        ResolveBackground();
        Subscribe();
        _spawnTimer = RandomInterval();
        _summaryTimer = summaryInterval;
    }

    /// <summary>시작 상태를 한 번 점검한다.</summary>
    private void Start()
    {
        _started = true;
        LogStartup();
    }

    /// <summary>꺼지거나 사라질 때 반짝이던 별을 기본 밝기·기본 모양으로 되돌린다.</summary>
    private void OnDisable()
    {
        StopAll();
        Unsubscribe();
    }

    /// <summary>간격마다 새 반짝임을 시작한다. 밝기 변화 자체는 DOTween이 처리한다.</summary>
    private void Update()
    {
        // 별 생성이 끝나기 전에는 아무것도 하지 않음 (실행 순서 문제 방지)
        if (background == null || !background.IsBuilt)
            return;

        if (!twinkleEnabled)
        {
            if (_active.Count > 0)
                StopAll();
            return;
        }

        // 일시정지(timeScale = 0) 중에도 계속 반짝이도록 unscaled 시간 사용 (SpriteBlink와 같음)
        float dt = Time.unscaledDeltaTime;

        _summaryTimer -= dt;
        if (_summaryTimer <= 0f)
        {
            _summaryTimer = summaryInterval;
            LogSummary();
        }

        _spawnTimer -= dt;
        if (_spawnTimer > 0f)
            return;

        // 최대 개수에 도달했으면 자리가 날 때까지 기다림 (절대 넘지 않음)
        if (_active.Count >= maxConcurrent)
            return;

        if (TryPickStar(out var star))
            StartTwinkle(star);
        _spawnTimer = RandomInterval();
    }

    #region Twinkle
    /// <summary>별 하나의 반짝임을 DOTween으로 시작한다. 기본 밝기 → 최고 밝기 → 기본 밝기로 정확히 돌아온다.</summary>
    /// <param name="star">반짝일 별이다.</param>
    private void StartTwinkle(StarData star)
    {
        var twinkle = new Twinkle
        {
            Star = star,
            Duration = Random.Range(Mathf.Min(minDuration, maxDuration), Mathf.Max(minDuration, maxDuration)),
            // 최고 밝기는 기본 밝기보다 낮아지지 않게, 1.0을 넘지 않게
            PeakAlpha = Mathf.Clamp(peakAlpha, star.BaseAlpha, 1f),
            OriginalSprite = star.Image.sprite,
            OriginalSize = star.Image.rectTransform.sizeDelta,
        };
        _active.Add(twinkle);
        _totalTwinkles++;
        ApplyDebugColor(twinkle);

        // 전반부에 밝아지고(InOutSine) Yoyo로 같은 곡선을 거꾸로 돌아와 기본 밝기로 끝난다.
        twinkle.Tween = star.Image.DOFade(twinkle.PeakAlpha, twinkle.Duration * 0.5f)
            .SetEase(Ease.InOutSine)
            .SetLoops(2, LoopType.Yoyo)
            .SetUpdate(true)      // timeScale 영향 없이 진행 (일시정지 중에도 반짝임)
            .SetLink(gameObject)  // 오브젝트가 파괴될 때 Tween도 자동 정리 (UI_Dialogue와 같은 안전장치)
            .OnUpdate(() => OnTwinkleUpdate(twinkle))
            .OnComplete(() => OnTwinkleComplete(twinkle));

        LogTwinkleStart(twinkle);
    }

    /// <summary>매 트윈 갱신마다 디버그 색과 최고점 십자 모양을 반영한다.</summary>
    private void OnTwinkleUpdate(Twinkle twinkle)
    {
        var image = twinkle.Star.Image;
        if (image == null)
        {
            // 별이 사라졌으면(다시 생성 등) 트윈만 정리
            twinkle.Tween?.Kill();
            _active.Remove(twinkle);
            return;
        }

        ApplyDebugColor(twinkle);

        // 현재 밝기를 0~1 곡선 값으로 바꿔 최고점 근처에서만 십자로 교체 (중앙 피벗이라 위치는 그대로)
        float range = twinkle.PeakAlpha - twinkle.Star.BaseAlpha;
        float shape = range > 0f ? (image.color.a - twinkle.Star.BaseAlpha) / range : 0f;
        bool wantCross = crossAtPeak && crossSprite != null && shape >= crossThreshold;
        if (wantCross != twinkle.ShowingCross)
            SetCross(twinkle, wantCross);
    }

    /// <summary>반짝임이 끝나면 기본 상태로 되돌리고 쉬는 목록에 넣는다.</summary>
    private void OnTwinkleComplete(Twinkle twinkle)
    {
        Restore(twinkle);
        _active.Remove(twinkle);
        _restUntil[twinkle.Star] = Time.unscaledTime + restTime;
        LogTwinkleEnd(twinkle);
    }

    /// <summary>디버그 색상이 켜져 있으면 빨간색, 아니면 기본 색으로 RGB만 바꾼다 (투명도는 트윈 값 유지).</summary>
    private void ApplyDebugColor(Twinkle twinkle)
    {
        var image = twinkle.Star.Image;
        Color color = debugColor ? Color.red : twinkle.Star.BaseColor;
        color.a = image.color.a;
        image.color = color;
    }

    /// <summary>점 ↔ 십자 스프라이트와 크기를 바꾼다.</summary>
    private void SetCross(Twinkle twinkle, bool showCross)
    {
        var image = twinkle.Star.Image;
        image.sprite = showCross ? crossSprite : twinkle.OriginalSprite;
        image.rectTransform.sizeDelta = showCross ? twinkle.OriginalSize * CROSS_SIZE_IN_CELLS : twinkle.OriginalSize;
        twinkle.ShowingCross = showCross;
    }

    /// <summary>별을 기본 밝기·기본 색·점 모양으로 되돌린다.</summary>
    private void Restore(Twinkle twinkle)
    {
        if (twinkle.Star.Image == null)
            return;
        if (twinkle.ShowingCross)
            SetCross(twinkle, false);
        twinkle.Star.ResetBrightness();
    }

    /// <summary>별이 기본 밝기·기본 색·기본 모양으로 돌아와 있는지 확인한다 (디버그용).</summary>
    private bool IsRestored(Twinkle twinkle)
    {
        var image = twinkle.Star.Image;
        if (image == null)
            return true; // 별이 이미 없어짐
        Color expected = twinkle.Star.BaseColor;
        expected.a = twinkle.Star.BaseAlpha;
        Color actual = image.color;
        const float eps = 0.001f;
        return image.sprite == twinkle.OriginalSprite
            && image.rectTransform.sizeDelta == twinkle.OriginalSize
            && Mathf.Abs(actual.r - expected.r) < eps
            && Mathf.Abs(actual.g - expected.g) < eps
            && Mathf.Abs(actual.b - expected.b) < eps
            && Mathf.Abs(actual.a - expected.a) < eps;
    }

    /// <summary>모든 반짝임을 멈추고 별을 기본 밝기·기본 모양으로 되돌린다.</summary>
    public void StopAll()
    {
        for (int i = 0; i < _active.Count; i++)
        {
            _active[i].Tween?.Kill(); // Kill은 OnComplete를 부르지 않는다
            Restore(_active[i]);
        }
        _active.Clear();
        _restUntil.Clear();
    }
    #endregion

    #region Background
    /// <summary>Background 칸이 비어 있으면 같은 오브젝트 → 부모 → 씬 전체 순서로 찾는다.</summary>
    private void ResolveBackground()
    {
        if (background != null)
            return;
        background = GetComponent<StarfieldBackground>();
        if (background == null)
            background = GetComponentInParent<StarfieldBackground>();
        if (background == null)
            background = FindAnyObjectByType<StarfieldBackground>();
    }

    /// <summary>배경의 생성 완료 이벤트를 구독한다.</summary>
    private void Subscribe()
    {
        if (_subscribed == background)
            return;
        Unsubscribe();
        if (background != null)
        {
            background.Built += OnBackgroundBuilt;
            _subscribed = background;
        }
    }

    /// <summary>배경의 생성 완료 이벤트 구독을 해제한다.</summary>
    private void Unsubscribe()
    {
        if (_subscribed != null)
            _subscribed.Built -= OnBackgroundBuilt;
        _subscribed = null;
    }

    /// <summary>별이 (다시) 생성되면 이전 반짝임 기록을 비우고 새 별 목록으로 시작한다.</summary>
    private void OnBackgroundBuilt(StarfieldBackground bg)
    {
        StopAll();
        // Start 전이면 Start에서 한 번만 로그를 찍는다
        if (_started)
            LogStartup();
    }
    #endregion

    #region Pick
    /// <summary>반짝이는 중도, 쉬는 중도 아닌 별 하나를 무작위로 고른다. 기본 밝기가 낮은 별을 우선한다.</summary>
    private bool TryPickStar(out StarData picked)
    {
        picked = null;
        var stars = background.Stars;
        float now = Time.unscaledTime;
        _dimCandidates.Clear();
        _otherCandidates.Clear();

        for (int i = 0; i < stars.Count; i++)
        {
            var star = stars[i];
            if (star == null || star.Image == null)
                continue;
            if (IsActive(star))
                continue;
            if (_restUntil.TryGetValue(star, out float until) && now < until)
                continue;
            if (star.BaseAlpha <= dimStarThreshold)
                _dimCandidates.Add(star);
            else
                _otherCandidates.Add(star);
        }

        var pool = _dimCandidates.Count > 0 ? _dimCandidates : _otherCandidates;
        if (pool.Count == 0)
            return false;
        picked = pool[Random.Range(0, pool.Count)];
        return true;
    }

    /// <summary>별이 지금 반짝이는 중인지 확인한다.</summary>
    private bool IsActive(StarData star)
    {
        for (int i = 0; i < _active.Count; i++)
        {
            if (_active[i].Star == star)
                return true;
        }
        return false;
    }

    /// <summary>다음 반짝임까지의 간격을 무작위로 정한다.</summary>
    private float RandomInterval()
    {
        return Random.Range(Mathf.Min(minInterval, maxInterval), Mathf.Max(minInterval, maxInterval));
    }
    #endregion

    #region Log
    /// <summary>배경 연결과 별 생성 상태를 점검하고, 디버그 로그가 켜져 있으면 설정을 요약한다.</summary>
    private void LogStartup()
    {
        if (background == null)
        {
            this.LogWarning("StarfieldBackground를 찾지 못했습니다. StarfieldBackground와 같은 오브젝트에 붙이거나, Background 칸에 연결하세요.");
            return;
        }
        if (!background.IsBuilt)
        {
            this.LogWarning($"별이 아직 생성되지 않았습니다. '{background.name}' 오브젝트가 꺼져 있지 않은지 확인하세요. 생성되면 자동으로 시작합니다.");
            return;
        }

        var stars = background.Stars;
        if (stars.Count == 0)
        {
            this.LogWarning($"별을 하나도 찾지 못했습니다. '{background.name}'의 Stars → Star Count가 0이 아닌지 확인하세요.");
            return;
        }
        if (crossAtPeak && crossSprite == null)
            this.LogWarning("Cross At Peak가 켜져 있지만 Cross Sprite가 비어 있어 십자 모양은 생략합니다. star_cross.png를 연결하세요.");

        if (!debugLog)
            return;

        int dimCount = 0;
        for (int i = 0; i < stars.Count; i++)
        {
            if (stars[i].BaseAlpha <= dimStarThreshold)
                dimCount++;
        }

        this.Log($"반짝임 준비 완료: 별 {stars.Count}개 (기본 밝기 {dimStarThreshold:0.00} 이하 우선 대상 {dimCount}개) | " +
                 $"켜짐 {twinkleEnabled}, 최대 동시 {maxConcurrent}, 시간 {minDuration:0.0}~{maxDuration:0.0}초, " +
                 $"간격 {minInterval:0.0}~{maxInterval:0.0}초, 최고 밝기 {peakAlpha:0.00}, 쉬는 시간 {restTime:0.0}초, " +
                 $"십자 {(crossAtPeak ? "켬" : "끔")}, 디버그 색상 {(debugColor ? "켬" : "끔")}");
    }

    /// <summary>반짝임 시작을 기록한다.</summary>
    private void LogTwinkleStart(Twinkle twinkle)
    {
        if (!debugLog)
            return;
        this.Log($"반짝임 시작: 별 #{twinkle.Star.Index:000}, {twinkle.Duration:0.00}초, " +
                 $"밝기 {twinkle.Star.BaseAlpha:0.00} → {twinkle.PeakAlpha:0.00}, 동시 {_active.Count}/{maxConcurrent}");
    }

    /// <summary>반짝임 끝과 기본 상태 복귀 여부를 기록한다. 복귀 실패는 로그 설정과 상관없이 알린다.</summary>
    private void LogTwinkleEnd(Twinkle twinkle)
    {
        if (!IsRestored(twinkle))
        {
            this.LogWarning($"반짝임 끝: 별 #{twinkle.Star.Index:000}, 기본 밝기 복귀 실패");
            return;
        }
        if (!debugLog)
            return;
        this.Log($"반짝임 끝: 별 #{twinkle.Star.Index:000}, 기본 밝기 {twinkle.Star.BaseAlpha:0.00}로 복귀 OK");
    }

    /// <summary>요약을 기록한다. 최대 개수 초과는 로그 설정과 상관없이 에러로 알린다.</summary>
    private void LogSummary()
    {
        if (_active.Count > maxConcurrent)
        {
            this.LogError($"요약: 동시 반짝임 {_active.Count}개가 최대 {maxConcurrent}개를 넘었습니다!");
            return;
        }
        if (!debugLog)
            return;
        this.Log($"요약: 지금까지 {_totalTwinkles}회 반짝임, 현재 동시 {_active.Count}/{maxConcurrent}");
    }
    #endregion

    /// <summary>인스펙터에서 최소값이 최대값보다 커지지 않도록 정리한다.</summary>
    private void OnValidate()
    {
        if (maxDuration < minDuration)
            maxDuration = minDuration;
        if (maxInterval < minInterval)
            maxInterval = minInterval;
    }
}
