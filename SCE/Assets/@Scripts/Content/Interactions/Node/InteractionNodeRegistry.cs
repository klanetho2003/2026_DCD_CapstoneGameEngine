using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 조건/효과 노드의 명시 등록소. 런타임 리플렉션은 등록 시 어트리뷰트 1회 읽기뿐.
/// 키는 [InteractionNode] 어트리뷰트에서만 온다
/// </summary>
public static class InteractionNodeRegistry
{
    public readonly struct NodeInfo
    {
        public readonly string Key;
        public readonly Type Type;
        public readonly string DisplayName;
        public readonly string Description;

        public NodeInfo(string key, Type type, string displayName, string description)
        {
            Key = key; Type = type; DisplayName = displayName; Description = description;
        }
    }

    private static readonly Dictionary<string, Func<InteractionCondition>> s_conditionFactories = new();
    private static readonly Dictionary<string, Func<InteractionEffect>> s_effectFactories = new();
    private static readonly Dictionary<Type, string> s_keysByType = new();
    private static readonly List<NodeInfo> s_conditionInfos = new();
    private static readonly List<NodeInfo> s_effectInfos = new();

    /// <summary>에디터 툴용 목록</summary>
    public static IReadOnlyList<NodeInfo> ConditionInfos { get { return s_conditionInfos; } }
    public static IReadOnlyList<NodeInfo> EffectInfos { get { return s_effectInfos; } }

    static InteractionNodeRegistry()
    {
        InteractionNodeCatalog.RegisterAll(); // 최초 접근 시 1회. 에디터/런타임 어느 쪽이 먼저 건드려도 안전.
    }

    #region Register Method
    public static void RegisterCondition<T>() where T : InteractionCondition, new()
    {
        NodeInfo info = ReadInfo(typeof(T));
        if (s_conditionFactories.ContainsKey(info.Key))
        {
            LogPrinter.LogError($"[InteractionNodeRegistry] Condition 키 중복 >> {info.Key} ({typeof(T).Name})");
            return;
        }
        s_conditionFactories.Add(info.Key, static () => new T());
        s_keysByType.Add(typeof(T), info.Key);
        s_conditionInfos.Add(info);
    }

    public static void RegisterEffect<T>() where T : InteractionEffect, new()
    {
        NodeInfo info = ReadInfo(typeof(T));
        if (s_effectFactories.ContainsKey(info.Key))
        {
            LogPrinter.LogError($"[InteractionNodeRegistry] Effect 키 중복 >> {info.Key} ({typeof(T).Name})");
            return;
        }
        s_effectFactories.Add(info.Key, static () => new T());
        s_keysByType.Add(typeof(T), info.Key);
        s_effectInfos.Add(info);
    }
    #endregion

    public static bool TryCreateCondition(string key, out InteractionCondition condition)
    {
        if (key != null && s_conditionFactories.TryGetValue(key, out Func<InteractionCondition> factory))
        {
            condition = factory();
            return true;
        }
        condition = null;
        return false;
    }

    public static bool TryCreateEffect(string key, out InteractionEffect effect)
    {
        if (key != null && s_effectFactories.TryGetValue(key, out Func<InteractionEffect> factory))
        {
            effect = factory();
            return true;
        }
        effect = null;
        return false;
    }

    /// <summary>직렬화(에디터 저장) 시 타입 → 키</summary>
    public static bool TryGetKey(Type type, out string key)
    {
        return s_keysByType.TryGetValue(type, out key);
    }

    private static NodeInfo ReadInfo(Type type)
    {
        InteractionNodeAttribute attr = type.GetCustomAttribute<InteractionNodeAttribute>(false);
        Debug.Assert(attr != null, $"[InteractionNodeRegistry] {type.Name} 에 [InteractionNode] 어트리뷰트 없음");
        return new NodeInfo(attr.Key, type, attr.DisplayName, attr.Description);
    }
}