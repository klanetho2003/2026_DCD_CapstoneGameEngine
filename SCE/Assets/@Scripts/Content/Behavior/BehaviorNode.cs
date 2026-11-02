using System;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Behavior Tree 노드의 바탕 — Composite · Task (Main 노드)와 Decorator · Service (보조 노드).
///
/// 규칙
///   1. 노드 객체는 트리 정의 하나에 속하고, 그 트리를 쓰는 모든 몬스터가 공유한다. 필드는 기획자 설정만 둔다.
///   2. 실행 중 상태는 실행기의 칸 (BehaviorNodeMemory)에만 둔다 — 필요한 칸 수를 MemorySlotCount로 밝히고 runner.Memory(this, i)로 쓴다.
///   3. 설정 필드는 public + [JsonProperty("키")] — [JsonProperty]가 붙은 멤버만 저장 · 읽기한다. 키를 고정해 필드 이름을 바꿔도 데이터는 그대로다.
///      Unity의 SerializeReference 편집도 public 필드를 쓴다. "type"은 노드 종류 키라 칸 이름으로 쓰지 않는다.
///   4. 로드 뒤 설정 값을 바꾸지 않는다 — 그 트리를 쓰는 모든 몬스터에 반영된다.
///   5. 평가 중 할당하지 않는다 (LINQ · 클로저 · 박싱 · 문자열 만들기 없음).
/// </summary>
[Serializable]
[JsonObject(MemberSerialization.OptIn)]
public abstract class BehaviorNode
{
    private protected BehaviorNode() { } // 노드는 아래 네 종류 (Composite · Task · Decorator · Service) 중 하나를 이어받는다

    /// <summary>편집기가 정하는 노드 이름표 — 배치 파일 · 디버거가 노드를 찾는 데 쓴다. 비어도 되고, 있으면 트리 안에서 겹치지 않아야 한다</summary>
    [JsonProperty("id")] public string NodeId = "";

    /// <summary>정의에 컴파일되었는가 — 컴파일된 노드는 다시 컴파일하지 않는다 (그 정의를 쓰는 몬스터의 칸 · 키 칸이 어긋나지 않게)</summary>
    public bool IsCompiled { get; internal set; }

    /// <summary>컴파일이 정한 번호 — Main 노드는 실행 순서 (깊이 우선, 작을수록 우선순위가 높다), 보조 노드는 보조 노드 순서. 컴파일 전 -1</summary>
    public int Index { get; internal set; } = -1;

    /// <summary>이 노드 칸이 실행기 칸 배열에서 시작하는 자리 (실행기가 쓰는 칸 포함). 컴파일 전 -1</summary>
    public int MemoryOffset { get; internal set; } = -1;

    /// <summary>노드가 쓰는 칸의 시작 자리 (실행기가 쓰는 칸 다음). 컴파일 전 -1</summary>
    public int OwnMemoryOffset { get; internal set; } = -1;

    /// <summary>노드가 실행 중 쓰는 칸 수 (실행기가 쓰는 칸은 빼고). 기본 0, 노드 하나에 BehaviorTreeCompiler.MaxNodeMemorySlots까지</summary>
    public virtual int MemorySlotCount => 0;

    /// <summary>실행기가 이 노드에 쓰는 칸 수 — 종류마다 정해져 있다</summary>
    internal virtual int SystemSlotCount => 0;

    /// <summary>
    /// 컴파일 — 키 고르기 · 값 또는 키 칸을 모두 해석하고 (context.ResolveKey · Resolve — 중간에 return하지 않는다), 노드 고유 값을 검사한다 (context.Error · Warning).
    /// 트리를 읽을 때 한 번 불린다. 종류의 공통 칸 (Observer Aborts · Service 간격)은 컴파일러가 따로 본다.
    /// </summary>
    public virtual void Compile(BehaviorCompileContext context) { }

#if UNITY_EDITOR
    /// <summary>
    /// 디버거 (편집기만) — 이 몬스터 (runner)에서 이 노드의 실행 값을 text에 짧은 글로 더한다 (남은 시간 · 횟수 · 지금 거리 등, 보일 것이 없으면 더하지 않는다).
    /// active = 이 노드가 진행 중이다 (Task는 진행 중 Task, Composite는 진행 경로에 있음, Decorator · Service는 붙은 노드가 진행 경로에 있음).
    /// 게임 상태를 바꾸지 않는다 — 크리처 키는 runner.Blackboard.PeekCreature로 읽는다 (GetCreature는 쓸 수 없게 된 대상을 지운다)
    /// </summary>
    public virtual void DescribeRuntimeValues(BehaviorTreeRunner runner, bool active, StringBuilder text) { }

    /// <summary>디버거 (편집기만) — 이 몬스터에서 Scene 뷰에 그릴 모양 (거리 원 · 목적지 등, 월드 좌표)을 담는다. active · 지킬 것은 DescribeRuntimeValues와 같다</summary>
    public virtual void CollectDebugShapes(BehaviorTreeRunner runner, bool active, BehaviorDebugShapes shapes) { }
    
    /// <summary>
    /// 세부 설정 패널 (편집기만) — 칸 (JSON 키)을 지금 설정에서 쓰지 않으면 그 까닭, 쓰면 null. 패널은 쓰지 않는 칸을 잠근다 (값은 지우지 않고 그대로 저장).
    /// keys = 트리의 Blackboard 키 (고른 키의 형식을 볼 때). 지금 값이 컴파일 에러이거나 저장할 수 없는 칸은 고칠 수 있게 null을 준다. 기본: 모든 칸을 쓴다
    /// </summary>
    public virtual string UnusedFieldReason(string field, BehaviorBlackboardKey[] keys)
    {
        return null;
    }

    /// <summary>세부 설정 패널 (편집기만) — enum 칸 (JSON 키)의 목록에 value를 둘까. 지금 값이 목록에 없으면 패널이 맨 앞에 따로 보인다. 기본: 모두</summary>
    public virtual bool AllowsChoice(string field, int value, BehaviorBlackboardKey[] keys)
    {
        return true;
    }
#endif
}

/// <summary>Composite · Task — Decorator · Service를 붙이고, 실행 순서 번호를 가진다</summary>
[Serializable]
public abstract class BehaviorMainNode : BehaviorNode
{
    private protected BehaviorMainNode() { }

    /// <summary>이 노드에 들어가기 전에 모두 참이어야 하는 조건 · 흐름 장치 (AND)</summary>
    [SerializeReference, JsonProperty("decorators")] public BehaviorDecorator[] Decorators = Array.Empty<BehaviorDecorator>();

    /// <summary>이 노드가 진행 중인 동안 간격마다 갱신하는 것</summary>
    [SerializeReference, JsonProperty("services")] public BehaviorService[] Services = Array.Empty<BehaviorService>();

    /// <summary>부모 Composite의 실행 순서 (루트는 -1)</summary>
    public int ParentIndex { get; internal set; } = -1;

    /// <summary>부모의 몇 번째 자식인가 (루트는 -1)</summary>
    public int ChildOrdinal { get; internal set; } = -1;

    /// <summary>이 노드 부분 트리의 마지막 실행 순서 — 실행 순서 n이 이 부분 트리 안인가 = Index ≤ n ≤ LastDescendantIndex</summary>
    public int LastDescendantIndex { get; internal set; } = -1;

    /// <summary>루트에서의 깊이 (루트 0)</summary>
    public int Depth { get; internal set; }

    /// <summary>Self · Both로 지켜보는 Decorator가 있는가 — 이 노드가 진행 중 경로에 있는 동안 조건을 다시 본다</summary>
    public bool HasSelfObservers { get; internal set; }

    /// <summary>
    /// LowerPriority · Both로 지켜보는 Decorator가 있는가 — 검색이 이 노드를 지나간 뒤 (조건이 거짓이었거나 끝났거나 · Both는 들어온 뒤부터)
    /// 이 부분 트리 뒤 (더 낮은 우선순위)의 Task가 진행 중이면 조건을 다시 본다. 그보다 앞에서 검색이 다시 시작되면 그만 본다
    /// </summary>
    public bool HasLowerPriorityObservers { get; internal set; }
}

/// <summary>
/// 자식을 고르는 노드 (Selector · Sequence 등). 다음 자식은 NextChild 하나로 정한다 — 상태를 바꾸지 않는 함수라 실행기 · 검색이 미리 물어볼 수 있다.
/// 실행기는 진행 중인 자식 차례를 이 노드의 실행기 칸에 둔다.
/// </summary>
[Serializable]
public abstract class BehaviorComposite : BehaviorMainNode
{
    [SerializeReference, JsonProperty("children")] public BehaviorMainNode[] Children = Array.Empty<BehaviorMainNode>();

    /// <summary>
    /// 다음에 실행할 자식 차례. previousOrdinal = 방금 끝난 자식 차례 (처음 들어올 때 -1), previousResult = 그 결과 (처음 들어올 때는 보지 않는다).
    /// 이 노드가 끝나면 -1을 반환하고 끝 결과를 finishResult에 둔다.
    /// runner는 읽기만 한다 (Blackboard · 자기 칸) — 같은 상태 · 같은 입력에 같은 결과여야 검색이 미리 물어볼 수 있다.
    /// </summary>
    public abstract int NextChild(BehaviorTreeRunner runner, int previousOrdinal, EBehaviorStatus previousResult, out EBehaviorStatus finishResult);

    /// <summary>
    /// 자식의 Decorator가 LowerPriority · Both로 끼어들 수 있는가. 자식이 차례로 모두 성공해야 하는 Composite (Sequence)는 false —
    /// 뒤 자식은 앞 자식의 다음 단계이지 낮은 우선순위가 아니다.
    /// </summary>
    public virtual bool CanAbortLowerPriority => true;

    /// <summary>진행 중인 자식 차례 (+1, 0 = 없음)</summary>
    internal override int SystemSlotCount => 1;
}

/// <summary>
/// 행동 잎 노드. OnStart → (진행이면 OnUpdate를 평가 간격마다, UpdateEveryFrame이면 매 프레임) → 끝 (OnEnd).
/// 끊기: OnAbort — Done이면 바로 끝, Pending이면 실행기가 OnAbortUpdate를 Done이 될 때까지 묻고 그동안 새 행동을 시작하지 않는다.
/// 재우기 · 풀 반납 · 죽음의 강제 정리에서는 OnAbort · OnEnd가 불리지 않는다 — 몸의 정리 (Clear)가 몸 상태를 맡는다.
/// </summary>
[Serializable]
public abstract class BehaviorTask : BehaviorMainNode
{
    /// <summary>진행 중일 때 매 프레임 OnUpdate를 받는가 (기본: 평가 간격마다) — 끝을 바로 알아야 하는 Task (스킬 끝 확인 등)</summary>
    public virtual bool UpdateEveryFrame => false;

    /// <summary>행동 잠금 (스킬 · 기절 · 강제 이동 중)에도 OnUpdate를 받는가 — 스스로 잠금을 만드는 Task (스킬 쓰기)</summary>
    public virtual bool UpdateWhileActionLocked => false;

    /// <summary>시작 — Success · Failure면 바로 끝, Running이면 진행</summary>
    public abstract EBehaviorStatus OnStart(BehaviorTreeRunner runner);

    /// <summary>진행 중 갱신 — Success · Failure면 끝. 기본 Running (끝나지 않음)</summary>
    public virtual EBehaviorStatus OnUpdate(BehaviorTreeRunner runner)
    {
        return EBehaviorStatus.Running;
    }

    /// <summary>끊기 요청. 기본 Done (바로 끝)</summary>
    public virtual EBehaviorAbortResult OnAbort(BehaviorTreeRunner runner)
    {
        return EBehaviorAbortResult.Done;
    }

    /// <summary>끊기를 미루는 동안 실행기가 묻는다 (OnUpdate와 같은 주기). 기본 Done</summary>
    public virtual EBehaviorAbortResult OnAbortUpdate(BehaviorTreeRunner runner)
    {
        return EBehaviorAbortResult.Done;
    }

    /// <summary>끝 — 끝났거나 끊긴 뒤 한 번. 끊겼으면 aborted가 true이고 result는 Failure</summary>
    public virtual void OnEnd(BehaviorTreeRunner runner, EBehaviorStatus result, bool aborted) { }
}

/// <summary>Decorator · Service — Main 노드에 붙는다</summary>
[Serializable]
public abstract class BehaviorAuxiliaryNode : BehaviorNode
{
    private protected BehaviorAuxiliaryNode() { }

    /// <summary>붙은 Main 노드의 실행 순서 — 컴파일이 정한다</summary>
    public int OwnerIndex { get; internal set; } = -1;
}

/// <summary>
/// 붙은 노드에 들어가기 전에 보는 조건 · 흐름 장치. 조건 값은 CalculateRawConditionValue (부수 효과 없음 — 실행기가 지켜볼 때 몇 번이고 부른다).
/// AbortMode로 결과가 바뀔 때 끊을 범위를 고른다. 흐름 장치 (Cooldown · TimeLimit · Loop · ForceSuccess)는 아래 갈고리를 쓴다.
/// 붙은 노드가 끝날 때의 차례: ShouldRepeat (하나라도 true면 반복) → ModifyResult (차례대로) → OnNodeDeactivation (바뀐 결과).
/// 반복이면 그 회를 끝내고 다시 들어온다 — 조건을 다시 보고 OnNodeActivation. 처음 들어온 것과 반복을 가르려면 Decorator가 자기 칸에 표시한다.
/// </summary>
[Serializable]
public abstract class BehaviorDecorator : BehaviorAuxiliaryNode
{
    [JsonProperty("abort")] public EBehaviorAbortMode AbortMode = EBehaviorAbortMode.None;

    /// <summary>조건 값을 뒤집는다 (Unreal의 Inverse Condition)</summary>
    [JsonProperty("inverse")] public bool InverseCondition;

    /// <summary>고를 수 있는 Observer Aborts — 기본 모두. 흐름 장치는 좁힌다 (예: Cooldown은 None · LowerPriority)</summary>
    public virtual bool AllowsAbortMode(EBehaviorAbortMode mode)
    {
        return true;
    }

    /// <summary>결과 뒤집기를 쓸 수 있는가 — 조건이 아닌 흐름 장치는 false</summary>
    public virtual bool AllowsInverse => true;

    /// <summary>뒤집기 전 조건 값. 같은 상태에 같은 결과 · 부수 효과 없음. 기본 참 (조건이 아닌 흐름 장치)</summary>
    protected virtual bool CalculateRawConditionValue(BehaviorTreeRunner runner)
    {
        return true;
    }

    /// <summary>조건 값 (뒤집기 반영)</summary>
    public bool Evaluate(BehaviorTreeRunner runner)
    {
        return CalculateRawConditionValue(runner) != InverseCondition;
    }

    /// <summary>값이 바뀌면 붙은 가지를 다시 시작할 Blackboard 키 칸 (Unreal의 'On Value Change') — 없으면 -1</summary>
    public virtual int RestartKeySlot => -1;


    /// <summary>
    /// 조건이 읽는 Blackboard 키 칸 — LowerPriority로 지켜보는 동안 이 키가 바뀌면 (조건이 참일 때) 다시 검색을 요청한다. 없으면 -1.
    /// 기본은 RestartKeySlot과 같다. 키를 읽는 조건은 다시 시작 여부와 상관없이 키 칸을 반환한다
    /// </summary>
    public virtual int ObservedKeySlot => RestartKeySlot;

    /// <summary>붙은 노드가 시작될 때 (검색이 들어옴 — 반복도)</summary>
    public virtual void OnNodeActivation(BehaviorTreeRunner runner) { }

    /// <summary>붙은 노드가 끝나거나 끊길 때 (반복의 한 회가 끝날 때도). result는 ModifyResult를 지난 결과, 끊겼으면 aborted가 true이고 result는 Failure</summary>
    public virtual void OnNodeDeactivation(BehaviorTreeRunner runner, EBehaviorStatus result, bool aborted) { }

    /// <summary>붙은 노드를 한 번 더 실행할까 — 노드가 끝난 직후 (끊긴 때는 묻지 않는다). 기본 false</summary>
    public virtual bool ShouldRepeat(BehaviorTreeRunner runner, EBehaviorStatus result)
    {
        return false;
    }

    /// <summary>붙은 노드의 결과를 부모에 넘기기 전에 바꾼다 (반복하지 않을 때). 기본 그대로</summary>
    public virtual EBehaviorStatus ModifyResult(BehaviorTreeRunner runner, EBehaviorStatus result)
    {
        return result;
    }

    /// <summary>지켜보는 상태 — 마지막 조건 결과 · 본 키 판 번호 · LowerPriority 등록 (실행기만 쓴다)</summary>
    internal override int SystemSlotCount => 1;

#if UNITY_EDITOR
    /// <summary>결과 뒤집기를 쓰지 않는 흐름 장치 (AllowsInverse)는 inverse 칸을 쓰지 않는다 — 켜져 있으면 (컴파일 에러) 끌 수 있게 null</summary>
    public override string UnusedFieldReason(string field, BehaviorBlackboardKey[] keys)
    {
        if (field == "inverse" && AllowsInverse == false && InverseCondition == false)
            return "결과를 뒤집지 않는 흐름 장치";
        return base.UnusedFieldReason(field, keys);
    }

    /// <summary>abort 목록 = 이 노드가 고를 수 있는 Observer Aborts (AllowsAbortMode)</summary>
    public override bool AllowsChoice(string field, int value, BehaviorBlackboardKey[] keys)
    {
        if (field == "abort")
            return AllowsAbortMode((EBehaviorAbortMode)value);
        return base.AllowsChoice(field, value, keys);
    }
#endif
}

/// <summary>
/// 붙은 노드가 진행 중인 동안 간격 (Interval ± RandomDeviation)마다 OnTick — 감지 · 값 갱신 (Blackboard 쓰기).
/// 진행 중 경로에 들어오면 OnBecomeRelevant, 나가면 OnCeaseRelevant. 다시 들어와도 다음 갱신 시각은 이어 간다.
/// </summary>
[Serializable]
public abstract class BehaviorService : BehaviorAuxiliaryNode
{
    /// <summary>갱신 간격 (초) — 0보다 커야 한다</summary>
    [JsonProperty("interval")] public float Interval = 0.5f;

    /// <summary>간격의 무작위 편차 (초) — 0 이상, 간격보다 작게. 몬스터마다 갱신이 같은 프레임에 몰리지 않게</summary>
    [JsonProperty("randomDeviation")] public float RandomDeviation = 0.1f;

    /// <summary>검색이 이 노드에 들어올 때 한 번 갱신한다 (Unreal의 Call Tick on Search Start) — 들어오자마자 값이 필요한 감지</summary>
    [JsonProperty("tickOnSearchStart")] public bool TickOnSearchStart = true;

    public virtual void OnBecomeRelevant(BehaviorTreeRunner runner) { }

    public abstract void OnTick(BehaviorTreeRunner runner);

    public virtual void OnCeaseRelevant(BehaviorTreeRunner runner) { }

    /// <summary>다음 갱신 시각</summary>
    internal override int SystemSlotCount => 1;
}