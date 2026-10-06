using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// enum 값의 표시 이름 — Define.cs의 [InspectorName]을 읽는다 (없으면 enum 이름).
/// EnumField는 [InspectorName]을 스스로 쓰지만, PopupField처럼 목록을 직접 만드는 곳은 이 함수를 써서
/// 표시 이름의 원본을 Define.cs 한 곳으로 유지한다.
/// </summary>
public static class EditorEnumLabels
{
    private static readonly Dictionary<Enum, string> s_cache = new Dictionary<Enum, string>();

    public static string Of(Enum value)
    {
        if (s_cache.TryGetValue(value, out string label))
            return label;

        string name = value.ToString();
        FieldInfo field = value.GetType().GetField(name, BindingFlags.Public | BindingFlags.Static);
        InspectorNameAttribute attribute = field?.GetCustomAttribute<InspectorNameAttribute>();
        label = attribute != null ? attribute.displayName : name;

        s_cache[value] = label;
        return label;
    }
}