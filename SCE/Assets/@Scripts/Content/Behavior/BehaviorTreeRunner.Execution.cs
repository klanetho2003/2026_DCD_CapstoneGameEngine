using System;
using UnityEngine;

/// <summary>
/// 실행 — 몸이 매 프레임 Tick(now)을 부르면 평가 간격마다 진행 경로의 Service · 지켜보는 조건 · 진행 중 Task를 갱신하고,
/// Task가 끝나면 부모가 다음 자식을 고르는 검색을 이어 간다 (Unreal Behavior Tree의 규칙).
///  - 진행 경로 = 루트부터 진행 중 Task까지의 Main 노드. 경로의 노드만 Service · Self 조건을 갱신한다.
///  - 조건이 바뀌면 요청: Self = 그 노드가 실패로 끝나고 부모가 이어 감, LowerPriority = 그 노드부터 다시 검색 — 시작할 Task를 찾았을 때만 끊는다.
///    요청은 가장 높은 우선순위 (작은 실행 순서) 하나만 남고, 끊기가 먼저 새 시작이 뒤다.
///  - 끊기를 미루는 Task (OnAbort Pending)는 OnAbortUpdate가 Done이 될 때까지 기다리고, 그동안 새 행동을 시작하지 않는다.
///  - 행동 잠금 (스킬 · 기절 · 강제 이동) 중에는 요청 · 검색을 미루고, UpdateWhileActionLocked Task만 갱신한다.
///  - 평가 중 예외는 이 몬스터의 트리만 멈추고 (고장) 한 번 로그한다. 평가 중 할당 없음.
/// 실행기 칸 (SystemMemory): Composite Int0 = 진행 중 자식 차례 + 1 · Int1 = 이어 갈 표시, Decorator Int0 = 마지막 조건 결과 + 지켜보기 등록 · Int1 = 본 키 판 번호,
/// Service Float0 = 다음 갱신 시각 · Int0 = 경로에 있음.
/// </summary>
public sealed partial class BehaviorTreeRunner
{
    /// <summary>한 평가에서 검색이 방문하는 노드 수 상한 — 끝나지 않는 반복 (무한 Loop + 바로 끝나는 Task 등)은 여기서 끊고 다음 평가에 이어 간다</summary>
    public const int MaxNodeVisitsPerEvaluation = 1024;

    private enum ERequestKind : byte
    {
        None,
        Fail,   // 그 노드가 실패로 끝남 — 부모가 이어 감 (Self)
        Search, // 그 노드부터 다시 검색 (LowerPriority · 키 값 바뀜)
    }

    private const int ResultMask = 3;    // Decorator 칸 Int0 — 마지막 조건 결과 (0 모름 · 1 거짓 · 2 참)
    private const int ResultFalse = 1;
    private const int ResultTrue = 2;
    private const int ObservingFlag = 4; // Decorator 칸 Int0 — LowerPriority로 지켜보는 중 (검색이 지나간 뒤)
    private const int ReenterMark = -1;  // Composite 칸 Int1 — Int0의 자식에 다시 들어간다 (반복을 미룸)

    private readonly int[] _path = new int[BehaviorTreeCompiler.MaxDepth + 1];
    private readonly int[] _chain = new int[BehaviorTreeCompiler.MaxDepth + 1];
    private int _pathLength;
    private int _activeTask = -1;
    private bool _aborting;
    private int _requestNode = -1;
    private ERequestKind _requestKind;
    private float _nextEvaluationTime;
    private bool _evaluationRequested;
    private bool _staggerPending;
    private bool _stopped;
    private bool _faulted;
    private bool _wasActionLocked;
    private bool _stopMoveRequested;
    private bool _moveIssued;
    private bool _visitCapWarned;
    private bool _rootRepeatPending;
    private int _visits;
    private uint _randomState = 1;

    #region 읽기
    /// <summary>Stop으로 멈춤 — Tick이 아무것도 하지 않는다 (Start · Bind로 다시)</summary>
    public bool IsStopped => _stopped;

    /// <summary>평가 중 예외로 멈춤 — Bind로만 다시</summary>
    public bool IsFaulted => _faulted;

    /// <summary>진행 중 Task의 끊기를 미루는 중</summary>
    public bool IsAborting => _aborting;

    /// <summary>진행 중 Task의 실행 순서 — 없으면 -1 (검색 중 · 트리 끝 · 끊기 끝)</summary>
    public int ActiveTaskIndex => _activeTask;

    /// <summary>진행 경로의 길이 (루트 포함) — 0이면 다음 평가에 루트부터 시작한다</summary>
    public int PathLength => _pathLength;

    /// <summary>진행 경로 depth번째 노드의 실행 순서 (0 = 루트)</summary>
    public int GetPathNodeIndex(int depth)
    {
        if ((uint)depth >= (uint)_pathLength)
            throw new ArgumentOutOfRangeException(nameof(depth));
        return _path[depth];
    }

    /// <summary>처리를 기다리는 요청의 노드 실행 순서 — 없으면 -1 (끊기를 미루는 중 · 잠금 중에 남는다)</summary>
    public int PendingRequestNode => _requestNode;

    /// <summary>묶은 뒤 평가한 횟수</summary>
    public int EvaluationCount { get; private set; }

    /// <summary>다음 평가 시각</summary>
    public float NextEvaluationTime => _nextEvaluationTime;

    /// <summary>지금 들어가는 노드가 반복 (ShouldRepeat)으로 다시 들어가는 것인가 — Decorator의 OnNodeActivation에서 읽는다 (처음 들어올 때만 횟수를 정하는 Loop 등)</summary>
    public bool IsRepeatEntry { get; private set; }
    #endregion

    #region 제어 — 몸이 부른다
    /// <summary>
    /// 매 프레임. 때가 되면 (평가 간격 · 거리 LOD · 요청) 평가한다. 매 프레임 갱신 Task는 프레임마다 갱신하고 끝나면 바로 다음 행동을 찾는다.
    /// 평가 중 예외 → 고장 (Debug.LogException 한 번, 이 몬스터의 트리만 멈춤). O(경로 길이 + 지켜보는 조건 수 + 검색한 노드 수)
    /// </summary>
    public void Tick(float now)
    {
        if (_definition == null || _stopped || _faulted)
            return;
        Now = now;
        _stopMoveRequested = false;
        _moveIssued = false;
        try
        {
            bool active = ComputeInterval(out float interval);
            if (_staggerPending)
            {
                _staggerPending = false;
                _nextEvaluationTime = now + NextRandom() * interval;
            }
            bool locked = Agent.IsActionLocked;
            if (locked && _wasActionLocked == false)
                Agent.StopMoving();
            _wasActionLocked = locked;
            if (active)
            {
                UpdateFrameTask(locked);
                if (_evaluationRequested || now >= _nextEvaluationTime)
                {
                    Evaluate(locked, interval);
                    DebugEvaluated();
                }
            }
            if (_stopMoveRequested && _moveIssued == false)
                Agent.StopMoving();
        }
        catch (Exception exception)
        {
            Fault(exception);
        }
    }

    /// <summary>
    /// 실행을 멈춘다 (재우기 · 죽음) — 진행 중 Task의 OnAbort · OnEnd 없이 (몸의 정리가 몸 상태를 맡는다), 경로의 Decorator · Service만 조용히 나간다.
    /// 노드 칸 · Blackboard · 고장은 그대로, Start로 루트부터 다시
    /// </summary>
    public void Stop()
    {
        if (_faulted == false)
            LeaveQuietly();
        ResetState();
        _stopped = true;
    }

    /// <summary>Stop으로 멈춘 실행을 다시 (깨우기) — 다음 Tick부터, 첫 평가는 흩뜨린다</summary>
    public void Start()
    {
        _stopped = false;
    }

    /// <summary>다음 Tick에 평가한다 (피격 등 — 간격을 기다리지 않는다)</summary>
    public void RequestEvaluation()
    {
        _evaluationRequested = true;
    }
    #endregion

    #region 노드가 부른다
    /// <summary>
    /// 이 Tick이 끝날 때 이동을 멈춘다 — 같은 Tick에 새 이동 (MarkMoveIssued)이 나왔으면 멈추지 않는다
    /// (StopMoving이 다시 찾기 간격 · 닿을 수 없음 기억을 지우므로). 이동 Task의 OnEnd가 부른다
    /// </summary>
    public void RequestStopMoving()
    {
        _stopMoveRequested = true;
    }

    /// <summary>이 Tick에 새 이동을 시작했다 — 이동 Task의 OnStart가 부른다</summary>
    public void MarkMoveIssued()
    {
        _moveIssued = true;
    }

    /// <summary>이 몬스터의 무작위 수 [0, 1) — 스폰 번호로 씨를 정한 xorshift. 할당 없음</summary>
    public float NextRandom()
    {
        uint state = _randomState;
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        _randomState = state;
        return (state & 0xFFFFFF) * (1f / 16777216f);
    }
    #endregion

    #region 평가
    /// <summary>매 프레임 갱신 Task — 진행 중이면 OnUpdate, 끊는 중이면 OnAbortUpdate. 끝나면 이 프레임에 평가한다</summary>
    private void UpdateFrameTask(bool locked)
    {
        if (_activeTask < 0)
            return;
        var task = (BehaviorTask)_definition.Mains[_activeTask];
        if (task.UpdateEveryFrame == false || (locked && task.UpdateWhileActionLocked == false))
            return;
        _visits = 0;
        if (_aborting)
        {
            if (task.OnAbortUpdate(this) == EBehaviorAbortResult.Done)
            {
                CompleteLatentAbort(locked);
                if (_activeTask < 0)
                    _evaluationRequested = true;
            }
            return;
        }
        EBehaviorStatus status = task.OnUpdate(this);
        if (status == EBehaviorStatus.Running)
            return;
        task.OnEnd(this, status, false);
        int index = _activeTask;
        _activeTask = -1;
        if (FinishNode(index, status, locked, out int previousOrdinal, out EBehaviorStatus previousResult))
            StoreContinuation(ref TopMemory(), previousOrdinal, previousResult);
        if (_activeTask < 0)
            _evaluationRequested = true;
    }

    /// <summary>
    /// 평가 하나: 다음 시각 → (경로 비었으면 루트부터) → 경로의 Service → 지켜보는 조건 → 요청 (끊기 · 검색) 또는 진행 중 Task 갱신 → 끝나면 검색.
    /// 잠금 중에는 요청 · 검색을 미루고 UpdateWhileActionLocked Task만 갱신한다
    /// </summary>
    private void Evaluate(bool locked, float interval)
    {
        EvaluationCount++;
        _evaluationRequested = false;
        _visits = 0;
        _nextEvaluationTime = Now + interval;
        if (_pathLength == 0)
        {
            if (locked == false)
                StartTree();
            return;
        }
        TickServices();
        CollectRequests();
        if (_activeTask >= 0)
        {
            var task = (BehaviorTask)_definition.Mains[_activeTask];
            if (_aborting)
            {
                if (task.UpdateEveryFrame || (locked && task.UpdateWhileActionLocked == false))
                    return;
                if (task.OnAbortUpdate(this) == EBehaviorAbortResult.Done)
                    CompleteLatentAbort(locked);
                return;
            }
            if (_requestNode >= 0 && locked == false && ProcessRequest())
                return; // 끊기가 먼저 — 이 평가에서 Task는 갱신하지 않는다
            if (task.UpdateEveryFrame || (locked && task.UpdateWhileActionLocked == false))
                return;
            EBehaviorStatus status = task.OnUpdate(this);
            if (status == EBehaviorStatus.Running)
                return;
            task.OnEnd(this, status, false);
            int index = _activeTask;
            _activeTask = -1;
            if (FinishNode(index, status, locked, out int previousOrdinal, out EBehaviorStatus previousResult))
            {
                if (locked)
                    StoreContinuation(ref TopMemory(), previousOrdinal, previousResult);
                else
                    Search(-1, previousOrdinal, previousResult);
            }
            return;
        }
        if (locked)
            return;
        if (_requestNode >= 0 && ProcessRequest())
            return;
        if (_pathLength > 0 && _activeTask < 0)
            ResumeSearch();
    }

    /// <summary>평가 간격 — 거리 LOD (트리 설정). 활성 거리 밖이면 false (평가하지 않는다)</summary>
    private bool ComputeInterval(out float interval)
    {
        BehaviorTreeSettings settings = _definition.Settings;
        interval = settings.TickInterval;
        if (settings.NearDistance <= 0f && settings.ActivationDistance <= 0f)
            return true;
        CreatureBase target = World.PossessedTarget;
        if (target == null || target.IsValid() == false)
        {
            if (settings.NearDistance > 0f)
                interval *= settings.FarIntervalMultiplier;
            return true;
        }
        Vector2 offset = (Vector2)target.transform.position - Agent.Position;
        float squared = offset.sqrMagnitude;
        if (settings.ActivationDistance > 0f && squared > settings.ActivationDistance * settings.ActivationDistance)
            return false;
        if (settings.NearDistance > 0f && squared > settings.NearDistance * settings.NearDistance)
            interval *= settings.FarIntervalMultiplier;
        return true;
    }

    /// <summary>루트부터 — 지켜보기 등록을 모두 지우고 루트에 들어가 검색한다. 루트의 조건이 거짓이면 다음 평가에 다시</summary>
    private void StartTree()
    {
        _requestNode = -1;
        bool repeatEntry = _rootRepeatPending;
        _rootRepeatPending = false;
        ClearObservers(0);
        if (EnterNode(0, repeatEntry) == false)
            return;
        Search(-1, -1, EBehaviorStatus.Success);
    }

    /// <summary>경로의 Service 가운데 때가 된 것 — 루트부터. 밀린 것은 한 번만 갱신하고 다음 시각을 잡는다</summary>
    private void TickServices()
    {
        for (int depth = 0; depth < _pathLength; depth++)
        {
            BehaviorService[] services = _definition.Mains[_path[depth]].Services;
            for (int i = 0; i < services.Length; i++)
            {
                BehaviorService service = services[i];
                ref BehaviorNodeMemory memory = ref SystemMemory(service);
                if (memory.Int0 == 0 || Now < memory.Float0)
                    continue;
                service.OnTick(this);
                memory.Float0 = Now + NextInterval(service);
            }
        }
    }

    /// <summary>
    /// 지켜보는 조건 → 요청. 경로 노드의 Self · Both 조건: 거짓이면 Fail, 참인데 다시 시작 키 (RestartKeySlot)가 바뀌었으면 Search.
    /// 검색이 지나간 (등록된) LowerPriority · Both 조건 — 진행 중 노드보다 앞인 것 (경로 위의 조상은 등록되어 있지 않다): 참이 되었거나 참인 채 지켜보는 키가 바뀌었으면 Search
    /// </summary>
    private void CollectRequests()
    {
        int top = _path[_pathLength - 1];
        for (int depth = 0; depth < _pathLength; depth++)
        {
            BehaviorMainNode node = _definition.Mains[_path[depth]];
            if (node.HasSelfObservers == false)
                continue;
            BehaviorDecorator[] decorators = node.Decorators;
            for (int i = 0; i < decorators.Length; i++)
            {
                BehaviorDecorator decorator = decorators[i];
                if (BehaviorTreeCompiler.HasSelf(decorator.AbortMode) == false)
                    continue;
                ref BehaviorNodeMemory memory = ref SystemMemory(decorator);
                bool value = decorator.Evaluate(this);
                bool keyChanged = UpdateKeyVersion(decorator, ref memory);
                if (value == false)
                    Request(ERequestKind.Fail, node.Index);
                else if (keyChanged && decorator.RestartKeySlot >= 0)
                    RequestSearch(node.Index);
                memory.Int0 = (memory.Int0 & ~ResultMask) | (value ? ResultTrue : ResultFalse);
            }
        }

        int[] observers = _definition.LowerPriorityObservers;
        for (int position = 0; position < observers.Length; position++)
        {
            int index = observers[position];
            if (index >= top)
                break;
            BehaviorDecorator[] decorators = _definition.Mains[index].Decorators; // 경로 위의 조상은 들어올 때 등록이 지워져 아래에서 걸러진다
            for (int i = 0; i < decorators.Length; i++)
            {
                BehaviorDecorator decorator = decorators[i];
                if (BehaviorTreeCompiler.HasLowerPriority(decorator.AbortMode) == false)
                    continue;
                ref BehaviorNodeMemory memory = ref SystemMemory(decorator);
                if ((memory.Int0 & ObservingFlag) == 0)
                    continue;
                bool value = decorator.Evaluate(this);
                bool keyChanged = UpdateKeyVersion(decorator, ref memory);
                if (value && ((memory.Int0 & ResultMask) != ResultTrue || keyChanged))
                    RequestSearch(index);
                memory.Int0 = ObservingFlag | (value ? ResultTrue : ResultFalse);
            }
        }
    }

    /// <summary>지켜보는 키의 판 번호를 칸에 적고, 바뀌었으면 true</summary>
    private bool UpdateKeyVersion(BehaviorDecorator decorator, ref BehaviorNodeMemory memory)
    {
        int slot = decorator.ObservedKeySlot;
        if (slot < 0)
            return false;
        int version = _blackboard.GetVersion(slot);
        bool changed = version != memory.Int1;
        memory.Int1 = version;
        return changed;
    }

    /// <summary>
    /// 요청은 하나 — 더 높은 우선순위 (작은 실행 순서)가 남는다. 같은 노드면 Fail이 남는다.
    /// 끊기를 미루는 중에 끊을 범위가 넓어지면 그 범위의 Service도 지금 멈춘다
    /// </summary>
    private void Request(ERequestKind kind, int node)
    {
        if (_requestNode >= 0 && node > _requestNode)
            return;
        if (node == _requestNode && (kind != ERequestKind.Fail || _requestKind == ERequestKind.Fail))
            return;
        _requestNode = node;
        _requestKind = kind;
        if (_aborting)
            CeaseServices(kind == ERequestKind.Fail ? DepthOf(node) : KeepLengthForSearch(node));
    }

    /// <summary>Search 요청 — 문 (사슬의 조건 · 미리 보기)을 지나는 것만 요청으로 센다 (지나지 못한 요청이 더 낮은 Fail · Search를 가리지 않게)</summary>
    private void RequestSearch(int node)
    {
        if (_requestNode >= 0 && node > _requestNode)
            return;
        if (GateSearch(node))
            Request(ERequestKind.Search, node);
    }
    #endregion

    #region 요청 · 끊기
    /// <summary>
    /// 요청 처리. Fail: 그 노드까지 끊고 부모가 Failure로 이어 간다. Search: 문 (사슬의 조건 · 미리 보기 — 잠금으로 미뤄진 요청은 여기서 다시 본다)을 지나면 그 자리까지 끊고 그 노드로 들어간다.
    /// 그 노드 이상의 지켜보기 등록은 지우고 (나가는 노드는 다시 등록된다), 끊기를 미루면 요청을 남겨 두었다가 끝난 뒤 이어 간다.
    /// 문을 지나지 못해 버렸으면 false (진행 중 Task 그대로)
    /// </summary>
    private bool ProcessRequest()
    {
        int node = _requestNode;
        ERequestKind kind = _requestKind;
        _requestNode = -1;
        int keep;
        if (kind == ERequestKind.Fail)
        {
            keep = DepthOf(node);
            if (keep < 0)
                return false; // 이미 끊긴 노드
        }
        else
        {
            if (GateSearch(node) == false)
                return false;
            keep = KeepLengthForSearch(node);
        }
        ClearObservers(node);
        if (AbortDownTo(keep))
        {
            _requestNode = node;
            _requestKind = kind;
            return true;
        }
        ContinueRequest(node, kind);
        return true;
    }

    /// <summary>끊기가 끝난 뒤 (경로는 그 자리까지 줄어 있다) — Fail은 부모가 Failure로, Search는 그 노드로 들어간다 (루트면 처음부터)</summary>
    private void ContinueRequest(int node, ERequestKind kind)
    {
        if (kind == ERequestKind.Fail)
        {
            if (_pathLength > 0)
                ResumeSearch();
            return;
        }
        if (_pathLength == 0)
            StartTree();
        else
            EnterChainAndSearch(node);
    }

    /// <summary>
    /// Search 요청의 문 — 경로의 가장 깊은 조상 아래부터 그 노드까지의 조건이 모두 참이고, 그 노드 부분 트리의 미리 보기 (순수)가 시작할 Task를 찾는가.
    /// 아니면 끊지 않는다 (진행 중 Task 그대로)
    /// </summary>
    private bool GateSearch(int node)
    {
        int depth = DepthOf(node);
        int stop = depth >= 0 ? (depth > 0 ? _path[depth - 1] : -1) : _path[AncestorDepthOf(node)];
        for (int index = node; index != stop; index = _definition.Mains[index].ParentIndex)
        {
            if (DecoratorsPass(index) == false)
                return false;
        }
        BehaviorMainNode main = _definition.Mains[node];
        if (main is BehaviorTask)
            return true;
        return DryRun((BehaviorComposite)main, out _);
    }

    /// <summary>미리 보기 — 들어갔을 때 시작할 Task가 있는가. 조건 Evaluate · NextChild · ModifyResult만 (부수 효과 없음, 반복은 보지 않는다). 방문 상한 안에서</summary>
    private bool DryRun(BehaviorComposite composite, out EBehaviorStatus finishResult)
    {
        int previousOrdinal = -1;
        EBehaviorStatus previousResult = EBehaviorStatus.Success;
        while (true)
        {
            if (++_visits > MaxNodeVisitsPerEvaluation)
            {
                finishResult = EBehaviorStatus.Failure;
                return false;
            }
            int ordinal = composite.NextChild(this, previousOrdinal, previousResult, out finishResult);
            if (ordinal < 0)
                return false;
            BehaviorMainNode child = ChildAt(composite, ordinal);
            if (DecoratorsPass(child.Index) == false)
            {
                previousOrdinal = ordinal;
                previousResult = EBehaviorStatus.Failure;
                continue;
            }
            if (child is BehaviorTask)
                return true;
            if (DryRun((BehaviorComposite)child, out EBehaviorStatus childResult))
                return true;
            previousOrdinal = ordinal;
            previousResult = ModifiedResult(child, childResult);
        }
    }

    /// <summary>Search 요청 뒤 남길 경로 길이 — 노드가 경로 위면 그 깊이 (그 노드부터 다시), 아니면 가장 깊은 조상 다음까지</summary>
    private int KeepLengthForSearch(int node)
    {
        int depth = DepthOf(node);
        return depth >= 0 ? depth : AncestorDepthOf(node) + 1;
    }

    /// <summary>
    /// 경로를 keep 길이까지 끊는다. 진행 중 Task는 OnAbort — Pending이면 미룸 (true, 끊기는 가지의 Service는 지금 멈춤), Done이면 OnEnd(Failure, aborted).
    /// 나가는 노드마다 OnNodeDeactivation(Failure, aborted) · LowerPriority 등록 · Service 멈춤
    /// </summary>
    private bool AbortDownTo(int keep)
    {
        if (_activeTask >= 0)
        {
            var task = (BehaviorTask)_definition.Mains[_activeTask];
            if (task.OnAbort(this) == EBehaviorAbortResult.Pending)
            {
                _aborting = true;
                CeaseServices(keep);
                return true;
            }
            task.OnEnd(this, EBehaviorStatus.Failure, true);
            _activeTask = -1;
        }
        PopTo(keep);
        return false;
    }

    /// <summary>미룬 끊기가 끝남 — OnEnd(Failure, aborted), 요청이 정한 자리까지 나간다. 잠금 중이면 Search 요청은 남겨 두고, Fail은 부모가 이어 갈 표시만 남는다</summary>
    private void CompleteLatentAbort(bool locked)
    {
        var task = (BehaviorTask)_definition.Mains[_activeTask];
        task.OnEnd(this, EBehaviorStatus.Failure, true);
        _activeTask = -1;
        _aborting = false;
        int node = _requestNode;
        ERequestKind kind = _requestKind;
        _requestNode = -1;
        if (node < 0)
        {
            kind = ERequestKind.Fail; // 요청 없이 끊겼다면 그 Task만 나간다
            node = _path[_pathLength - 1];
        }
        int keep = kind == ERequestKind.Fail ? DepthOf(node) : KeepLengthForSearch(node);
        if (keep < 0)
            keep = _pathLength - 1;
        ClearObservers(node);
        PopTo(keep);
        if (locked)
        {
            if (kind == ERequestKind.Search)
            {
                _requestNode = node;
                _requestKind = kind;
            }
            return;
        }
        ContinueRequest(node, kind);
    }

    /// <summary>keep 길이부터 위 (깊은 쪽)의 Service를 멈춘다 — 끊기를 미루는 동안</summary>
    private void CeaseServices(int keep)
    {
        for (int depth = keep; depth < _pathLength; depth++)
        {
            BehaviorService[] services = _definition.Mains[_path[depth]].Services;
            for (int i = 0; i < services.Length; i++)
            {
                ref BehaviorNodeMemory memory = ref SystemMemory(services[i]);
                if (memory.Int0 == 0)
                    continue;
                memory.Int0 = 0;
                services[i].OnCeaseRelevant(this);
            }
        }
    }

    /// <summary>경로를 keep 길이까지 끊긴 것으로 나간다. 남은 끝 Composite에는 '그 자식이 Failure로 끝남' 표시</summary>
    private void PopTo(int keep)
    {
        while (_pathLength > keep)
            PopAndDeactivate(_path[_pathLength - 1], EBehaviorStatus.Failure, true);
        if (_pathLength > 0)
            TopMemory().Int1 = (int)EBehaviorStatus.Failure + 1;
    }

    /// <summary>LowerPriority 지켜보기 등록을 fromIndex 이상의 노드에서 지운다 — 검색이 거기부터 다시 지나가며 정한다</summary>
    private void ClearObservers(int fromIndex)
    {
        int[] observers = _definition.LowerPriorityObservers;
        for (int position = 0; position < observers.Length; position++)
        {
            if (observers[position] < fromIndex)
                continue;
            BehaviorDecorator[] decorators = _definition.Mains[observers[position]].Decorators;
            for (int i = 0; i < decorators.Length; i++)
            {
                if (BehaviorTreeCompiler.HasLowerPriority(decorators[i].AbortMode))
                    SystemMemory(decorators[i]).Int0 &= ~ObservingFlag;
            }
        }
    }
    #endregion

    #region 상태
    /// <summary>묶기 · 풀기 — 경로를 조용히 나가고 실행 상태를 처음으로. 고장도 풀린다</summary>
    partial void ResetExecution()
    {
        LeaveQuietly();
        ResetState();
        _faulted = false;
        _wasActionLocked = false;
    }

    /// <summary>
    /// 경로의 노드를 조용히 나간다 (재우기 · 죽음 · 다시 묶기) — Task의 OnAbort · OnEnd는 부르지 않고 (몸의 정리가 몸 상태를 맡는다),
    /// Decorator의 OnNodeDeactivation(Failure, aborted) · Service의 OnCeaseRelevant · Composite 칸 비우기만 한다 (칸에 남은 '진행 중' 표시가 다음 생애를 막지 않게).
    /// 노드 코드의 예외는 로그만 남기고 경로를 비운다
    /// </summary>
    private void LeaveQuietly()
    {
        if (_definition == null || _pathLength == 0)
            return;
        _activeTask = -1;
        _aborting = false;
        try
        {
            while (_pathLength > 0)
                PopAndDeactivate(_path[_pathLength - 1], EBehaviorStatus.Failure, true);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            _pathLength = 0;
        }
    }

    /// <summary>실행 상태를 처음으로 — 노드 칸 · Blackboard · 고장 · 잠금 기억은 건드리지 않는다. 무작위 씨는 스폰 번호</summary>
    private void ResetState()
    {
        _pathLength = 0;
        _activeTask = -1;
        _aborting = false;
        _requestNode = -1;
        _requestKind = ERequestKind.None;
        _rootRepeatPending = false;
        _nextEvaluationTime = 0f;
        _evaluationRequested = false;
        _staggerPending = true;
        _stopped = false;
        _stopMoveRequested = false;
        _moveIssued = false;
        _visitCapWarned = false;
        _visits = 0;
        IsRepeatEntry = false;
        EvaluationCount = 0;
        uint seed = (uint)(Agent != null ? Agent.SpawnSerial : 0) * 2654435761u + 0x9E3779B9u;
        _randomState = seed != 0 ? seed : 1u;
    }

    /// <summary>평가 중 예외 — 이 몬스터의 트리만 멈춘다 (고장, Bind로만 풀림). 출시 빌드에도 예외를 남긴다</summary>
    private void Fault(Exception exception)
    {
        LogPrinter.LogError($"[BehaviorTreeRunner] 트리 '{_definition.Id}' 평가 중 예외 — 이 몬스터의 트리를 멈춤");
        Debug.LogException(exception);
        ResetState();
        _faulted = true;
    }
    #endregion
}