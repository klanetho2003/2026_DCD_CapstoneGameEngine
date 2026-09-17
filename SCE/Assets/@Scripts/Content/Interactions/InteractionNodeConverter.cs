using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

/// <summary>
/// { "type": "Distance", ...params } ↔ 노드 인스턴스.
/// 어셈블리 이름을 JSON에 박는 TypeNameHandling을 쓰지 않는다 — 기획자가 읽을 수 있고 리네임에 안전.
/// </summary>
public sealed class InteractionNodeConverter<TBase> : JsonConverter where TBase : class
{
    private const string TypeKey = "type";

    private readonly Func<string, TBase> _create;

    public InteractionNodeConverter(Func<string, TBase> create)
    {
        _create = create; // 레지스트리 조회. 실패 시 null 반환 계약
    }

    public override bool CanConvert(Type objectType)
    {
        return typeof(TBase).IsAssignableFrom(objectType);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;

        JObject jo = JObject.Load(reader);

        string key = jo.Value<string>(TypeKey);
        if (string.IsNullOrEmpty(key))
            throw new JsonSerializationException($"'{TypeKey}' 누락 (path: {jo.Path})");

        TBase instance = _create(key);
        if (instance == null)
            throw new JsonSerializationException($"등록되지 않은 노드 '{key}' (path: {jo.Path})");

        // "type"은 노드 클래스의 멤버가 아니므로 Populate 전에 제거 (MissingMemberHandling.Error 회피)
        jo.Remove(TypeKey);

        using (JsonReader nodeReader = jo.CreateReader())
        {
            serializer.Populate(nodeReader, instance);
        }
        return instance;
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        Type type = value.GetType();
        if (InteractionNodeRegistry.TryGetKey(type, out string key) == false)
            throw new JsonSerializationException($"레지스트리에 없는 노드 타입 {type.Name}");

        // JObject.FromObject(value, serializer)는 이 컨버터를 재호출해 무한 재귀 → 계약 프로퍼티를 직접 순회
        JsonObjectContract contract = serializer.ContractResolver.ResolveContract(type) as JsonObjectContract;

        writer.WriteStartObject();
        writer.WritePropertyName(TypeKey);
        writer.WriteValue(key);

        foreach (JsonProperty property in contract.Properties)
        {
            if (property.Ignored || property.Readable == false)
                continue;

            object propertyValue = property.ValueProvider.GetValue(value);
            writer.WritePropertyName(property.PropertyName);
            serializer.Serialize(writer, propertyValue, property.PropertyType); // 중첩 노드는 여기서 다시 컨버터를 탄다
        }

        writer.WriteEndObject();
    }
}