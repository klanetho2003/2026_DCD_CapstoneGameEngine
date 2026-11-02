using System;

/// <summary>
/// 검색 — 경로 끝 Composite가 다음 자식을 고르고 (NextChild), 자식에 들어가 (조건 · OnNodeActivation · Service) Task를 시작하거나 더 깊이 고른다.
/// 노드가 끝나면 나가고 (ShouldRepeat · ModifyResult · OnNodeDeactivation · 지켜보기 등록 · Service 멈춤) 부모가 이어 간다.
/// 요청으로 특정 노드에 바로 들어갈 때는 조상에서 그 노드까지 사슬을 따라 들어간다. 방문 상한을 넘으면 이어 갈 표시를 남긴다.
/// </summary>
public sealed partial class BehaviorTreeRunner
{
    #region 검색
    /// <summary>
    /// 경로 끝 Composite부터 — stepOrdinal이 0 이상이면 그 자식에 바로 들어가고, 아니면 NextChild(previousOrdinal, previousResult)로 고른다.
    /// 자식에 들어가 (조건) Task면 시작하고 Composite면 이어서 고른다. Composite가 끝나면 부모에게. 루트가 끝나면 경로를 비운다 (다음 평가에 다시).
    /// 방문 상한을 넘으면 이어 갈 표시를 남기고 멈춘다
    /// </summary>
    private void Search(int stepOrdinal, int previousOrdinal, EBehaviorStatus previousResult)
    {
        while (_pathLength > 0)
        {
            var composite = (BehaviorComposite)_definition.Mains[_path[_pathLength - 1]];
            ref BehaviorNodeMemory memory = ref SystemMemory(composite);
            if (++_visits > MaxNodeVisitsPerEvaluation)
            {
                if (stepOrdinal >= 0)
                    StoreReenter(ref memory, stepOrdinal);
                else
                    StoreContinuation(ref memory, previousOrdinal, previousResult);
                WarnVisitCap();
                return;
            }
            int ordinal;
            bool repeatEntry = stepOrdinal >= 0;
            if (repeatEntry)
            {
                ordinal = stepOrdinal;
                stepOrdinal = -1;
            }
            else
            {
                ordinal = composite.NextChild(this, previousOrdinal, previousResult, out EBehaviorStatus finishResult);
                if (ordinal < 0)
                {
                    if (FinishNode(composite.Index, finishResult, false, out previousOrdinal, out previousResult) == false)
                        return;
                    continue;
                }
            }
            BehaviorMainNode child = ChildAt(composite, ordinal);
            memory.Int0 = ordinal + 1;
            memory.Int1 = 0;
            if (EnterNode(child.Index, repeatEntry) == false)
            {
                previousOrdinal = ordinal;
                previousResult = EBehaviorStatus.Failure;
                continue;
            }
            if (child is BehaviorTask task)
            {
                EBehaviorStatus status = StartTask(task);
                if (status == EBehaviorStatus.Running)
                    return;
                if (FinishNode(child.Index, status, false, out previousOrdinal, out previousResult) == false)
                    return;
                continue;
            }
            previousOrdinal = -1;
        }
    }

    /// <summary>
    /// 끝난 노드 (경로 끝)를 나간다 — ShouldRepeat (하나라도) → 반복이 아니면 ModifyResult 차례 → 나가기 (OnNodeDeactivation · 등록 · Service).
    /// 반복이면 다시 들어간다 (조건 다시 · Task는 바로 시작 — 잠금 · 방문 상한이면 표시만 남긴다).
    /// true = 경로 끝 Composite에 (previousOrdinal, previousResult)로 물을 차례 (-1이면 처음부터), false = 끝 (Task 진행 중 · 루트 끝 · 미룸)
    /// </summary>
    private bool FinishNode(int index, EBehaviorStatus result, bool locked, out int previousOrdinal, out EBehaviorStatus previousResult)
    {
        while (true)
        {
            BehaviorMainNode node = _definition.Mains[index];
            BehaviorDecorator[] decorators = node.Decorators;
            bool repeat = false;
            for (int i = 0; i < decorators.Length; i++)
                repeat |= decorators[i].ShouldRepeat(this, result);
            if (repeat == false)
            {
                for (int i = 0; i < decorators.Length; i++)
                    result = decorators[i].ModifyResult(this, result);
            }
            PopAndDeactivate(index, result, false);
            if (repeat == false)
            {
                previousOrdinal = node.ChildOrdinal;
                previousResult = result;
                if (_pathLength == 0)
                    return false;
                TopMemory().Int1 = (int)result + 1;
                return true;
            }
            previousOrdinal = -1;
            previousResult = EBehaviorStatus.Success;
            if (locked || ++_visits > MaxNodeVisitsPerEvaluation)
            {
                if (_pathLength > 0)
                    StoreReenter(ref TopMemory(), node.ChildOrdinal);
                else
                    _rootRepeatPending = true;
                if (locked == false)
                    WarnVisitCap();
                return false;
            }
            if (EnterNode(index, true) == false)
            {
                previousOrdinal = node.ChildOrdinal;
                previousResult = EBehaviorStatus.Failure;
                if (_pathLength == 0)
                    return false;
                TopMemory().Int1 = (int)EBehaviorStatus.Failure + 1;
                return true;
            }
            if (node is BehaviorTask task)
            {
                EBehaviorStatus status = StartTask(task);
                if (status == EBehaviorStatus.Running)
                    return false;
                result = status;
                continue;
            }
            return true;
        }
    }

    /// <summary>경로 끝 Composite에 남긴 표시대로 이어 간다 — 그 자식에 다시 들어가거나 (반복), 그 자식의 결과로 다음을 고르거나, 처음부터</summary>
    private void ResumeSearch()
    {
        ref BehaviorNodeMemory memory = ref TopMemory();
        int ordinal = memory.Int0 - 1;
        if (memory.Int1 == ReenterMark && ordinal >= 0)
            Search(ordinal, -1, EBehaviorStatus.Success);
        else if (memory.Int1 > 0 && ordinal >= 0)
            Search(-1, ordinal, (EBehaviorStatus)(memory.Int1 - 1));
        else
            Search(-1, -1, EBehaviorStatus.Success);
    }

    /// <summary>경로 끝 (조상)에서 node까지 사슬을 따라 들어간다 — 들어가지 못한 자리는 실패로 부모가 이어 간다. node에 들어가면 Task는 시작, Composite는 자식 고르기</summary>
    private void EnterChainAndSearch(int node)
    {
        int stop = _path[_pathLength - 1];
        int count = 0;
        for (int index = node; index != stop; index = _definition.Mains[index].ParentIndex)
            _chain[count++] = index;
        for (int link = count - 1; link >= 0; link--)
        {
            int index = _chain[link];
            BehaviorMainNode main = _definition.Mains[index];
            ref BehaviorNodeMemory memory = ref TopMemory();
            memory.Int0 = main.ChildOrdinal + 1;
            memory.Int1 = 0;
            _visits++;
            if (EnterNode(index) == false)
            {
                Search(-1, main.ChildOrdinal, EBehaviorStatus.Failure);
                return;
            }
            if (main is BehaviorTask task)
            {
                EBehaviorStatus status = StartTask(task);
                if (status == EBehaviorStatus.Running)
                    return;
                if (FinishNode(index, status, false, out int previousOrdinal, out EBehaviorStatus previousResult))
                    Search(-1, previousOrdinal, previousResult);
                return;
            }
        }
        Search(-1, -1, EBehaviorStatus.Success);
    }

    /// <summary>
    /// 노드에 들어간다 — 조건을 모두 평가해 기록하고 (하나라도 거짓이면 들어가지 않고 LowerPriority 조건을 지켜보기에 등록, false),
    /// 경로에 올리고 OnNodeActivation (반복으로 다시 들어오면 IsRepeatEntry), Service는 OnBecomeRelevant (검색 시작 갱신이면 OnTick, 다음 시각은 이어 간다)
    /// </summary>
    private bool EnterNode(int index, bool repeatEntry = false)
    {
        BehaviorMainNode node = _definition.Mains[index];
        BehaviorDecorator[] decorators = node.Decorators;
        bool pass = true;
        for (int i = 0; i < decorators.Length; i++)
        {
            BehaviorDecorator decorator = decorators[i];
            ref BehaviorNodeMemory memory = ref SystemMemory(decorator);
            bool value = decorator.Evaluate(this);
            UpdateKeyVersion(decorator, ref memory);
            memory.Int0 = value ? ResultTrue : ResultFalse;
            pass &= value;
        }
        if (pass == false)
        {
            for (int i = 0; i < decorators.Length; i++)
            {
                if (BehaviorTreeCompiler.HasLowerPriority(decorators[i].AbortMode))
                    SystemMemory(decorators[i]).Int0 |= ObservingFlag;
            }
            return false;
        }
        _path[_pathLength++] = index;
        if (node is BehaviorComposite)
        {
            ref BehaviorNodeMemory memory = ref SystemMemory(node);
            memory.Int0 = 0;
            memory.Int1 = 0;
        }
        IsRepeatEntry = repeatEntry;
        for (int i = 0; i < decorators.Length; i++)
            decorators[i].OnNodeActivation(this);
        IsRepeatEntry = false;
        BehaviorService[] services = node.Services;
        for (int i = 0; i < services.Length; i++)
        {
            BehaviorService service = services[i];
            ref BehaviorNodeMemory memory = ref SystemMemory(service);
            memory.Int0 = 1;
            service.OnBecomeRelevant(this);
            if (service.TickOnSearchStart)
            {
                service.OnTick(this);
                memory.Float0 = Now + NextInterval(service);
            }
            else if (memory.Float0 == 0f)
                memory.Float0 = Now + NextInterval(service);
        }

        DebugNodeEntered(index);
        return true;
    }

    /// <summary>경로 끝 노드를 나간다 — OnNodeDeactivation, LowerPriority 조건은 지켜보기에 등록, Service 멈춤, Composite 칸 비움</summary>
    private void PopAndDeactivate(int index, EBehaviorStatus result, bool aborted)
    {
        if (_pathLength == 0 || _path[_pathLength - 1] != index)
            throw new InvalidOperationException($"경로 끝이 아닌 노드 {index}를 나가려 함");
        _pathLength--;
        BehaviorMainNode node = _definition.Mains[index];
        BehaviorDecorator[] decorators = node.Decorators;
        for (int i = 0; i < decorators.Length; i++)
        {
            BehaviorDecorator decorator = decorators[i];
            decorator.OnNodeDeactivation(this, result, aborted);
            if (BehaviorTreeCompiler.HasLowerPriority(decorator.AbortMode))
                SystemMemory(decorator).Int0 |= ObservingFlag; // 지켜보는 키의 기준은 다음 Task를 시작할 때 다시 찍는다 (BaselineKeyVersions)
        }
        BehaviorService[] services = node.Services;
        for (int i = 0; i < services.Length; i++)
        {
            ref BehaviorNodeMemory memory = ref SystemMemory(services[i]);
            if (memory.Int0 == 0)
                continue;
            memory.Int0 = 0;
            services[i].OnCeaseRelevant(this);
        }
        if (node is BehaviorComposite)
        {
            ref BehaviorNodeMemory memory = ref SystemMemory(node);
            memory.Int0 = 0;
            memory.Int1 = 0;
        }
    }

    /// <summary>
    /// Task를 시작한다 — 그 전에 지켜보는 키의 기준을 지금 값으로 (검색 중 Service가 바꾼 키는 바뀜으로 보지 않는다 — Unreal이 검색 뒤에 지켜보기를 등록하는 것과 같다).
    /// Running이면 진행 중 Task로 두고, 아니면 OnEnd까지 부른다
    /// </summary>
    private EBehaviorStatus StartTask(BehaviorTask task)
    {
        BaselineKeyVersions();
        EBehaviorStatus status = task.OnStart(this);
        if (status == EBehaviorStatus.Running)
            _activeTask = task.Index;
        else
            task.OnEnd(this, status, false);
        return status;
    }

    /// <summary>경로 노드의 조건과 등록된 LowerPriority 조건이 보는 키의 판 번호를 지금 값으로</summary>
    private void BaselineKeyVersions()
    {
        for (int depth = 0; depth < _pathLength; depth++)
        {
            BehaviorDecorator[] decorators = _definition.Mains[_path[depth]].Decorators;
            for (int i = 0; i < decorators.Length; i++)
                UpdateKeyVersion(decorators[i], ref SystemMemory(decorators[i]));
        }
        int[] observers = _definition.LowerPriorityObservers;
        for (int position = 0; position < observers.Length; position++)
        {
            BehaviorDecorator[] decorators = _definition.Mains[observers[position]].Decorators;
            for (int i = 0; i < decorators.Length; i++)
            {
                ref BehaviorNodeMemory memory = ref SystemMemory(decorators[i]);
                if ((memory.Int0 & ObservingFlag) != 0)
                    UpdateKeyVersion(decorators[i], ref memory);
            }
        }
    }

    /// <summary>노드의 조건이 모두 참인가 — 기록 없이 (미리 보기)</summary>
    private bool DecoratorsPass(int index)
    {
        BehaviorDecorator[] decorators = _definition.Mains[index].Decorators;
        for (int i = 0; i < decorators.Length; i++)
        {
            if (decorators[i].Evaluate(this) == false)
                return false;
        }
        return true;
    }

    /// <summary>노드의 Decorator가 바꾼 결과 (ModifyResult 차례) — 미리 보기</summary>
    private EBehaviorStatus ModifiedResult(BehaviorMainNode node, EBehaviorStatus result)
    {
        BehaviorDecorator[] decorators = node.Decorators;
        for (int i = 0; i < decorators.Length; i++)
            result = decorators[i].ModifyResult(this, result);
        return result;
    }

    private static BehaviorMainNode ChildAt(BehaviorComposite composite, int ordinal)
    {
        if ((uint)ordinal >= (uint)composite.Children.Length)
            throw new InvalidOperationException($"{composite.GetType().Name}.NextChild가 없는 자식 차례 {ordinal}를 반환함 (자식 {composite.Children.Length}개)");
        return composite.Children[ordinal];
    }

    private static void StoreContinuation(ref BehaviorNodeMemory memory, int previousOrdinal, EBehaviorStatus previousResult)
    {
        memory.Int0 = previousOrdinal + 1;
        memory.Int1 = previousOrdinal >= 0 ? (int)previousResult + 1 : 0;
    }

    private static void StoreReenter(ref BehaviorNodeMemory memory, int ordinal)
    {
        memory.Int0 = ordinal + 1;
        memory.Int1 = ReenterMark;
    }

    private ref BehaviorNodeMemory TopMemory()
    {
        return ref SystemMemory(_definition.Mains[_path[_pathLength - 1]]);
    }

    /// <summary>노드가 경로의 몇 번째인가 — 없으면 -1</summary>
    private int DepthOf(int index)
    {
        for (int depth = 0; depth < _pathLength; depth++)
        {
            if (_path[depth] == index)
                return depth;
        }
        return -1;
    }

    /// <summary>index를 부분 트리에 담은 가장 깊은 경로 노드의 깊이 (루트는 모두 담는다)</summary>
    private int AncestorDepthOf(int index)
    {
        for (int depth = _pathLength - 1; depth > 0; depth--)
        {
            BehaviorMainNode node = _definition.Mains[_path[depth]];
            if (node.Index <= index && index <= node.LastDescendantIndex)
                return depth;
        }
        return 0;
    }

    private float NextInterval(BehaviorService service)
    {
        return service.Interval + (NextRandom() * 2f - 1f) * service.RandomDeviation;
    }

    private void WarnVisitCap()
    {
        if (_visitCapWarned)
            return;
        _visitCapWarned = true;
        LogPrinter.LogWarning($"[BehaviorTreeRunner] 트리 '{_definition.Id}'의 한 평가가 노드 {MaxNodeVisitsPerEvaluation}개를 넘게 방문함 — 끝나지 않는 반복 (Loop · 바로 끝나는 Task)을 다음 평가로 미룸");
    }
    #endregion
}