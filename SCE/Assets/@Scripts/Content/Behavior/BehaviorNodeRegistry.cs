using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

/// <summary>
/// Behavior Tree 노드 등록소 — JSON "type" 키 ↔ 노드 형식. 키는 [BehaviorNode] 어트리뷰트에서만 온다 (종류를 넘어 겹치지 않는다).
/// 게임은 Default (BehaviorNodeCatalog의 목록), 시험은 따로 만든 등록소에 시험용 노드를 넣는다 — 시험 노드가 편집기 목록에 섞이지 않게.
/// 리플렉션은 등록할 때 어트리뷰트를 한 번 읽는 것뿐이다. 노드는 new T()로 만든다 (IL2CPP에서도 형식이 남는다).
/// </summary>
public sealed class BehaviorNodeRegistry
{
    public readonly struct NodeInfo
    {
        public readonly string Key;
        public readonly Type Type;
        public readonly EBehaviorNodeKind Kind;
        public readonly string DisplayName;
        public readonly string Description;
        public readonly string Category; // 비면 종류 이름

        public NodeInfo(string key, Type type, EBehaviorNodeKind kind, string displayName, string description, string category)
        {
            Key = key;
            Type = type;
            Kind = kind;
            DisplayName = displayName;
            Description = description ?? "";
            Category = string.IsNullOrEmpty(category) ? kind.ToString() : category;
        }
    }

    private static BehaviorNodeRegistry s_default;

    private readonly Dictionary<string, Func<BehaviorNode>> _factories = new Dictionary<string, Func<BehaviorNode>>(StringComparer.Ordinal);
    private readonly Dictionary<string, NodeInfo> _infosByKey = new Dictionary<string, NodeInfo>(StringComparer.Ordinal);
    private readonly Dictionary<Type, NodeInfo> _infosByType = new Dictionary<Type, NodeInfo>();
    private readonly List<NodeInfo> _infos = new List<NodeInfo>();
    private JsonSerializerSettings _jsonSettings;
    private JsonSerializer _jsonSerializer;

    /// <summary>게임의 등록소 — 처음 읽을 때 BehaviorNodeCatalog.RegisterAll로 한 번 채운다</summary>
    public static BehaviorNodeRegistry Default
    {
        get
        {
            if (s_default == null)
            {
                var registry = new BehaviorNodeRegistry();
                BehaviorNodeCatalog.RegisterAll(registry);
                s_default = registry;
            }
            return s_default;
        }
    }

    /// <summary>편집기 노드 목록용 — 등록 차례</summary>
    public IReadOnlyList<NodeInfo> Infos => _infos;

    /// <summary>등록한다. 어트리뷰트가 없거나 · 종류를 알 수 없거나 · 키가 겹치면 에러를 남기고 false</summary>
    public bool Register<T>() where T : BehaviorNode, new()
    {
        Type type = typeof(T);
        BehaviorNodeAttribute attribute = type.GetCustomAttribute<BehaviorNodeAttribute>(false);
        if (attribute == null || string.IsNullOrEmpty(attribute.Key))
        {
            LogPrinter.LogError($"[BehaviorNodeRegistry] {type.Name}에 [BehaviorNode] 어트리뷰트 (키)가 없음 — 등록 안 함");
            return false;
        }
        if (TryKindOf(type, out EBehaviorNodeKind kind) == false)
        {
            LogPrinter.LogError($"[BehaviorNodeRegistry] {type.Name}은 Composite · Task · Decorator · Service 어느 것도 아님 — 등록 안 함");
            return false;
        }
        if (_factories.ContainsKey(attribute.Key) || _infosByType.ContainsKey(type))
        {
            LogPrinter.LogError($"[BehaviorNodeRegistry] 키 중복 >> {attribute.Key} ({type.Name}) — 등록 안 함");
            return false;
        }

        var info = new NodeInfo(attribute.Key, type, kind, attribute.DisplayName, attribute.Description, attribute.Category);
        _factories.Add(info.Key, static () => new T());
        _infosByKey.Add(info.Key, info);
        _infosByType.Add(type, info);
        _infos.Add(info);
        _jsonSettings = null;
        _jsonSerializer = null;
        return true;
    }

    /// <summary>키로 새 노드 — 그 키가 kind 종류일 때만. 없거나 종류가 다르면 false와 이유</summary>
    public bool TryCreate(string key, EBehaviorNodeKind kind, out BehaviorNode node, out string error)
    {
        node = null;
        if (key == null || _infosByKey.TryGetValue(key, out NodeInfo info) == false)
        {
            error = $"등록되지 않은 노드 '{key}'";
            return false;
        }
        if (info.Kind != kind)
        {
            error = $"'{key}'는 {info.Kind} — 이 자리에는 {kind}만 올 수 있다";
            return false;
        }
        node = _factories[key]();
        error = null;
        return true;
    }

    /// <summary>형식 → 키 (저장). 등록되지 않은 형식이면 false</summary>
    public bool TryGetKey(Type type, out string key)
    {
        bool found = _infosByType.TryGetValue(type, out NodeInfo info);
        key = found ? info.Key : null;
        return found;
    }

    public bool TryGetInfo(Type type, out NodeInfo info)
    {
        return _infosByType.TryGetValue(type, out info);
    }

    public bool TryGetInfo(string key, out NodeInfo info)
    {
        if (key == null)
        {
            info = default;
            return false;
        }
        return _infosByKey.TryGetValue(key, out info);
    }

    /// <summary>
    /// 이 등록소의 JSON 설정 — 모르는 칸은 에러 (MissingMemberHandling.Error · [JsonProperty]가 없는 멤버 이름도), enum은 정의된 이름만,
    /// int · float · bool · string은 토큰 형식이 맞아야 하고 float는 유한한 수만, 노드 칸은 "type" 키 변환 (Main · Decorator · Service 자리마다 종류를 가린다).
    /// 칸 이름은 Newtonsoft대로 대소문자를 가리지 않고 맞춘다. 등록이 바뀌면 다시 만든다. 트리를 읽고 저장할 때만 쓴다.
    /// </summary>
    internal JsonSerializerSettings JsonSettings
    {
        get
        {
            if (_jsonSettings == null)
            {
                _jsonSettings = new JsonSerializerSettings
                {
                    MissingMemberHandling = MissingMemberHandling.Error,
                    ContractResolver = new DeclaredOnlyContractResolver(),
                    Formatting = Formatting.Indented,
                    Converters =
                    {
                        new BehaviorEnumJsonConverter(),
                        new BehaviorPrimitiveJsonConverter(),
                        new InteractionNodeConverter<BehaviorMainNode>(CreateMainNode, KeyOrNull),
                        new InteractionNodeConverter<BehaviorDecorator>(key => (BehaviorDecorator)CreateOrThrow(key, EBehaviorNodeKind.Decorator), KeyOrNull),
                        new InteractionNodeConverter<BehaviorService>(key => (BehaviorService)CreateOrThrow(key, EBehaviorNodeKind.Service), KeyOrNull),
                    },
                };
            }
            return _jsonSettings;
        }
    }

    /// <summary>JsonSettings로 만든 직렬화기 — 등록이 바뀌면 다시 만든다</summary>
    internal JsonSerializer Serializer => _jsonSerializer ??= JsonSerializer.Create(JsonSettings);

    /// <summary>Main 노드 자리 — Composite 또는 Task. 아니면 까닭을 담은 JsonSerializationException</summary>
    private BehaviorMainNode CreateMainNode(string key)
    {
        if (TryGetInfo(key, out NodeInfo info) == false)
            throw new JsonSerializationException($"등록되지 않은 노드 '{key}'");
        if (info.Kind != EBehaviorNodeKind.Composite && info.Kind != EBehaviorNodeKind.Task)
            throw new JsonSerializationException($"'{key}'는 {info.Kind} — 이 자리에는 Composite · Task만 올 수 있다");
        return (BehaviorMainNode)_factories[key]();
    }

    /// <summary>등록되지 않은 키 · 다른 종류의 키는 까닭을 담은 JsonSerializationException — 트리 로드가 실패로 알린다</summary>
    private BehaviorNode CreateOrThrow(string key, EBehaviorNodeKind kind)
    {
        if (TryCreate(key, kind, out BehaviorNode node, out string error) == false)
            throw new JsonSerializationException(error);
        return node;
    }

    private string KeyOrNull(Type type)
    {
        return TryGetKey(type, out string key) ? key : null;
    }

    /// <summary>
    /// [JsonProperty]가 없는 멤버를 계약에서 뺀다 — JSON에 그 이름 (실행 중 값인 Index 등)이 오면 조용히 건너뛰지 않고 모르는 칸 에러.
    /// </summary>
    private sealed class DeclaredOnlyContractResolver : DefaultContractResolver
    {
        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        {
            IList<JsonProperty> properties = base.CreateProperties(type, memberSerialization);
            for (int i = properties.Count - 1; i >= 0; i--)
            {
                if (properties[i].Ignored)
                    properties.RemoveAt(i);
            }
            return properties;
        }
    }

    private static bool TryKindOf(Type type, out EBehaviorNodeKind kind)
    {
        if (typeof(BehaviorComposite).IsAssignableFrom(type))
            kind = EBehaviorNodeKind.Composite;
        else if (typeof(BehaviorTask).IsAssignableFrom(type))
            kind = EBehaviorNodeKind.Task;
        else if (typeof(BehaviorDecorator).IsAssignableFrom(type))
            kind = EBehaviorNodeKind.Decorator;
        else if (typeof(BehaviorService).IsAssignableFrom(type))
            kind = EBehaviorNodeKind.Service;
        else
        {
            kind = default;
            return false;
        }
        return true;
    }
}