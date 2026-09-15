using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

/// <summary>
/// JSON ↔ InteractionSetDefinition. 순수 함수. 파일 IO는 호출 측(Managers 데이터 로딩 경로) 책임.
/// </summary>
public static class InteractionLoader
{
    // 파싱 결과물 구조 setting
    private static readonly JsonSerializerSettings s_settings = new JsonSerializerSettings
    {
        // 오타 키, [JsonIgnore] 누락으로 새어 나온 키를 로드 시점에 에러로 잡는다
        MissingMemberHandling = MissingMemberHandling.Error,
        Formatting = Formatting.Indented,
        Converters =
        {
            new StringEnumConverter(),
            new InteractionNodeConverter<InteractionCondition>(key =>
                InteractionNodeRegistry.TryCreateCondition(key, out InteractionCondition c) ? c : null),
            new InteractionNodeConverter<InteractionEffect>(key =>
                InteractionNodeRegistry.TryCreateEffect(key, out InteractionEffect e) ? e : null),
        },
    };

    private static readonly List<string> s_errors = new List<string>();
    private static readonly List<string> s_warnings = new List<string>();

    /// <summary>파싱 > 정규화(null인 곳 밀기) > Check.</summary>
    public static InteractionSetDefinition Parse(string json)
    {
        InteractionSetDefinition set;
        try
        {
            set = JsonConvert.DeserializeObject<InteractionSetDefinition>(json, s_settings);
        }
        catch (JsonException e)
        {
            LogPrinter.LogError($"[InteractionLoader] JSON 파싱 실패 >> {e.Message}");
            return null;
        }

        Normalize(set);

        s_errors.Clear();
        s_warnings.Clear();
        bool ok = InteractionValidator.Validate(set, s_errors, s_warnings);

        for (int i = 0; i < s_warnings.Count; i++)
            LogPrinter.Log($"[InteractionLoader] 경고 >> {s_warnings[i]}");

        if (ok == false)
        {
            for (int i = 0; i < s_errors.Count; i++)
                LogPrinter.LogError($"[InteractionLoader] 에러 >> {s_errors[i]}");
            return null;
        }

        return set;
    }

    /// <summary>에디터용: 검증 없이 파싱 + 정규화만. 깨진 파일도 툴에서 열어 고치기 위함. 실패 시 null + error.</summary>
    public static InteractionSetDefinition ParseRaw(string json, out string error)
    {
        error = null;
        try
        {
            InteractionSetDefinition set = JsonConvert.DeserializeObject<InteractionSetDefinition>(json, s_settings);
            Normalize(set);
            return set;
        }
        catch (JsonException e)
        {
            error = e.Message;
            return null;
        }
    }

    /// <summary>에디터용 deep copy (카드 복제). JSON 왕복이므로 노드 상태 필드는 복사되지 않는다 — 의도된 동작.</summary>
    public static InteractionDefinition CloneDefinition(InteractionDefinition source)
    {
        string json = JsonConvert.SerializeObject(source, s_settings);
        InteractionDefinition clone = JsonConvert.DeserializeObject<InteractionDefinition>(json, s_settings);
        if (clone.Conditions == null) clone.Conditions = Array.Empty<InteractionCondition>();
        if (clone.EffectPrototypes == null) clone.EffectPrototypes = Array.Empty<InteractionEffect>();
        return clone;
    }

    /// <summary>에디터 저장용</summary>
    public static string Serialize(InteractionSetDefinition set)
    {
        Normalize(set);
        return JsonConvert.SerializeObject(set, s_settings);
    }

    /// <summary>null 배열인 곳에 Empty로 밀어주기. InteractionRuntime 생성자가 null을 만나지 않도록 함.</summary>
    public static void Normalize(InteractionSetDefinition set)
    {
        if (set == null)
            return;

        if (set.Interactions == null)
            set.Interactions = Array.Empty<InteractionDefinition>();

        for (int i = 0; i < set.Interactions.Length; i++)
        {
            InteractionDefinition def = set.Interactions[i];
            if (def == null)
                continue;
            if (def.Conditions == null)
                def.Conditions = Array.Empty<InteractionCondition>();
            if (def.EffectPrototypes == null)
                def.EffectPrototypes = Array.Empty<InteractionEffect>();
        }
    }
}