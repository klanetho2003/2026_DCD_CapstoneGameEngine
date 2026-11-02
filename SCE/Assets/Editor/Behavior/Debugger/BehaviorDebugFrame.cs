using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>디버거가 보이는 Main 노드의 상태</summary>
public enum EBehaviorDebugNodeState : byte
{
    Idle,     // 진행 경로 밖
    OnPath,   // 진행 경로에 있음 (Composite, 들어왔지만 아직 시작 전인 Task)
    Running,  // 진행 중 Task
    Aborting, // 끊기를 미루는 진행 중 Task
}

/// <summary>Blackboard 키 하나의 값 (디버거)</summary>
public struct BehaviorDebugKey
{
    public string Name;
    public EBehaviorKeyType Type;
    public string Value;
    public int Version;
}

/// <summary>크리처 키가 가리키는 쓸 수 있는 대상 (디버거 — Scene 뷰의 대상 점선)</summary>
public struct BehaviorDebugTarget
{
    public string Key;
    public string Name;
    public Vector2 Position;
}

/// <summary>
/// 몬스터 하나의 트리 실행 모습 (한 순간) — 진행 경로 · 진행 중 Task · 기다리는 요청 · Decorator 결과 · Service 다음 갱신 · 노드의 실행 값 · Blackboard 값 · Scene 뷰 모양.
/// Capture가 실행기의 디버거 입구 (BehaviorTreeRunner.Debug*)와 노드의 디버거 함수 (DescribeRuntimeValues · CollectDebugShapes)로 읽어 담는다 — 게임 상태를 바꾸지 않는다.
/// 모양은 진행 경로의 노드 · 그 Decorator · Service와 LowerPriority로 지켜보는 Decorator의 것. 같은 크기의 정의를 다시 담으면 배열을 그대로 쓴다.
/// 노드의 디버거 함수가 던지면 그 노드의 글은 ExceptionText (Console에는 이 모습에서 노드 형식마다 한 번)
/// </summary>
public sealed class BehaviorDebugFrame
{
    public const string ExceptionText = "(디버거 함수 예외 — Console)";

    private readonly HashSet<Type> _reportedTypes = new HashSet<Type>(); // 디버거 함수의 예외를 알린 노드 형식 — 갱신마다 쌓이지 않게

    private readonly StringBuilder _text = new StringBuilder();
    private EBehaviorDebugNodeState[] _mainStates = Array.Empty<EBehaviorDebugNodeState>();
    private string[] _mainTexts = Array.Empty<string>();
    private EBehaviorDebugCondition[] _conditions = Array.Empty<EBehaviorDebugCondition>();
    private bool[] _observing = Array.Empty<bool>();
    private float[] _serviceNext = Array.Empty<float>(); // 다음 갱신까지 (초) — 갱신하지 않는 Service · Decorator는 NaN
    private string[] _auxiliaryTexts = Array.Empty<string>();

    /// <summary>담은 정의 — 담지 않았거나 실행기가 정의를 묶지 않았으면 null</summary>
    public BehaviorTreeDefinition Definition { get; private set; }

    public int EvaluationCount { get; private set; }
    public float Now { get; private set; }
    public bool IsStopped { get; private set; }
    public bool IsFaulted { get; private set; }
    public bool IsAborting { get; private set; }

    /// <summary>진행 중 Task의 실행 순서 — 없으면 -1</summary>
    public int ActiveTask { get; private set; } = -1;

    /// <summary>기다리는 요청의 노드 — 없으면 -1</summary>
    public int RequestNode { get; private set; } = -1;

    public EBehaviorDebugRequest RequestKind { get; private set; }

    /// <summary>몸의 자리 (월드)</summary>
    public Vector2 Position { get; private set; }

    /// <summary>진행 경로 — 루트부터 실행 순서</summary>
    public readonly List<int> Path = new List<int>();

    /// <summary>Blackboard 키 — 정의의 키 차례</summary>
    public readonly List<BehaviorDebugKey> Keys = new List<BehaviorDebugKey>();

    /// <summary>크리처 키가 가리키는 쓸 수 있는 대상</summary>
    public readonly List<BehaviorDebugTarget> Targets = new List<BehaviorDebugTarget>();

    /// <summary>Scene 뷰 모양</summary>
    public readonly BehaviorDebugShapes Shapes = new BehaviorDebugShapes();

    /// <summary>Main 노드의 상태 — 담지 않았거나 밖의 번호면 Idle</summary>
    public EBehaviorDebugNodeState StateOf(int mainIndex)
    {
        return Definition != null && (uint)mainIndex < (uint)_mainStates.Length ? _mainStates[mainIndex] : EBehaviorDebugNodeState.Idle;
    }

    /// <summary>Main 노드의 실행 값 글 — 없으면 빈 글</summary>
    public string MainTextOf(int mainIndex)
    {
        return Definition != null && (uint)mainIndex < (uint)_mainTexts.Length ? _mainTexts[mainIndex] ?? "" : "";
    }

    /// <summary>Decorator의 마지막 조건 결과 — Service · 밖의 번호면 Unknown</summary>
    public EBehaviorDebugCondition ConditionOf(int auxiliaryIndex)
    {
        return Definition != null && (uint)auxiliaryIndex < (uint)_conditions.Length ? _conditions[auxiliaryIndex] : EBehaviorDebugCondition.Unknown;
    }

    /// <summary>Decorator가 LowerPriority로 지켜보는 중인가</summary>
    public bool IsObserving(int auxiliaryIndex)
    {
        return Definition != null && (uint)auxiliaryIndex < (uint)_observing.Length && _observing[auxiliaryIndex];
    }

    /// <summary>Service가 갱신하는 중이면 다음 갱신까지 (초, 0 이상) — 아니면 false</summary>
    public bool TryGetServiceNext(int auxiliaryIndex, out float seconds)
    {
        seconds = Definition != null && (uint)auxiliaryIndex < (uint)_serviceNext.Length ? _serviceNext[auxiliaryIndex] : float.NaN;
        return float.IsNaN(seconds) == false;
    }

    /// <summary>Decorator · Service의 실행 값 글 — 없으면 빈 글</summary>
    public string AuxiliaryTextOf(int auxiliaryIndex)
    {
        return Definition != null && (uint)auxiliaryIndex < (uint)_auxiliaryTexts.Length ? _auxiliaryTexts[auxiliaryIndex] ?? "" : "";
    }

    /// <summary>담은 것을 비운다 (Definition null)</summary>
    public void Clear()
    {
        Definition = null;
        EvaluationCount = 0;
        Now = 0f;
        IsStopped = false;
        IsFaulted = false;
        IsAborting = false;
        ActiveTask = -1;
        RequestNode = -1;
        RequestKind = EBehaviorDebugRequest.None;
        Position = default;
        Path.Clear();
        Keys.Clear();
        Targets.Clear();
        Shapes.Clear();
    }

    /// <summary>
    /// 실행기의 지금 모습을 담는다 (runner가 null이거나 정의를 묶지 않았으면 비운다). 게임 상태를 바꾸지 않는다 — 실행기의 디버거 입구 · Blackboard 엿보기 · 노드의 디버거 함수만 부른다.
    /// O(노드 수 + 키 수) + 노드의 디버거 함수
    /// </summary>
    public void Capture(BehaviorTreeRunner runner)
    {
        BehaviorTreeDefinition definition = runner != null ? runner.Definition : null;
        if (definition == null)
        {
            Clear();
            return;
        }

        Definition = definition;
        Resize(definition.MainNodeCount, definition.AuxiliaryNodeCount);
        EvaluationCount = runner.EvaluationCount;
        Now = runner.Now;
        IsStopped = runner.IsStopped;
        IsFaulted = runner.IsFaulted;
        IsAborting = runner.IsAborting;
        ActiveTask = runner.ActiveTaskIndex;
        RequestNode = runner.PendingRequestNode;
        RequestKind = runner.DebugPendingRequest;
        IBehaviorAgent agent = runner.Agent;
        Position = agent != null ? agent.Position : default;

        Array.Clear(_mainStates, 0, _mainStates.Length);
        Path.Clear();
        for (int depth = 0; depth < runner.PathLength; depth++)
        {
            int index = runner.GetPathNodeIndex(depth);
            Path.Add(index);
            _mainStates[index] = EBehaviorDebugNodeState.OnPath;
        }
        if ((uint)ActiveTask < (uint)_mainStates.Length)
            _mainStates[ActiveTask] = IsAborting ? EBehaviorDebugNodeState.Aborting : EBehaviorDebugNodeState.Running;

        Shapes.Clear();
        for (int i = 0; i < _mainStates.Length; i++)
        {
            BehaviorMainNode node = definition.GetMainNode(i);
            bool active = node is BehaviorTask ? i == ActiveTask : _mainStates[i] != EBehaviorDebugNodeState.Idle;
            _mainTexts[i] = Describe(node, runner, active);
            if (_mainStates[i] != EBehaviorDebugNodeState.Idle)
                Collect(node, runner, active);
        }
        for (int i = 0; i < _auxiliaryTexts.Length; i++)
        {
            BehaviorAuxiliaryNode node = definition.GetAuxiliaryNode(i);
            bool ownerOnPath = StateOf(node.OwnerIndex) != EBehaviorDebugNodeState.Idle;
            _conditions[i] = EBehaviorDebugCondition.Unknown;
            _observing[i] = false;
            _serviceNext[i] = float.NaN;
            if (node is BehaviorDecorator decorator)
            {
                _conditions[i] = runner.DebugConditionOf(decorator);
                _observing[i] = runner.DebugIsObserving(decorator);
            }
            else if (node is BehaviorService service && runner.DebugServiceState(service, out float nextTime))
            {
                _serviceNext[i] = Mathf.Max(0f, nextTime - runner.Now);
            }
            _auxiliaryTexts[i] = Describe(node, runner, ownerOnPath);
            if (ownerOnPath || _observing[i])
                Collect(node, runner, ownerOnPath);
        }

        Keys.Clear();
        Targets.Clear();
        BehaviorBlackboard blackboard = runner.Blackboard;
        BehaviorBlackboardLayout layout = definition.Blackboard;
        for (int slot = 0; slot < layout.KeyCount; slot++)
        {
            BehaviorBlackboardKey key = layout.GetKey(slot);
            Keys.Add(new BehaviorDebugKey { Name = key.Name, Type = key.Type, Value = blackboard.DebugValueText(slot), Version = blackboard.PeekVersion(slot) });
            if (key.Type != EBehaviorKeyType.Creature)
                continue;
            CreatureBase creature = blackboard.PeekCreature(slot, out _);
            if (creature != null)
                Targets.Add(new BehaviorDebugTarget { Key = key.Name, Name = creature.name, Position = creature.transform.position });
        }
    }

    private void Resize(int mainCount, int auxiliaryCount)
    {
        if (_mainStates.Length != mainCount)
        {
            _mainStates = new EBehaviorDebugNodeState[mainCount];
            _mainTexts = new string[mainCount];
        }
        if (_auxiliaryTexts.Length != auxiliaryCount)
        {
            _conditions = new EBehaviorDebugCondition[auxiliaryCount];
            _observing = new bool[auxiliaryCount];
            _serviceNext = new float[auxiliaryCount];
            _auxiliaryTexts = new string[auxiliaryCount];
        }
    }

    /// <summary>노드의 실행 값 글 — 던지면 ExceptionText</summary>
    private string Describe(BehaviorNode node, BehaviorTreeRunner runner, bool active)
    {
        _text.Clear();
        try
        {
            node.DescribeRuntimeValues(runner, active, _text);
        }
        catch (Exception exception)
        {
            Report(node, exception);
            return ExceptionText;
        }
        return _text.Length > 0 ? _text.ToString() : "";
    }

    /// <summary>노드의 Scene 뷰 모양 — 던지면 그 노드의 모양은 건너뛴다 (담은 데까지는 남는다)</summary>
    private void Collect(BehaviorNode node, BehaviorTreeRunner runner, bool active)
    {
        try
        {
            node.CollectDebugShapes(runner, active, Shapes);
        }
        catch (Exception exception)
        {
            Report(node, exception);
        }
    }

    private void Report(BehaviorNode node, Exception exception)
    {
        if (_reportedTypes.Add(node.GetType()))
            Debug.LogException(exception);
    }
}