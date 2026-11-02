using System.Collections.Generic;

/// <summary>
/// 컴파일된 트리 — 읽기 전용. 트리 Id마다 하나를 그 트리를 쓰는 모든 몬스터가 공유한다 (몬스터마다의 상태는 실행기 칸에).
/// Main 노드는 실행 순서 (깊이 우선 — 작을수록 우선순위가 높다)로, 보조 노드는 붙은 노드 차례로 배열에 있다.
/// BehaviorTreeCompiler만 만든다 — 변형 (BehaviorTreeVariants.Build)은 기반 정의의 노드 · 칸 배치 · 설정을 함께 쓰고 Blackboard 배치만 다른 정의 (WithVariant).
/// </summary>
public sealed class BehaviorTreeDefinition
{
    internal readonly BehaviorMainNode[] Mains;
    internal readonly BehaviorAuxiliaryNode[] Auxiliaries;
    internal readonly int[] LowerPriorityObservers;

    internal BehaviorTreeDefinition(BehaviorTreeAsset asset, BehaviorBlackboardLayout blackboard, BehaviorMainNode[] mains, BehaviorAuxiliaryNode[] auxiliaries,
        int[] lowerPriorityObservers, int memorySlotCount, int maxDepth, string variantName = "")
    {
        Asset = asset;
        Blackboard = blackboard;
        Mains = mains;
        Auxiliaries = auxiliaries;
        LowerPriorityObservers = lowerPriorityObservers;
        Root = (BehaviorComposite)mains[0];
        MemorySlotCount = memorySlotCount;
        MaxDepth = maxDepth;
        VariantName = variantName ?? "";
    }

    /// <summary>컴파일한 트리 데이터 — 노드 객체를 함께 쓴다 (편집기 · 디버거가 노드를 찾는다)</summary>
    public BehaviorTreeAsset Asset { get; }

    /// <summary>트리 Id — 변형이면 기반 트리의 Id (노드 · breakpoint · 디버거는 기반 트리로 본다)</summary>
    public string Id => Asset.Id;
    public BehaviorTreeSettings Settings => Asset.Settings;
    public BehaviorBlackboardLayout Blackboard { get; }
    public BehaviorComposite Root { get; }

    /// <summary>변형 이름 — 기반 트리면 "" (몬스터 데이터의 트리 Id "Id/변형 이름"의 뒤)</summary>
    public string VariantName { get; }

    /// <summary>몬스터마다 쓰는 노드 칸 수 (BehaviorNodeMemory, 16바이트씩)</summary>
    public int MemorySlotCount { get; }

    /// <summary>가장 깊은 노드의 깊이 (루트 0)</summary>
    public int MaxDepth { get; }

    public int MainNodeCount => Mains.Length;
    public int AuxiliaryNodeCount => Auxiliaries.Length;

    /// <summary>실행 순서 index의 Main 노드</summary>
    public BehaviorMainNode GetMainNode(int index)
    {
        return Mains[index];
    }

    /// <summary>보조 노드 번호 index의 Decorator · Service</summary>
    public BehaviorAuxiliaryNode GetAuxiliaryNode(int index)
    {
        return Auxiliaries[index];
    }

    /// <summary>LowerPriority · Both로 지켜보는 Main 노드의 실행 순서 (작은 것부터) — 실행기가 몬스터마다의 등록 상태와 함께 본다</summary>
    public IReadOnlyList<int> LowerPriorityObserverIndices => LowerPriorityObservers;

    /// <summary>변형 — 이 정의의 노드 · 칸 배치 · 설정 · 트리 데이터를 함께 쓰고 Blackboard 배치만 blackboard인 정의 (BehaviorTreeVariants.Build만 부른다). O(1)</summary>
    internal BehaviorTreeDefinition WithVariant(BehaviorBlackboardLayout blackboard, string variantName)
    {
        return new BehaviorTreeDefinition(Asset, blackboard, Mains, Auxiliaries, LowerPriorityObservers, MemorySlotCount, MaxDepth, variantName);
    }
}