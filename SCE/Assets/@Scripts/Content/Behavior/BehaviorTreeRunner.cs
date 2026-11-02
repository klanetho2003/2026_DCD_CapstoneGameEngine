using System;
using System.Diagnostics;

/// <summary>
/// 몬스터 하나의 트리 실행기 — 공유 정의와 몬스터마다의 칸 (노드 칸 · Blackboard)을 묶는다.
/// 노드는 실행기로 몸 (Agent) · 바깥 (World) · 시각 (Now) · 자기 칸 (Memory)을 읽는다 — Managers · Time을 직접 읽지 않는다.
/// 같은 정의로 다시 묶으면 배열을 그대로 쓴다 (풀에서 다시 쓰는 몬스터는 할당 없음).
/// </summary>
public sealed partial class BehaviorTreeRunner
{
    private static readonly BehaviorNodeMemory[] s_noMemory = Array.Empty<BehaviorNodeMemory>();

    private readonly BehaviorBlackboard _blackboard = new BehaviorBlackboard();
    private BehaviorTreeDefinition _definition;
    private BehaviorNodeMemory[] _memory = s_noMemory;

    public BehaviorTreeRunner(IBehaviorAgent agent, IBehaviorWorld world)
    {
        Agent = agent;
        World = world;
    }

    public IBehaviorAgent Agent { get; }
    public IBehaviorWorld World { get; }
    public BehaviorTreeDefinition Definition => _definition;
    public BehaviorBlackboard Blackboard => _blackboard;

    /// <summary>지금 평가의 시각 (초) — 평가를 부르는 쪽이 넘긴다</summary>
    public float Now { get; private set; }

    /// <summary>정의를 묶고 처음 상태로 — 노드 칸 0, Blackboard 처음 값. 같은 정의면 배열을 그대로 쓴다. O(칸 수 + 키 수)</summary>
    public void Bind(BehaviorTreeDefinition definition)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));
        ResetExecution();
        if (ReferenceEquals(definition, _definition) == false)
        {
            _definition = definition;
            if (_memory.Length != definition.MemorySlotCount)
                _memory = definition.MemorySlotCount > 0 ? new BehaviorNodeMemory[definition.MemorySlotCount] : s_noMemory;
            _blackboard.Bind(definition.Blackboard);
        }
        Array.Clear(_memory, 0, _memory.Length);
        _blackboard.ResetToDefaults(Agent);
    }

    /// <summary>정의를 푼다 — 칸 · 참조를 놓는다 (트리 없는 몬스터)</summary>
    public void Unbind()
    {
        ResetExecution();
        _definition = null;
        _memory = s_noMemory;
        _blackboard.Bind(null);
    }

    /// <summary>노드 자기 칸 (slot번째, 0부터 MemorySlotCount 전까지) — 노드가 실행 중 상태를 읽고 쓴다. O(1)</summary>
    public ref BehaviorNodeMemory Memory(BehaviorNode node, int slot = 0)
    {
        CheckSlot(node, slot);
        return ref _memory[node.OwnMemoryOffset + slot];
    }

    /// <summary>실행기가 이 노드에 쓰는 칸 (Composite · Decorator · Service)</summary>
    internal ref BehaviorNodeMemory SystemMemory(BehaviorNode node)
    {
        CheckSystemSlot(node);
        return ref _memory[node.MemoryOffset];
    }

    /// <summary>실행 상태를 처음으로 — 진행 중 노드의 끝 처리 (OnEnd 등) 없이. 실행 부분이 정의한다</summary>
    partial void ResetExecution();

    /// <summary>노드가 밝힌 칸 수 밖을 쓰면 예외 — 편집기 · 개발 빌드만 (다른 노드의 칸을 조용히 덮지 않게)</summary>
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    private static void CheckSlot(BehaviorNode node, int slot)
    {
        if ((uint)slot >= (uint)node.MemorySlotCount)
            throw new ArgumentOutOfRangeException(nameof(slot), $"{node.GetType().Name}의 칸은 {node.MemorySlotCount}개 — {slot}번 칸 없음");
    }

    /// <summary>실행기 칸이 없는 노드 (Task)면 예외 — 편집기 · 개발 빌드만</summary>
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    private static void CheckSystemSlot(BehaviorNode node)
    {
        if (node.SystemSlotCount == 0)
            throw new InvalidOperationException($"{node.GetType().Name}에는 실행기 칸이 없음");
    }
}