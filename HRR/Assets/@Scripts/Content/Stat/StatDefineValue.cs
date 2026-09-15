using System;
using System.Collections.Generic;
using static Define;

#region Compare Struct
/// <summary>
/// EStatType 키 Dictionary용 EqualityComparer.
/// 
/// 기본 EnumEqualityComparer는 enum을 object로 boxing
/// 본 Comparer는 (int)캐스팅으로 비용 저하
/// 
/// singleton 인스턴스로 사용 — Dictionary 생성 시 alloc 회피.
/// </summary>
public sealed class StatTypeComparer : IEqualityComparer<EStatType>
{
    public static readonly StatTypeComparer Instance = new();

    private StatTypeComparer() { }

    public bool Equals(EStatType x, EStatType y) => x == y;
    public int GetHashCode(EStatType obj) => (int)obj;
}
#endregion

/// <summary>
/// 단일 stat의 값 객체. BaseValue + modifier 리스트 + 캐시
/// 
/// 책임:
/// - modifier 추가/제거
/// - 최종 값 계산 (lazy + cached)
/// - dirty flag로 불필요한 재계산 방지
/// </summary>
public class StatDefineValue
{
    public EStatType StatType { get; private set; }
    public float BaseValue { get; private set; }

    private readonly List<StatModifier> _modifiers = new();
    public IReadOnlyList<StatModifier> Modifiers => _modifiers;

    private bool _isDirty = true;
    private float _cachedValue;

    /// <summary>
    /// 값이 변경되었을 때 Invoke (BaseValue 변경, Modifier 추가/제거)
    /// 
    /// 구독 방법: player.GetStatValue(EStatType.MaxHp).OnValueChanged += UpdateHpBar;
    /// </summary>
    public event Action<EStatType, float> OnValueChanged;

    /// <summary>
    /// 최종 값. dirty flag 확인 후 필요 시만 재계산
    /// 빈번한 호출이 예상되는 핫패스이므로 캐싱이 중요
    /// </summary>
    public float Value
    {
        get
        {
            if (_isDirty)
            {
                _cachedValue = CalculateFinalValue();
                _isDirty = false;
            }
            return _cachedValue;
        }
    }

    public StatDefineValue(EStatType type, float baseValue)
    {
        StatType = type;
        BaseValue = baseValue;
    }

    /// <summary>기준 값을 바꾸는 함수입니다. 버프 적용은 Modifier 함수를 사용하세요</summary>
    public void SetBaseValue(float value)
    {
        if (MathUtil.IsEqualValue(BaseValue, value)) return;
        BaseValue = value;
        _isDirty = true;
        NotifyChanged();
    }

    public void AddModifier(StatModifier modifier)
    {
        _modifiers.Add(modifier);
        _modifiers.Sort((a, b) => a.Order.CompareTo(b.Order));
        _isDirty = true;
        NotifyChanged();
    }

    public bool RemoveModifier(StatModifier modifier)
    {
        if (_modifiers.Remove(modifier))
        {
            _isDirty = true;
            NotifyChanged();
            return true;
        }
        return false;
    }

    /// <summary>
    /// 특정 출처에서 발생한 모든 modifier 제거
    /// 장비 해제, 버프 종료 등에 사용. 한 번의 호출로 그룹 일괄 정리
    /// </summary>
    public int RemoveModifiersFromSource(object source)
    {
        if (source == null) return 0;

        int removed = _modifiers.RemoveAll(m => ReferenceEquals(m.Source, source));
        if (removed > 0)
        {
            _isDirty = true;
            NotifyChanged();
        }
        return removed;
    }

    public void Clear()
    {
        if (_modifiers.Count == 0) return;
        _modifiers.Clear();
        _isDirty = true;
        NotifyChanged();
    }

    /// <summary>
    /// 변경 알림
    /// </summary>
    private void NotifyChanged()
    {
        if (OnValueChanged == null) return;
        OnValueChanged?.Invoke(StatType, Value);
    }

    /// <summary>
    /// 최종 값 계산.
    /// Add >> PercentAdd(그룹 합산) >> PercentMult(독립 곱).
    /// </summary>
    private float CalculateFinalValue()
    {
        float finalValue = BaseValue;
        float percentAddSum = 0f;

        for (int i = 0; i < _modifiers.Count; i++)
        {
            var mod = _modifiers[i];

            switch (mod.Type)
            {
                case EStatModType.Add:
                    finalValue += mod.Value;
                    break;

                case EStatModType.PercentAdd:
                    percentAddSum += mod.Value;

                    // 다음 modifier가 PercentAdd가 아니면 그룹 종료 >> 한 번에 곱
                    bool isLastPercentAdd = (i + 1 == _modifiers.Count) || (_modifiers[i + 1].Type != EStatModType.PercentAdd);
                    if (isLastPercentAdd)
                    {
                        finalValue *= (1f + percentAddSum);
                        percentAddSum = 0f;
                    }
                    break;

                case EStatModType.PercentMult:
                    finalValue *= (1f + mod.Value);
                    break;
            }
        }

        // 부동소수점 잡노이즈 제거 — 소수 4자리에서 절단
        return (float)System.Math.Round(finalValue, 4);
    }

    /// <summary>
    /// type별로 분리 후 일괄 합산하는 방법이 있음
    /// 공식: (BaseValue + Add총합) * (1 + PercentAdd총합) * (PercentMult 독립곱 누적)
    /// </summary>
    /*private float CalculateFinalValue()
    {
        float sumAdd = 0f;
        float sumPercentAdd = 0f;
        float totalPercentMult = 1f; // 곱셈이므로 1로 초기화

        // 1. 단 한 번의 루프로 모든 모디파이어를 타입별로 분류하여 합산/누적
        for (int i = 0; i < _modifiers.Count; i++)
        {
            var mod = _modifiers[i];

            switch (mod.Type)
            {
                case EStatModType.Add:
                    sumAdd += mod.Value;
                    break;

                case EStatModType.PercentAdd:
                    sumPercentAdd += mod.Value;
                    break;

                case EStatModType.PercentMult:
                    totalPercentMult *= (1f + mod.Value);
                    break;
            }
        }

        // 2. 스탯 계산 공식에 맞춰 최종값 도출
        // (기본값 + 고정값 합산) * (1 + 퍼센트그룹 합산) * (독립 퍼센트 곱 누적)
        float finalValue = (BaseValue + sumAdd) * (1f + sumPercentAdd) * totalPercentMult;

        // 3. 부동소수점 잡노이즈 제거 — 소수 4자리에서 절단
        return (float)System.Math.Round(finalValue, 4);
    }*/
}