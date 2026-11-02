using System;
using System.Collections.Generic;

/// <summary>
/// 컴파일 · 읽기 문제 하나 — 무게 · 자리 (노드 경로 "root (Selector).children[1] (Wait)" · "settings" · "blackboard[0]", 트리 전체면 "") ·
/// 노드 (노드의 문제가 아니면 null) · 칸 (JSON 키, 없으면 "") · 문장. 편집기는 노드 · 칸으로 문제 자리를 고른다.
/// </summary>
public readonly struct BehaviorCompileMessage
{
    public readonly EBehaviorMessageSeverity Severity;
    public readonly string Path;
    public readonly BehaviorNode Node;
    public readonly string Field;
    public readonly string Text;

    public BehaviorCompileMessage(EBehaviorMessageSeverity severity, string path, BehaviorNode node, string field, string text)
    {
        Severity = severity;
        Path = path ?? "";
        Node = node;
        Field = field ?? "";
        Text = text ?? "";
    }

    public bool IsError => Severity == EBehaviorMessageSeverity.Error;

    /// <summary>"자리: 칸: 문장" (빈 부분은 뺀다) — 로그 · 시험용</summary>
    public override string ToString()
    {
        string text = Field.Length > 0 ? Field + ": " + Text : Text;
        return Path.Length > 0 ? Path + ": " + text : text;
    }
}

#if UNITY_EDITOR // 편집기만 쓴다 — 플레이어 빌드에서 빠진다
/// <summary>노드 칸 하나가 고르는 Blackboard 키 — 칸 (JSON 키) · 받는 형식 · 꼭 골라야 하는가. 편집기의 키 고르기 목록이 쓴다 (BehaviorTreeCompiler.CollectKeyFields)</summary>
public readonly struct BehaviorKeyFieldInfo
{
    public readonly string Field;
    public readonly EBehaviorKeyTypeMask Allowed;
    public readonly bool Required;

    public BehaviorKeyFieldInfo(string field, EBehaviorKeyTypeMask allowed, bool required)
    {
        Field = field ?? "";
        Allowed = allowed;
        Required = required;
    }
}
#endif

/// <summary>
/// 컴파일 중 노드가 쓰는 창구 — 키 해석 · 문제 보고. 문제에는 지금 컴파일하는 노드와 그 자리가 붙는다.
/// </summary>
public sealed class BehaviorCompileContext
{
    private readonly List<BehaviorCompileMessage> _messages;
    private string _path = "";
    private BehaviorNode _node;
#if UNITY_EDITOR
    internal List<BehaviorKeyFieldInfo> KeyFields; // 편집기 — ResolveKey가 밝힌 키 칸을 모은다 (CollectKeyFields만 둔다, 게임 컴파일은 null)
#endif

    internal BehaviorCompileContext(List<BehaviorCompileMessage> messages)
    {
        _messages = messages;
    }

    /// <summary>트리의 Blackboard 배치 — 키 목록이 깨졌으면 null (그때는 노드 Compile을 부르지 않는다)</summary>
    public BehaviorBlackboardLayout Blackboard { get; internal set; }

    /// <summary>이 컴파일에서 낸 에러 수</summary>
    internal int ErrorCount { get; private set; }

    public void Error(string message)
    {
        Add(EBehaviorMessageSeverity.Error, _path, _node, "", message);
    }

    /// <summary>칸 (JSON 키) 하나의 에러</summary>
    public void Error(string field, string message)
    {
        Add(EBehaviorMessageSeverity.Error, _path, _node, field, message);
    }

    public void Warning(string message)
    {
        Add(EBehaviorMessageSeverity.Warning, _path, _node, "", message);
    }

    /// <summary>칸 (JSON 키) 하나의 경고</summary>
    public void Warning(string field, string message)
    {
        Add(EBehaviorMessageSeverity.Warning, _path, _node, field, message);
    }

    /// <summary>
    /// 키 고르기를 칸 번호로 — 고르지 않았으면 required일 때 에러, 없는 키 · 형식이 allowed에 없으면 에러. 해석하면 true.
    /// field는 문장에 쓰는 칸 이름 (JSON 키).
    /// </summary>
    public bool ResolveKey(ref BehaviorKeySelector selector, string field, EBehaviorKeyTypeMask allowed, bool required = true)
    {
#if UNITY_EDITOR
        KeyFields?.Add(new BehaviorKeyFieldInfo(field, allowed, required));
#endif
        selector.SlotPlusOne = 0;
        if (selector.IsSelected == false)
        {
            if (required)
                Error(field, "키를 고르지 않음");
            return false;
        }
        if (TryResolve(selector.Name, field, allowed, out int slot) == false)
            return false;
        selector.SlotPlusOne = slot + 1;
        return true;
    }

    /// <summary>값 또는 키 칸 — 키를 골랐으면 Float 키여야 한다</summary>
    public void Resolve(ref BehaviorFloat value, string field)
    {
        value.KeySlotPlusOne = value.UsesKey && TryResolve(value.Key, field, EBehaviorKeyTypeMask.Float, out int slot) ? slot + 1 : 0;
    }

    /// <summary>값 또는 키 칸 — 키를 골랐으면 Int 키여야 한다</summary>
    public void Resolve(ref BehaviorInt value, string field)
    {
        value.KeySlotPlusOne = value.UsesKey && TryResolve(value.Key, field, EBehaviorKeyTypeMask.Int, out int slot) ? slot + 1 : 0;
    }

    /// <summary>값 또는 키 칸 — 키를 골랐으면 Bool 키여야 한다</summary>
    public void Resolve(ref BehaviorBool value, string field)
    {
        value.KeySlotPlusOne = value.UsesKey && TryResolve(value.Key, field, EBehaviorKeyTypeMask.Bool, out int slot) ? slot + 1 : 0;
    }

    /// <summary>값 또는 키 칸 — 키를 골랐으면 Vector2 키여야 한다</summary>
    public void Resolve(ref BehaviorVector2 value, string field)
    {
        value.KeySlotPlusOne = value.UsesKey && TryResolve(value.Key, field, EBehaviorKeyTypeMask.Vector2, out int slot) ? slot + 1 : 0;
    }

    public static EBehaviorKeyTypeMask MaskOf(EBehaviorKeyType type)
    {
        return (EBehaviorKeyTypeMask)(1 << (int)type);
    }

    /// <summary>문제에 붙일 노드와 자리 — 노드가 아닌 문제면 node null</summary>
    internal void At(string path, BehaviorNode node)
    {
        _path = path;
        _node = node;
    }

    internal void Add(EBehaviorMessageSeverity severity, string path, BehaviorNode node, string field, string text)
    {
        _messages.Add(new BehaviorCompileMessage(severity, path, node, field, text));
        if (severity == EBehaviorMessageSeverity.Error)
            ErrorCount++;
    }

    private bool TryResolve(string name, string field, EBehaviorKeyTypeMask allowed, out int slot)
    {
        if (Blackboard.TryGetSlot(name, out slot) == false)
        {
            Error(field, $"Blackboard에 없는 키 '{name}'");
            return false;
        }
        EBehaviorKeyType type = Blackboard.TypeOf(slot);
        if ((allowed & MaskOf(type)) == 0)
        {
            Error(field, $"키 '{name}'는 {type} — {allowed} 키만 쓸 수 있다");
            return false;
        }
        return true;
    }
}

/// <summary>
/// 트리 데이터 (BehaviorTreeAsset) → 실행할 정의 (BehaviorTreeDefinition). 게임 로드와 편집기 검사가 같은 판정을 쓴다.
///  1. 설정 · Blackboard 키 검사 → 배치 (키 이름 → 칸 번호, 키 정의는 복사)
///  2. 구조 — 루트는 Composite, Composite는 자식 하나 이상, 빈 칸 · 같은 노드 객체 두 번 · 이미 컴파일된 노드 · 깊이 · 노드 수 상한
///  3. 실행 순서 (깊이 우선 — 노드, 그 Decorator · Service, 그다음 자식) · 부모 · 자식 차례 · 부분 트리 끝 · 칸 자리
///  4. 노드마다 칸 수 · 종류의 공통 칸 (그 자리에서 쓸 수 있는 Observer Aborts · 결과 뒤집기 · Service 간격) · 노드 컴파일 (키 해석 · 노드 값)
///  5. 지켜보는 조건 — 노드마다 Self · LowerPriority 표시, LowerPriority로 지켜보는 노드 목록 · 노드 id 겹침
/// 문제는 한 번에 모두 모은다 (키 목록이 깨지면 노드 Compile만 건너뛴다). 노드 코드의 예외도 문제로 바꾼다 — 이 함수는 예외를 던지지 않는다.
/// 에러가 하나라도 있으면 null. 성공하면 노드 객체에 결과 (번호 · 칸 자리 · 키 칸)가 남고 IsCompiled — 한 노드 객체는 한 정의에만 컴파일된다.
/// </summary>
public static class BehaviorTreeCompiler
{
    public const int MaxMainNodes = 1024;

    /// <summary>루트 0부터의 깊이 상한 — 트리 JSON의 중첩이 JSON 읽기 깊이 상한 (64) 안에 들도록</summary>
    public const int MaxDepth = 24;

    /// <summary>노드 하나가 밝힐 수 있는 칸 수 (MemorySlotCount)</summary>
    public const int MaxNodeMemorySlots = 64;

    /// <summary>트리 하나의 칸 수 합 (몬스터마다 16바이트씩)</summary>
    public const int MaxTreeMemorySlots = 1 << 16;

    public static BehaviorTreeDefinition Compile(BehaviorTreeAsset asset, BehaviorNodeRegistry registry, List<BehaviorCompileMessage> messages)
    {
        var context = new BehaviorCompileContext(messages);
        try
        {
            return CompileChecked(asset, registry, context);
        }
        catch (Exception exception) // 컴파일러 · 노드 코드의 예상하지 못한 예외도 그 트리의 실패로
        {
            context.Add(EBehaviorMessageSeverity.Error, "", null, "", $"컴파일 중 예외 {exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    public static bool HasLowerPriority(EBehaviorAbortMode mode)
    {
        return mode == EBehaviorAbortMode.LowerPriority || mode == EBehaviorAbortMode.Both;
    }

    public static bool HasSelf(EBehaviorAbortMode mode)
    {
        return mode == EBehaviorAbortMode.Self || mode == EBehaviorAbortMode.Both;
    }

#if UNITY_EDITOR // 편집기만 부른다 — 플레이어 빌드에서 빠진다
    /// <summary>
    /// 편집기 — 노드가 고르는 Blackboard 키 칸 (Compile에서 ResolveKey로 밝힌 칸 · 받는 형식 · 꼭 골라야 하는가)을 밝힌 차례로 fields에 담는다 (먼저 비운다).
    /// 노드의 Compile을 키가 없는 Blackboard로 한 번 불러 본다 — 노드는 키 칸을 모두 해석하므로 (중간에 return하지 않는다) 칸이 모두 모인다.
    /// 문제는 버리고, 노드 코드의 예외는 그때까지 모은 칸으로 끝낸다. 노드에는 해석하지 않은 상태 (키 칸 번호 없음)가 남는다 — 편집기 사본의 노드에만 쓴다. O(노드 Compile)
    /// </summary>
    public static void CollectKeyFields(BehaviorNode node, List<BehaviorKeyFieldInfo> fields)
    {
        fields.Clear();
        if (node == null)
            return;
        var context = new BehaviorCompileContext(new List<BehaviorCompileMessage>())
        {
            Blackboard = new BehaviorBlackboardLayout(Array.Empty<BehaviorBlackboardKey>()),
            KeyFields = fields,
        };
        try
        {
            node.Compile(context);
        }
        catch (Exception) // 노드 코드의 예외 — 그때까지 밝힌 칸만 (예외는 트리 검사가 알린다)
        {
        }
    }
#endif

    private static BehaviorTreeDefinition CompileChecked(BehaviorTreeAsset asset, BehaviorNodeRegistry registry, BehaviorCompileContext context)
    {
        if (asset == null)
        {
            context.Add(EBehaviorMessageSeverity.Error, "", null, "", "트리가 비었음 (null)");
            return null;
        }
        if (string.IsNullOrEmpty(asset.Id))
            context.Add(EBehaviorMessageSeverity.Error, "", null, "id", "없음");
        CheckSettings(asset.Settings, context);
        BehaviorBlackboardLayout layout = BuildLayout(asset.Keys, context);
        context.Blackboard = layout;
        if (asset.Root == null)
        {
            context.Add(EBehaviorMessageSeverity.Error, "", null, "root", "없음");
            return null;
        }
        if (asset.Root is BehaviorComposite == false)
        {
            context.Add(EBehaviorMessageSeverity.Error, $"root ({KeyOf(registry, asset.Root)})", asset.Root, "", "Composite여야 한다");
            return null;
        }

        var walk = new Walk(registry, context);
        walk.Visit(asset.Root, null, -1, 0, "root");

        var mainSlots = new int[walk.Mains.Count];
        for (int i = 0; i < walk.Mains.Count; i++)
        {
            BehaviorMainNode main = walk.Mains[i];
            context.At(walk.MainPaths[i], main);
            mainSlots[i] = SlotCountOf(main, context);
            if (layout != null)
                CompileNode(main, context);
        }
        var auxiliarySlots = new int[walk.Auxiliaries.Count];
        for (int i = 0; i < walk.Auxiliaries.Count; i++)
        {
            BehaviorAuxiliaryNode auxiliary = walk.Auxiliaries[i];
            context.At(walk.AuxiliaryPaths[i], auxiliary);
            auxiliarySlots[i] = SlotCountOf(auxiliary, context);
            if (auxiliary is BehaviorDecorator decorator)
                CheckDecorator(decorator, walk.Mains[auxiliary.OwnerIndex], walk, context);
            else if (auxiliary is BehaviorService service)
                CheckService(service, context);
            if (layout != null)
                CompileNode(auxiliary, context);
        }
        context.At("", null);
        int slots = AssignMemory(walk, mainSlots, auxiliarySlots, context);
        int[] lowerPriorityObservers = FillObservers(walk);
        CheckNodeIds(walk, context);

        if (context.ErrorCount > 0)
            return null;
        var definition = new BehaviorTreeDefinition(asset, layout, walk.Mains.ToArray(), walk.Auxiliaries.ToArray(), lowerPriorityObservers, slots, walk.MaxDepthSeen);
        for (int i = 0; i < walk.Mains.Count; i++)
            walk.Mains[i].IsCompiled = true;
        for (int i = 0; i < walk.Auxiliaries.Count; i++)
            walk.Auxiliaries[i].IsCompiled = true;
        return definition;
    }

    #region 설정 · 키
    private static void CheckSettings(BehaviorTreeSettings settings, BehaviorCompileContext context)
    {
        if (settings == null)
        {
            context.Add(EBehaviorMessageSeverity.Error, "", null, "settings", "없음 (null)");
            return;
        }
        CheckSetting(settings.TickInterval, 0f, "tickInterval", context);
        CheckSetting(settings.NearDistance, 0f, "nearDistance", context);
        CheckSetting(settings.FarIntervalMultiplier, 1f, "farIntervalMultiplier", context);
        CheckSetting(settings.ActivationDistance, 0f, "activationDistance", context);
        if (settings.ActivationDistance > 0f && settings.NearDistance > settings.ActivationDistance)
            context.Add(EBehaviorMessageSeverity.Warning, "settings", null, "nearDistance", $"activationDistance ({settings.ActivationDistance})보다 큼 ({settings.NearDistance}) — 먼 간격은 쓰이지 않는다");
    }

    /// <summary>NaN · 무한이 아니고 min 이상</summary>
    private static void CheckSetting(float value, float min, string field, BehaviorCompileContext context)
    {
        if (value >= min && float.IsInfinity(value) == false)
            return;
        context.Add(EBehaviorMessageSeverity.Error, "settings", null, field, $"{min} 이상의 유한한 수여야 한다 ({value})");
    }

    /// <summary>키 검사 → 배치. 에러가 있으면 null</summary>
    private static BehaviorBlackboardLayout BuildLayout(BehaviorBlackboardKey[] keys, BehaviorCompileContext context)
    {
        if (keys == null)
        {
            context.Add(EBehaviorMessageSeverity.Error, "", null, "blackboard", "없음 (null)");
            return null;
        }
        int errorStart = context.ErrorCount;
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < keys.Length; i++)
        {
            BehaviorBlackboardKey key = keys[i];
            string path = $"blackboard[{i}]";
            if (key == null)
            {
                context.Add(EBehaviorMessageSeverity.Error, path, null, "", "빈 칸 (null)");
                continue;
            }
            if (string.IsNullOrEmpty(key.Name) || key.Name.Trim() != key.Name)
                context.Add(EBehaviorMessageSeverity.Error, path, null, "name", $"비었거나 앞뒤에 공백이 있음 ('{key.Name}')");
            else if (names.Add(key.Name) == false)
                context.Add(EBehaviorMessageSeverity.Error, path, null, "name", $"'{key.Name}'가 겹침");
            if (Enum.IsDefined(typeof(EBehaviorKeyType), key.Type) == false)
                context.Add(EBehaviorMessageSeverity.Error, path, null, "type", $"모르는 형식 {(int)key.Type}");
            if (key.HasSeed && key.Type != EBehaviorKeyType.Int)
                context.Add(EBehaviorMessageSeverity.Error, path, null, "seed", $"처음 값 출처는 Int 키만 쓸 수 있다 ('{key.Name}'는 {key.Type})");
            if (key.Type == EBehaviorKeyType.Float && IsFinite(key.DefaultFloat) == false)
                context.Add(EBehaviorMessageSeverity.Error, path, null, "default", $"유한한 수여야 한다 ({key.DefaultFloat})");
            if (key.Type == EBehaviorKeyType.Vector2 && (IsFinite(key.DefaultVector2.x) == false || IsFinite(key.DefaultVector2.y) == false))
                context.Add(EBehaviorMessageSeverity.Error, path, null, "default", $"유한한 수여야 한다 ({key.DefaultVector2.x}, {key.DefaultVector2.y})");
        }
        return context.ErrorCount == errorStart ? new BehaviorBlackboardLayout(keys) : null;
    }

    private static bool IsFinite(float value)
    {
        return float.IsNaN(value) == false && float.IsInfinity(value) == false;
    }
    #endregion

    #region 구조
    /// <summary>깊이 우선으로 Main 노드 (실행 순서)와 보조 노드를 모은다 — 노드, 그 Decorator · Service, 그다음 자식들. 모은 노드에만 결과를 적는다</summary>
    private sealed class Walk
    {
        public readonly List<BehaviorMainNode> Mains = new List<BehaviorMainNode>();
        public readonly List<string> MainPaths = new List<string>();
        public readonly List<BehaviorAuxiliaryNode> Auxiliaries = new List<BehaviorAuxiliaryNode>();
        public readonly List<string> AuxiliaryPaths = new List<string>();
        public int MaxDepthSeen;

        private readonly BehaviorNodeRegistry _registry;
        private readonly BehaviorCompileContext _context;
        private readonly HashSet<BehaviorNode> _seen = new HashSet<BehaviorNode>();
        private bool _tooMany;

        public Walk(BehaviorNodeRegistry registry, BehaviorCompileContext context)
        {
            _registry = registry;
            _context = context;
        }

        public void Visit(BehaviorMainNode node, BehaviorComposite parent, int ordinal, int depth, string path)
        {
            if (_tooMany)
                return;
            path = $"{path} ({KeyOf(_registry, node)})";
            if (Refuse(node, path))
                return;
            if (depth > MaxDepth)
            {
                Error(path, node, $"깊이가 {MaxDepth}를 넘음");
                return;
            }
            if (Mains.Count >= MaxMainNodes)
            {
                Error(path, node, $"Main 노드가 {MaxMainNodes}개를 넘음");
                _tooMany = true;
                return;
            }

            node.Index = Mains.Count;
            node.ParentIndex = parent != null ? parent.Index : -1;
            node.ChildOrdinal = ordinal;
            node.Depth = depth;
            if (depth > MaxDepthSeen)
                MaxDepthSeen = depth;
            Mains.Add(node);
            MainPaths.Add(path);

            AddAuxiliaries(node, node.Decorators, path, "decorators");
            AddAuxiliaries(node, node.Services, path, "services");

            if (node is BehaviorComposite composite)
            {
                BehaviorMainNode[] children = composite.Children;
                if (children == null || children.Length == 0)
                    Error(path, node, "자식이 없음 — Composite는 자식이 하나 이상이어야 한다");
                else
                {
                    for (int i = 0; i < children.Length; i++)
                    {
                        string childPath = $"{path}.children[{i}]";
                        if (children[i] == null)
                            Error(childPath, null, "빈 칸 (null)");
                        else
                            Visit(children[i], composite, i, depth + 1, childPath);
                    }
                }
            }
            node.LastDescendantIndex = Mains.Count - 1;
        }

        private void AddAuxiliaries<T>(BehaviorMainNode owner, T[] nodes, string ownerPath, string field) where T : BehaviorAuxiliaryNode
        {
            if (nodes == null)
            {
                _context.Add(EBehaviorMessageSeverity.Error, ownerPath, owner, field, "없음 (null)");
                return;
            }
            for (int i = 0; i < nodes.Length; i++)
            {
                T node = nodes[i];
                string path = $"{ownerPath}.{field}[{i}]";
                if (node == null)
                {
                    Error(path, null, "빈 칸 (null)");
                    continue;
                }
                path = $"{path} ({KeyOf(_registry, node)})";
                if (Refuse(node, path))
                    continue;
                node.Index = Auxiliaries.Count;
                node.OwnerIndex = owner.Index;
                Auxiliaries.Add(node);
                AuxiliaryPaths.Add(path);
            }
        }

        /// <summary>같은 노드 객체가 두 번째 · 이미 다른 정의에 컴파일된 노드 — 결과를 적지 않는다</summary>
        private bool Refuse(BehaviorNode node, string path)
        {
            if (_seen.Add(node) == false)
            {
                Error(path, node, "같은 노드 객체가 트리의 두 곳에 있음");
                return true;
            }
            if (node.IsCompiled)
            {
                Error(path, node, "이미 컴파일된 노드 — 노드 객체는 정의 하나에만 컴파일한다 (다시 컴파일은 JSON에서 새로 읽은 데이터로)");
                return true;
            }
            return false;
        }

        private void Error(string path, BehaviorNode node, string text)
        {
            _context.Add(EBehaviorMessageSeverity.Error, path, node, "", text);
        }
    }

    /// <summary>노드가 밝힌 칸 수 — 범위 밖 · 예외면 에러를 남기고 0</summary>
    private static int SlotCountOf(BehaviorNode node, BehaviorCompileContext context)
    {
        int count;
        try
        {
            count = node.MemorySlotCount;
        }
        catch (Exception exception)
        {
            context.Error($"칸 수 (MemorySlotCount)를 읽다 예외 {exception.GetType().Name}: {exception.Message}");
            return 0;
        }
        if (count < 0 || count > MaxNodeMemorySlots)
        {
            context.Error($"칸 수 (MemorySlotCount)는 0~{MaxNodeMemorySlots}여야 한다 ({count})");
            return 0;
        }
        return count;
    }

    /// <summary>칸 자리 — Main 노드 차례로, 노드 다음에 그 Decorator · Service (한 노드를 볼 때 칸이 가깝게). 칸 수 합을 반환 (상한을 넘으면 에러)</summary>
    private static int AssignMemory(Walk walk, int[] mainSlots, int[] auxiliarySlots, BehaviorCompileContext context)
    {
        int offset = 0;
        int auxiliary = 0;
        for (int i = 0; i < walk.Mains.Count; i++)
        {
            offset = Place(walk.Mains[i], offset, mainSlots[i]);
            while (auxiliary < walk.Auxiliaries.Count && walk.Auxiliaries[auxiliary].OwnerIndex == i)
            {
                offset = Place(walk.Auxiliaries[auxiliary], offset, auxiliarySlots[auxiliary]);
                auxiliary++;
            }
        }
        if (offset > MaxTreeMemorySlots)
            context.Add(EBehaviorMessageSeverity.Error, "", null, "", $"노드 칸 수 합이 {MaxTreeMemorySlots}를 넘음 ({offset})");
        return offset;
    }

    private static int Place(BehaviorNode node, int offset, int slotCount)
    {
        node.MemoryOffset = offset;
        node.OwnMemoryOffset = offset + node.SystemSlotCount;
        return node.OwnMemoryOffset + slotCount;
    }
    #endregion

    #region 노드 검사
    private static void CompileNode(BehaviorNode node, BehaviorCompileContext context)
    {
        try
        {
            node.Compile(context);
        }
        catch (Exception exception) // 노드 코드의 예외도 그 트리의 컴파일 실패로 알린다
        {
            context.Error($"컴파일 중 예외 {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>Decorator 공통 — Observer Aborts가 정의된 값 · 이 Decorator가 쓸 수 있는 값 · 그 자리에서 쓸 수 있는 값인가, 결과 뒤집기를 쓸 수 있는가</summary>
    private static void CheckDecorator(BehaviorDecorator decorator, BehaviorMainNode owner, Walk walk, BehaviorCompileContext context)
    {
        EBehaviorAbortMode mode = decorator.AbortMode;
        try
        {
            if (Enum.IsDefined(typeof(EBehaviorAbortMode), mode) == false)
                context.Error("abort", $"모르는 값 {(int)mode}");
            else
            {
                if (decorator.AllowsAbortMode(mode) == false)
                    context.Error("abort", $"이 Decorator는 {mode}를 쓸 수 없다");
                if (HasLowerPriority(mode))
                {
                    if (owner.ParentIndex < 0)
                        context.Error("abort", $"루트에는 {mode}를 쓸 수 없다 — 더 낮은 우선순위가 없음");
                    else if (walk.Mains[owner.ParentIndex] is BehaviorComposite parent && parent.CanAbortLowerPriority == false)
                        context.Error("abort", $"부모 Composite가 자식의 LowerPriority를 받지 않는다 ({mode}) — 뒤 자식은 다음 단계라 Self · None만");
                }
            }
            if (decorator.InverseCondition && decorator.AllowsInverse == false)
                context.Error("inverse", "이 Decorator는 결과 뒤집기를 쓸 수 없다");
        }
        catch (Exception exception)
        {
            context.Error($"검사 중 예외 {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>Service 공통 — 간격은 0보다 큰 유한한 수, 편차는 0 이상 · 간격보다 작다</summary>
    private static void CheckService(BehaviorService service, BehaviorCompileContext context)
    {
        if ((service.Interval > 0f) == false || float.IsInfinity(service.Interval))
            context.Error("interval", $"0보다 큰 유한한 수여야 한다 ({service.Interval})");
        else if ((service.RandomDeviation >= 0f) == false || service.RandomDeviation >= service.Interval)
            context.Error("randomDeviation", $"0 이상, interval ({service.Interval})보다 작아야 한다 ({service.RandomDeviation})");
    }

    /// <summary>노드마다 Self · LowerPriority로 지켜보는 Decorator가 있는가, LowerPriority로 지켜보는 노드의 실행 순서 목록 (작은 것부터)</summary>
    private static int[] FillObservers(Walk walk)
    {
        var lowerPriority = new List<int>();
        for (int i = 0; i < walk.Mains.Count; i++)
        {
            BehaviorMainNode main = walk.Mains[i];
            bool self = false;
            bool lower = false;
            BehaviorDecorator[] decorators = main.Decorators;
            if (decorators != null)
            {
                for (int index = 0; index < decorators.Length; index++)
                {
                    if (decorators[index] == null)
                        continue;
                    self |= HasSelf(decorators[index].AbortMode);
                    lower |= HasLowerPriority(decorators[index].AbortMode);
                }
            }
            main.HasSelfObservers = self;
            main.HasLowerPriorityObservers = lower;
            if (lower)
                lowerPriority.Add(i);
        }
        return lowerPriority.Count > 0 ? lowerPriority.ToArray() : Array.Empty<int>();
    }

    /// <summary>노드 이름표 (id)가 겹치면 에러 — 배치 파일 · 디버거가 노드를 id로 찾는다 (빈 id는 괜찮다)</summary>
    private static void CheckNodeIds(Walk walk, BehaviorCompileContext context)
    {
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < walk.Mains.Count; i++)
            CheckNodeId(walk.Mains[i], walk.MainPaths[i], ids, context);
        for (int i = 0; i < walk.Auxiliaries.Count; i++)
            CheckNodeId(walk.Auxiliaries[i], walk.AuxiliaryPaths[i], ids, context);
    }

    private static void CheckNodeId(BehaviorNode node, string path, Dictionary<string, string> ids, BehaviorCompileContext context)
    {
        if (string.IsNullOrEmpty(node.NodeId))
            return;
        if (ids.TryGetValue(node.NodeId, out string first))
            context.Add(EBehaviorMessageSeverity.Error, path, node, "id", $"'{node.NodeId}'가 {first}와 겹침");
        else
            ids.Add(node.NodeId, path);
    }
    #endregion

    /// <summary>문장에 쓰는 노드 이름 — 등록 키, 없으면 클래스 이름</summary>
    private static string KeyOf(BehaviorNodeRegistry registry, BehaviorNode node)
    {
        if (registry != null && registry.TryGetKey(node.GetType(), out string key))
            return key;
        return node.GetType().Name;
    }
}