using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class CoroutineFade : MonoBehaviour
{
    public static CoroutineFade Instance { get; private set; }

    [Header("기본 설정")]
    public Volume globalVolume;
    public float defaultDuration = 1.5f; // 기본 페이드 시간
    public float SaturationFadeDuration = 2f;  // 컬러로 돌아오는 데 걸리는 시간

    private ColorAdjustments colorAdjustments;

    // 밝기 조절용 코루틴
    private Coroutine currentFadeRoutine;
    // 채도 조절용 코루틴
    private Coroutine currentSaturationRoutine;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        if (globalVolume.profile.TryGet(out ColorAdjustments adj))
        {
            colorAdjustments = adj;
        }
        else
        {
            Debug.LogError("Global Volume에 'Color Adjustments'가 추가되어 있는지 확인해주세요!");
        }
    }

    public void SetGrayscaleImmediate()
    {
        if (colorAdjustments != null)
        {
            if (currentSaturationRoutine != null)
            {
                StopCoroutine(currentSaturationRoutine);
                currentSaturationRoutine = null;
            }

            // 채도를 즉시 -100으로 고정 (흑백)
            colorAdjustments.saturation.value = -100f;
        }
    }

    public Coroutine TriggerColorRecovery()
    {
        if (colorAdjustments != null)
        {
            // 실행 중인 루틴 정리
            if (currentSaturationRoutine != null) StopCoroutine(currentSaturationRoutine);

            // 목표값 0 (원래 색)으로 서서히 이동
            currentSaturationRoutine = StartCoroutine(SaturationRoutine(0f));
        }
        return currentSaturationRoutine;
    }

    public void RefreshVolume()
    {
        globalVolume = FindFirstObjectByType<Volume>();

        if (globalVolume != null && globalVolume.profile.TryGet(out ColorAdjustments adj))
        {
            colorAdjustments = adj;
        }
        else
        {
            Debug.LogWarning("[ScreenFader] Global Volume 또는 Color Adjustments를 찾을 수 없습니다.");
        }
    }

    public static void FadeOut(Action onComplete = null, float duration = -1, float erndDelaySec = 0.1f)
    {
        // target -20 (어두움)
        Instance?.StartFadeProcess(-20f, duration, onComplete, null, erndDelaySec);
    }

    public static void FadeIn(Action onComplete = null, float duration = -1, float endDelaySec = 0.1f)
    {
        // target: 0 (밝음), 시작값 설정: -20
        Instance?.StartFadeProcess(0f, duration, onComplete, -20f, endDelaySec);
    }

    private void StartFadeProcess(float targetExposure, float duration, Action onComplete, float? forceStartValue, float endDelaySec = 0.1f)
    {
        StopAllCoroutines();
        StartCoroutine(CoProcessFade(targetExposure, duration, endDelaySec, onComplete, forceStartValue));
    }

    private IEnumerator CoProcessFade(float targetExposure, float duration, float endDelaySec, Action onComplete, float? forceStartValue)
    {
        // 1. 씬이 바뀌었을 수 있으므로 Volume 다시 연결
        RefreshVolume();

        // Fade In인 경우, 밝은 화면을 어둡게 덮어씌움
        if (forceStartValue.HasValue && colorAdjustments != null)
        {
            colorAdjustments.postExposure.value = forceStartValue.Value;
        }

        // 2. 시간 설정
        float targetDuration = (duration < 0) ? defaultDuration : duration;

        // 3. 페이드 애니메이션 실행
        yield return StartCoroutine(ExposureRoutine(targetExposure, targetDuration));

        if (endDelaySec > 0) yield return new WaitForSeconds(endDelaySec);

        onComplete?.Invoke();
    }

    private IEnumerator ExposureRoutine(float targetValue, float duration)
    {
        if (colorAdjustments == null) yield break;

        float startValue = colorAdjustments.postExposure.value;
        float currentTime = 0f;

        // 즉시 변경해야 하는 경우
        if (duration <= 0)
        {
            colorAdjustments.postExposure.value = targetValue;
            yield break;
        }

        while (currentTime < duration)
        {
            currentTime += Time.deltaTime;

            float t = currentTime / duration;

            colorAdjustments.postExposure.value = Mathf.Lerp(startValue, targetValue, t);
            yield return null;
        }

        // 오차 보정
        colorAdjustments.postExposure.value = targetValue;
    }


    // 채도 조절
    private IEnumerator SaturationRoutine(float targetValue)
    {
        float currentTime = 0f;
        float startValue = colorAdjustments.saturation.value;

        while (currentTime < SaturationFadeDuration)
        {
            currentTime += Time.deltaTime;
            float t = currentTime / SaturationFadeDuration;

            // 부드럽게 값 변경
            colorAdjustments.saturation.value = Mathf.Lerp(startValue, targetValue, t);
            yield return null;
        }

        colorAdjustments.saturation.value = targetValue;
        currentSaturationRoutine = null;
    }


    // 화면을 즉시 검은색으로 전환하는 함수
    public void SetBlackImmediate()
    {
        // 1. 실행 중인 페이드 루틴이 있다면 정지 (값이 충돌하지 않게)
        if (currentFadeRoutine != null)
        {
            StopCoroutine(currentFadeRoutine);
            currentFadeRoutine = null;
        }

        // 2. 밝기 값을 즉시 최저치로 설정
        if (colorAdjustments != null)
        {
            colorAdjustments.postExposure.value = -20f;
        }
    }

    // 화면을 즉시 원래 밝기로 복구하는 함수
    public void SetNormalImmediate()
    {
        // 1. 실행 중인 페이드 루틴이 있다면 정지
        if (currentFadeRoutine != null)
        {
            StopCoroutine(currentFadeRoutine);
            currentFadeRoutine = null;
        }

        // 2. 밝기 값을 즉시 기본값(0)으로 설정
        if (colorAdjustments != null)
        {
            colorAdjustments.postExposure.value = 0f;
        }
    }
}