using System;
using static Define;

/// <summary>
/// Stat 보정값 제어
/// </summary>
public interface IStatModifierController
{
    StatDefineValue GetStatValue(EStatType type);
    void AddStatModifier(EStatType type, StatModifier modifier);
    bool RemoveStatModifier(EStatType type, StatModifier modifier);
    int ClearStatModifiersFromSource(object source);
}

/// <summary>
/// 단일 stat 보정값. 출처(Source) 추적으로 그룹 단위 일괄 제거 가능.
/// 
/// 사용 예시:
///   new StatModifier(0.3f, EStatModType.PercentAdd, source: this);
///   new StatModifier(5f,   EStatModType.Add,        source: armor);
/// </summary>
public readonly struct StatModifier : IEquatable<StatModifier>
{
    public readonly float Value;
    public readonly EStatModType Type;
    public readonly int Order;
    public readonly object Source;

    /// <summary>
    /// </summary>
    /// <param name="value">보정값. Add는 절대치, PercentAdd/Mult는 비율(0.3 = 30%).</param>
    /// <param name="type">적용 방식. Order도 이 값으로 자동 설정.</param>
    /// <param name="source">출처 객체. ClearStatModifiersFromSource로 일괄 제거 시 사용.</param>
    /// <param name="order">정렬 우선순위. 기본값(-1)이면 Type 값을 사용.</param>
    public StatModifier(float value, EStatModType type, object source = null, int order = -1)
    {
        Value = value;
        Type = type;
        Order = (order < 0) ? (int)type : order;
        Source = source;
    }

    public bool Equals(StatModifier other)
    {
        return Value == other.Value
            && Type == other.Type
            && Order == other.Order
            && ReferenceEquals(Source, other.Source);
    }

    public override bool Equals(object obj) { return obj is StatModifier other && Equals(other); }
    public override int GetHashCode() { return HashCode.Combine(Value, Type, Order, Source); }

    public static bool operator ==(StatModifier left, StatModifier right) => left.Equals(right);
    public static bool operator !=(StatModifier left, StatModifier right) => !left.Equals(right);
}