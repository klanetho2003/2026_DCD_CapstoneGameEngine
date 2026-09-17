using Mono.Cecil;
using System;
using UnityEngine;
using static Define;

/// <summary>
/// 실시간 변동 Stat. Max는 외부 StatDefineValue 참조, Current만 자체 소유.
/// 
/// - CurrentInputHandler 값 보관 및 [0, Max] 범위 클램프
/// - Max 변경 자동 (StatDefineValue.OnValueChanged 구독)
/// </summary>
public class StatResourceValue
{
    public EStatResourceType StatType { get; private set; }
    public float CurrentValue { get; private set; }
    public float Max => _maxStat.Value;
    public float Ratio => (Max <= 0f) ? 0f : CurrentValue / Max;

    private readonly StatDefineValue _maxStat;

    /// <summary>CurrentInputHandler 또는 Max 변경 시. (type, current, max)</summary>
    public event Action<EStatResourceType, float, float, DamageInfo> OnChanged;
    /// <summary>0에 도달한 순간. (사망/탈진 트리거)</summary>
    public event Action<EStatResourceType> OnDepleted;

    public StatResourceValue(EStatResourceType type, StatDefineValue maxStat, float initialCurrent = -1f)
    {
        StatType = type;
        _maxStat = maxStat;

        // 초기값: 음수면 풀필
        CurrentValue = (initialCurrent < 0f)
            ? maxStat.Value
            : Mathf.Clamp(initialCurrent, 0f, maxStat.Value);

        // Max 변경 시 Clamp 처리 — 단방향 구독
        _maxStat.OnValueChanged += OnMaxChanged;
    }

    /// <summary>구독 해제. CreatureBase 파괴 시 호출.</summary>
    public void Dispose()
    {
        if (_maxStat != null)
            _maxStat.OnValueChanged -= OnMaxChanged;
    }

    public void SetCurrent(float value, DamageInfo info = default)
    {
        float max = _maxStat.Value;
        float clamped = Mathf.Clamp(value, 0f, max);
        if (MathUtil.IsEqualValue(CurrentValue, clamped)) return;

        CurrentValue = clamped;
        OnChanged?.Invoke(StatType, CurrentValue, max, info);

        if (clamped <= 0f)
            OnDepleted?.Invoke(StatType);
    }

    /// <summary>MaxHp 변경 콜백 — 시나리오 4단계의 핵심.</summary>
    private void OnMaxChanged(EStatType _, float newMax)
    {
        if (CurrentValue > newMax)
        {
            // Max가 줄어서 Current를 깎아야 하는 경우 (버프 종료 등)
            SetCurrent(newMax);
        }
        else
        {
            // Current는 그대로지만 Max가 바뀜 → UI 갱신 필요 (체력바 비율 변동)
            OnChanged?.Invoke(StatType, CurrentValue, newMax, default);
        }
    }
}