using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

/// <summary>
/// 상태 정의 JSON <--> StateDefinitionSet.
/// 파일 단위 구조 검증만 하고, 키&규칙 검증은 StateDefinitionValidator가 한다 (파일 간 중복&참조까지 보기 위해).
/// </summary>
public static class StateDefinitionLoader
{
    private static readonly JsonSerializerSettings s_settings = new JsonSerializerSettings
    {
        MissingMemberHandling = MissingMemberHandling.Error, // 오타 키를 로드 시점에 거부
        Converters = { new StringEnumConverter() },
    };

    /// <summary>런타임 로드용. 실패 사유를 로그로 남기고 null.</summary>
    public static StateDefinitionSet Parse(string json)
    {
        StateDefinitionSet set = ParseRaw(json, out string error);
        if (set == null)
        {
            LogPrinter.LogError($"[StateDefinitionLoader] {error}");
            return null;
        }
        if (string.IsNullOrEmpty(set.Id))
        {
            LogPrinter.LogError("[StateDefinitionLoader] id 비어 있음");
            return null;
        }
        return set;
    }

    /// <summary>
    /// 에디터 툴용. 로그 없이 파싱 + 정규화만 한다. id가 비어 있어도 통과시킨다 (툴이 파일 이름으로 채운다).
    /// 실패하면 null과 사유를 돌려준다 — 사유 문구는 런타임 로그 본문과 같다.
    /// </summary>
    public static StateDefinitionSet ParseRaw(string json, out string error)
    {
        StateDefinitionSet set;
        try
        {
            set = JsonConvert.DeserializeObject<StateDefinitionSet>(json, s_settings);
        }
        catch (JsonException e)
        {
            error = $"JSON 파싱 실패 >> {e.Message}";
            return null;
        }

        if (set == null)
        {
            error = "빈 JSON";
            return null;
        }

        Normalize(set);
        error = null;
        return set;
    }

    /// <summary>null 배열 → 빈 배열. 런타임&툴이 null 검사 없이 순회하도록.</summary>
    private static void Normalize(StateDefinitionSet set)
    {
        set.Keys ??= Array.Empty<StateKeyDefinition>();
        set.Rules ??= Array.Empty<StateRuleDefinition>();
        for (int i = 0; i < set.Rules.Length; i++)
            NormalizeRule(set.Rules[i]);
    }

    private static void NormalizeRule(StateRuleDefinition rule)
    {
        if (rule == null)
            return;
        rule.Require ??= Array.Empty<StateRequireDefinition>();
        rule.Ops ??= Array.Empty<StateOpDefinition>();
    }

    // =======
    // Helpers
    // =======

    /// <summary>저장용 JSON 텍스트. 들여쓰기 2칸. 필드 순서&생략 규칙은 StateDefinition의 특성(Order, ShouldSerialize)을 따른다.</summary>
    public static string Serialize(StateDefinitionSet set)
    {
        Normalize(set);
        return JsonConvert.SerializeObject(set, Formatting.Indented, s_settings);
    }

    /// <summary>툴의 "복제"용 깊은 복사. JSON 왕복이라 저장되는 내용만 복사된다.</summary>
    public static StateKeyDefinition CloneKey(StateKeyDefinition source)
    {
        if (source == null)
            return null;

        string json = JsonConvert.SerializeObject(source, s_settings);
        return JsonConvert.DeserializeObject<StateKeyDefinition>(json, s_settings);
    }

    /// <summary>툴의 "복제"용 깊은 복사. 저장 시 생략된 빈 require도 빈 배열로 되돌린다.</summary>
    public static StateRuleDefinition CloneRule(StateRuleDefinition source)
    {
        if (source == null)
            return null;

        string json = JsonConvert.SerializeObject(source, s_settings);
        StateRuleDefinition clone = JsonConvert.DeserializeObject<StateRuleDefinition>(json, s_settings);
        NormalizeRule(clone);
        return clone;
    }
}