using Data;
using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using static Define;



public interface IMovable
{
    float MoveSpeed { get; }
    Vector2 CacheMoveDirection { get; set; }
    //public float LeftForceDashDuration { get; }

    void Move(Vector3 direction, float speedWeight = 1f);
    void ForceMoveTo(Vector3 destination, float speedWeight = 1f, Action onComplete = null);
    void ForceMoveFor(Vector3 direction, float duration, float speedWeight = 1f, Action onComplete = null);

    void StopForceMove(Action onComplete = null);
}

/// <summary>enum 키 Dictionary의 boxing 방지용 공용 comparer</summary>
public struct EnumComparer<T> : IEqualityComparer<T> where T : unmanaged, Enum
{
    public static readonly EnumComparer<T> Instance = new EnumComparer<T>();

    public bool Equals(T x, T y) { return UnsafeUtility.EnumEquals(x, y); }
    public int GetHashCode(T obj) { return UnsafeUtility.EnumToInt(obj); }
}

public class CreatureStatDefine
{
    private readonly Dictionary<EStatType, StatDefineValue> _statDic = new(EnumComparer<EStatType>.Instance);

    /// <summary>어떤 stat이든 최종값이 바뀌면 invoke. (type, newValue)</summary>
    public event Action<EStatType, float> OnAnyStatChanged;

    /// <summary>JSON stat 데이터로 초기화. 재사용(pooling) 시 재호출 가능 — 내부에서 기존 구독 정리.</summary>
    public void InitDefineStat(List<StatData> statDatas)
    {
        Dispose();

        if (statDatas == null) return;

        foreach (var data in statDatas)
            SetStatValue(data.sType, data.sValue);
    }

    /// <summary>구독 해제 및 폐기. despawn/pooling 반환 시 호출 (Resource와 정리 관례 대칭).</summary>
    public void Dispose()
    {
        foreach (var sv in _statDic.Values)
            sv.OnValueChanged -= RelayStatChanged;

        _statDic.Clear();
    }

    private void RelayStatChanged(EStatType type, float value)
    {
        OnAnyStatChanged?.Invoke(type, value);
    }

    #region StatModifier API
    /// <summary>StatDefineValue 자체에 접근 (Modifiers 리스트 조회, UI 표시, Resource 연결 등). 미정의 시 null.</summary>
    public StatDefineValue GetStatValue(EStatType type)
    {
        return _statDic.TryGetValue(type, out var sv) ? sv : null;
    }

    public void SetStatValue(EStatType type, float value)
    {
        if (_statDic.TryGetValue(type, out var sv))
        {
            sv.SetBaseValue(value);
            return;
        }

        var newSv = new StatDefineValue(type, value);
        newSv.OnValueChanged += RelayStatChanged;
        _statDic[type] = newSv;
    }

    public void AddModifier(EStatType type, StatModifier modifier)
    {
        if (_statDic.TryGetValue(type, out var sv) == false)
        {
            LogPrinter.LogError($"[CreatureStat] Stat '{type}'이 정의되지 않아 modifier를 추가할 수 없습니다. JSON 데이터를 확인하세요.");
            return;
        }

        sv.AddModifier(modifier);
    }

    public bool RemoveModifier(EStatType type, StatModifier modifier)
    {
        return _statDic.TryGetValue(type, out var sv) && sv.RemoveModifier(modifier);
    }

    /// <summary>
    /// 모든 stat에서 특정 source의 modifier를 일괄 제거. 장비 해제·버프 종료를 한 호출로 정리.
    /// 반환: 제거된 modifier 총 개수.
    /// </summary>
    public int RemoveModifiersFromSource(object source)
    {
        if (source == null) return 0;

        int total = 0;
        foreach (var sv in _statDic.Values)
            total += sv.RemoveModifiersFromSource(source);

        return total;
    }
    #endregion
}

public class CreatureStatResource
{
    private readonly Dictionary<EStatResourceType, StatResourceValue> _statResources
        = new(EnumComparer<EStatResourceType>.Instance);

    /// <summary>Stat 시스템과 연결하여 초기화. SetInfo 시 호출. 재사용 시 재호출 가능.</summary>
    public void InitResourcesStat(List<StatResourceData> statResourceDatas, CreatureStatDefine creatureStat)
    {
        Dispose(); // 정리

        if (statResourceDatas == null) return;

        if (creatureStat == null)
        {
            LogPrinter.LogError("[CreatureResource] creatureStat is null");
            return;
        }

        foreach (var rStat in statResourceDatas)
        {
            // fail-fast — 기반 stat 미정의를 여기서 발견 (미검출 시 생성자/첫 clamp 시점에 원인 먼 폭발)
            var maxStat = creatureStat.GetStatValue(rStat.sType);
            if (maxStat == null)
            {
                LogPrinter.LogError($"[CreatureResource] Resource '{rStat.rType}'의 기반 stat '{rStat.sType}'이 정의되지 않음");
                continue;
            }

            var resource = new StatResourceValue(rStat.rType, maxStat, initialCurrent: rStat.rValue);
            if (_statResources.TryAdd(rStat.rType, resource) == false)
            {
                LogPrinter.LogWarning($"[CreatureResource] Resource '{rStat.rType}' 중복 정의");
                resource.Dispose();
            }
        }
    }

    /// <summary>StatResourceValue 자체에 접근.</summary>
    public StatResourceValue GetResource(EStatResourceType type)
    {
        return _statResources.TryGetValue(type, out var r) ? r : null;
    }

    public void ModifyResource(EStatResourceType type, float delta)
    {
        if (_statResources.TryGetValue(type, out var r))
            r.SetCurrent(delta);
    }

    /// <summary>구독 해제 및 폐기. despawn/pooling 반환 시 호출.</summary>
    public void Dispose()
    {
        foreach (var r in _statResources.Values)
            r.Dispose();

        _statResources.Clear();
    }
}
