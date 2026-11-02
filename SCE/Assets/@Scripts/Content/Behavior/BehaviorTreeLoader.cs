using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// 트리 JSON ↔ 트리 데이터 · 정의. 파일 읽기는 부르는 쪽 (게임의 데이터 로드 · 편집기)이 맡는다. 로그를 남기지 않고 문제를 모은다.
/// </summary>
public static class BehaviorTreeLoader
{
    /// <summary>같은 객체 안의 같은 칸 이름은 에러 · 줄 정보를 남긴다 (문제 문장에 원문의 줄 · 칸)</summary>
    private static readonly JsonLoadSettings s_loadSettings = new JsonLoadSettings
    {
        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
        LineInfoHandling = LineInfoHandling.Load,
    };

    /// <summary>JSON → 트리 데이터 (컴파일 전). 구조 검사는 하지 않는다 — 편집기가 깨진 트리도 열어 고칠 수 있게. 실패하면 null과 까닭</summary>
    public static BehaviorTreeAsset ParseAsset(string json, BehaviorNodeRegistry registry, out string error)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "빈 JSON";
            return null;
        }
        try
        {
            JToken token;
            using (var reader = new JsonTextReader(new StringReader(json)))
            {
                token = JToken.ReadFrom(reader, s_loadSettings);
                while (reader.Read()) { } // 뒤에는 주석만 — 다른 글이면 JsonReaderException
            }
            if (token.Type != JTokenType.Object)
            {
                error = $"JSON 읽기 실패 >> 트리 JSON은 객체여야 한다 ({token.Type})";
                return null;
            }
            error = null;
            return token.ToObject<BehaviorTreeAsset>(registry.Serializer);
        }
        catch (JsonException exception)
        {
            error = "JSON 읽기 실패 >> " + exception.Message;
            return null;
        }
        catch (Exception exception) // 노드 변환기는 JsonException이 아닌 예외도 던진다 ("type"이 객체 · 배열이면 InvalidCastException)
        {
            error = $"JSON 읽기 실패 >> {exception.GetType().Name}: {exception.Message}";
            return null;
        }
    }

    /// <summary>JSON → 컴파일된 정의 (게임이 쓰는 길). 문제는 messages에 모은다 (읽기 실패도 에러 하나로). 실패하면 null</summary>
    public static BehaviorTreeDefinition Load(string json, BehaviorNodeRegistry registry, List<BehaviorCompileMessage> messages)
    {
        BehaviorTreeAsset asset = ParseAsset(json, registry, out string error);
        if (asset == null)
        {
            messages.Add(new BehaviorCompileMessage(EBehaviorMessageSeverity.Error, "", null, "", error));
            return null;
        }
        return BehaviorTreeCompiler.Compile(asset, registry, messages);
    }

#if UNITY_EDITOR // 편집기 · 시험만 부른다 — 플레이어 빌드에서 빠진다
    /// <summary>트리 데이터 → JSON (편집기 저장). 등록되지 않은 노드 형식 · 유한하지 않은 수면 JsonSerializationException</summary>
    public static string Serialize(BehaviorTreeAsset asset, BehaviorNodeRegistry registry)
    {
        return JsonConvert.SerializeObject(asset, registry.JsonSettings);
    }
#endif
}