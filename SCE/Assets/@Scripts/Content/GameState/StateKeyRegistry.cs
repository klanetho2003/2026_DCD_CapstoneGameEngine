using System;
using System.Collections.Generic;
using static Define;

/// <summary>
/// 문자열 키 <--> 핸들 등록소. 키 선언의 유일한 원본.
/// Generation: 재구축될 때마다 증가 — 핸들을 캐싱한 쪽(조건 노드 등)이 무효화 여부를 비교한다.
/// </summary>
public sealed class StateKeyRegistry
{
    public const string ReservedPrefix = "sys.";

    private readonly Dictionary<string, int> _indexByKey = new(StringComparer.Ordinal);
    private readonly List<StateKeyDefinition> _definitions = new();

    public int Count { get { return _definitions.Count; } }
    public int Generation { get; private set; }

    public void Clear()
    {
        _indexByKey.Clear();
        _definitions.Clear();
        Generation++;
    }

    /// <param name="allowReserved">시스템 키 등록용. 기획 파일 경로에서는 false.</param>
    public bool TryAdd(StateKeyDefinition definition, bool allowReserved, out string error)
    {
        if (definition == null)
        {
            error = "[StateKeyRegistry] definition is null";
            return false;
        }
        if (IsValidKeyName(definition.Key, out error) == false)
            return false;
        if (allowReserved == false && definition.Key.StartsWith(ReservedPrefix, StringComparison.Ordinal))
        {
            error = $"'{definition.Key}' — '{ReservedPrefix}'로 시작하는 키는 시스템 예약(개발자가 코드로 추가하는 시스템)";
            return false;
        }
        if (_indexByKey.ContainsKey(definition.Key))
        {
            error = $"'{definition.Key}' — 키 중복";
            return false;
        }
        if (definition.Reset == EStateResetPolicy.EveryNDays && definition.Interval < 1)
        {
            error = $"'{definition.Key}' — EveryNDays는 interval 1 이상 필요 (현재 {definition.Interval})";
            return false;
        }

        _indexByKey.Add(definition.Key, _definitions.Count);
        _definitions.Add(definition);
        error = null;
        return true;
    }

    public bool TryGetHandle(string key, out StateHandle handle)
    {
        if (key != null && _indexByKey.TryGetValue(key, out int index))
        {
            handle = new StateHandle(index);
            return true;
        }
        handle = default;
        return false;
    }

    public StateKeyDefinition GetDefinition(int index)
    {
        return (uint)index < (uint)_definitions.Count ? _definitions[index] : null;
    }

    /// <summary>
    /// 허용 문자: 영문, 숫자, '.', '_', '-'. 정규식 대신 문자 순회 — 로드 경로라도 불필요한 할당을 만들지 않는다.
    /// </summary>
    public static bool IsValidKeyName(string key, out string error)
    {
        if (string.IsNullOrEmpty(key))
        {
            error = "키가 비어 있음";
            return false;
        }

        for (int i = 0; i < key.Length; i++)
        {
            char c = key[i];
            bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                   || c == '.' || c == '_' || c == '-';
            if (ok == false)
            {
                error = $"'{key}' — 허용되지 않는 문자 '{c}' (영문·숫자·'.'·'_'·'-'만)";
                return false;
            }
        }

        error = null;
        return true;
    }
}