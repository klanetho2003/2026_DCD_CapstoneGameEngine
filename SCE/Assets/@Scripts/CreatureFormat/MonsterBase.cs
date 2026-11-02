using Data;
using System;
using UnityEngine;
using static Define;

/// <summary>몬스터의 데이터와 전투 처리를 관리하고 행동 트리로 공통 이동 컴포넌트에 이동을 요청한다. 행동 트리 Id가 비어 있으면 연속 순찰 기본 트리를 쓴다.</summary>
public class MonsterBase : CombatCreature, IMovable, IBehaviorTileAgent
{
    /// <summary>행동 트리가 새 행동을 시작하지 않는 동안의 태그다.</summary>
    private const ECreatureTag ACTION_LOCK_TAGS = ECreatureTag.UsingSkill | ECreatureTag.Stunned | ECreatureTag.ForceMoving;

    /// <summary>MonsterData.BehaviorTreeId가 비어 있을 때 쓰는 연속 순찰과 탈출의 기본 트리 Id(트리 JSON의 Addressable 주소)다.</summary>
    public const string DEFAULT_BEHAVIOR_TREE_ID = "Monster_TileDefault";

    /// <summary>현재 몬스터의 템플릿에서 읽은 전용 데이터다.</summary>
    public MonsterData Data { get; private set; }

    /// <summary>정지 중에도 유지되는 한 칸 출발기의 시선 방향이며 초기 방향은 오른쪽이다.</summary>
    public override Vector2 LookDirection => Stepper != null
        ? (Vector2)Stepper.Facing : Vector2.right;

    /// <summary>프리팹에 연결된 몬스터 애니메이션이며 없는 경우 null로 안전하게 처리한다.</summary>
    public override BaseCreatureAnim CreatureAnim => GetComponent<BaseCreatureAnim>();

    /// <summary>이번 스폰의 행동 트리(MonsterData.BehaviorTreeId, 비어 있으면 기본 트리)를 매 프레임 갱신하는 실행기다.</summary>
    public BehaviorTreeRunner BehaviorRunner { get; private set; }

    /// <summary>이번 스폰이 쓰는 트리 Id이며 MonsterData.BehaviorTreeId, 비어 있으면 기본 트리 Id다. 행동 트리 편집기가 쓰는 몬스터를 찾을 때 읽는다.</summary>
    public string BehaviorTreeId { get; private set; }

    /// <summary>이번 스폰의 행동 트리 정의이며 읽기에 실패하면 null이고 몬스터는 가만히 있는다.</summary>
    private BehaviorTreeDefinition _behaviorTree;

    /// <summary>실행기를 묶은 생애의 스폰 번호이며 다르면 새 생애로 다시 묶는다.</summary>
    private int _lifeSerial;

    /// <summary>라운드 진행 중에만 흐르는 행동 트리 시계(초)이며 라운드 정지 중에는 기다리기도 멈춘다.</summary>
    private float _behaviorClock;

    /// <summary>타일 중앙 사이의 실제 이동을 담당하는 공통 컴포넌트다.</summary>
    public CreatureMovement Movement { get; private set; }

    /// <summary>한 칸 출발과 도착 결과, 정지 중 시선을 관리하며 행동 트리의 칸 이동 노드가 쓴다.</summary>
    public MonsterTileStepper Stepper { get; private set; }

    /// <summary>성공한 도착 기록과 세 바퀴 순환 뒤의 탈출 단계를 관리하며 행동 트리의 칸 이동 노드가 쓴다.</summary>
    public MonsterEscapeTracker EscapeTracker { get; private set; }

    /// <summary>몬스터의 이동 속도 스탯에서 읽은 기본 월드 이동 속도다.</summary>
    public float MoveSpeed { get { return GetStatValue(EStatType.MoveSpeed).Value; } }

    /// <summary>공통 이동 컴포넌트가 기록하는 몬스터의 실제 이동 방향이다.</summary>
    public Vector2 CacheMoveDirection { get; set; }

    /// <summary>기본 캐릭터와 이동 컴포넌트를 초기화하고 탈출 예고 표시를 연결한다.</summary>
    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        Movement = GetComponent<CreatureMovement>();
        if (Movement == null)
            Movement = gameObject.AddComponent<CreatureMovement>();
        Stepper = new MonsterTileStepper(this);
        EscapeTracker = new MonsterEscapeTracker(Stepper);
        BehaviorRunner = new BehaviorTreeRunner(this, BehaviorGameWorld.Instance);
        if (GetComponent<MonsterEscapeIndicator>() == null)
            gameObject.AddComponent<MonsterEscapeIndicator>();

        return true;
    }

    /// <summary>템플릿 데이터와 이동 소유자를 연결하고 새 생애의 오른쪽 순찰과 행동 트리를 준비한다.</summary>
    /// <param name="objectID">몬스터를 구성할 템플릿 데이터의 식별자다.</param>
    public override void SetInfo(int objectID)
    {
        base.SetInfo(objectID);

        Data = GetCreatureData<MonsterData>();
        Movement.SetInfo(this);
        ResetTileMovement();

        BehaviorTreeId = Data != null && !string.IsNullOrEmpty(Data.BehaviorTreeId)
            ? Data.BehaviorTreeId : DEFAULT_BEHAVIOR_TREE_ID;
        _behaviorTree = Managers.Behavior.GetDefinition(BehaviorTreeId);
        if (_behaviorTree == null)
            BehaviorRunner.Unbind();
        BehaviorRunner.Start();
    }

    /// <summary>초기화한 몬스터의 행동 트리와 예고 타이머를 게임 시간에 맞춰 갱신한다.</summary>
    private void Update()
    {
        if (_behaviorTree != null)
            UpdateBehavior();
    }

    /// <summary>풀 반환 시 이전 생애의 순환 기록과 경로, 카운트다운을 비운다.</summary>
    private void OnDisable()
    {
        ResetTileMovement();
    }

    /// <summary>새 생애나 풀 반환, 사망 시 탈출 기록과 한 칸 출발 상태를 비우고 시선을 오른쪽으로 되돌린다.</summary>
    private void ResetTileMovement()
    {
        EscapeTracker.Reset();
        Stepper.Reset(true);
    }

    #region Combat Creature
    /// <summary>요청한 공격 주체가 몬스터를 공격할 수 있는 Villager인지 확인한다.</summary>
    /// <param name="objType">공격 주체의 오브젝트 종류다.</param>
    public override bool IsValidTarget(EObjectType objType)
    {
        return objType == EObjectType.Villager;
    }

    /// <summary>현재는 별도 보정 없이 전달된 원본 피해량을 최종 피해량으로 반환한다.</summary>
    /// <param name="damageInfo">원본 피해량과 공격 정보가 담긴 데이터다.</param>
    protected override float CalculateFinalDamage(DamageInfo damageInfo)
    {
        return damageInfo.Amount;
    }

    /// <summary>피해 적용 후 추가 반응을 구현할 확장 지점이며 현재는 추가 처리가 없다.</summary>
    /// <param name="damageInfo">이번 피해의 원본 공격 정보다.</param>
    /// <param name="finalDamage">이번에 실제로 적용한 최종 피해량이다.</param>
    protected override void OnAfterDamageApplied(DamageInfo damageInfo, float finalDamage)
    {
        //throw new System.NotImplementedException();
    }

    /// <summary>몬스터 사망을 기록하며 후속 사망 연출이나 디스폰 처리를 연결할 지점이다.</summary>
    protected override void OnDie()
    {
        Movement.CancelToCurrentCell();
        ResetTileMovement();
        BehaviorRunner.Stop();
        LogPrinter.Log($"[MonsterBase] {Data.prefabName} Died");
        Managers.Object.Despawn(this);
    }
    #endregion

    #region Movable

    /// <summary>몬스터의 방향 입력을 공통 타일 이동 컴포넌트에 전달한다.</summary>
    /// <param name="direction">다음 한 칸 이동에 사용할 방향이며 0이면 연속 이동을 멈춘다.</param>
    /// <param name="speedWeight">다음 한 칸 이동에 적용할 기본 속도의 배율이다.</param>
    public void Move(Vector2 direction, float speedWeight = 1)
    {
        Movement.Move(direction, speedWeight);
    }

    /// <summary>몬스터의 인접 타일 목적지와 완료 콜백을 공통 이동 컴포넌트에 전달한다.</summary>
    /// <param name="destination">이동할 인접 타일에 포함되는 월드 좌표다.</param>
    /// <param name="speedWeight">이번 한 칸 이동에 적용할 기본 속도의 배율이다.</param>
    /// <param name="onComplete">목적지에 실제로 도착한 뒤 실행할 콜백이다.</param>
    public void ForceMoveTo(Vector2 destination, float speedWeight = 1, Action onComplete = null)
    {
        Movement.ForceMoveTo(destination, speedWeight, onComplete);
    }

    /// <summary>지정 시간 동안 강제 이동하는 확장 지점이며 아직 구현되지 않았다.</summary>
    /// <param name="direction">강제 이동에 사용할 방향이다.</param>
    /// <param name="duration">강제 이동을 유지할 시간이다.</param>
    /// <param name="speedWeight">강제 이동에 적용할 기본 속도의 배율이다.</param>
    /// <param name="onComplete">강제 이동 완료 후 실행할 콜백이다.</param>
    public void ForceMoveFor(Vector2 direction, float duration, float speedWeight = 1, Action onComplete = null)
    {
        throw new NotImplementedException();
    }

    /// <summary>강제 이동 중단을 구현할 확장 지점이며 아직 구현되지 않았다.</summary>
    /// <param name="onComplete">강제 이동 중단 후 실행할 콜백이다.</param>
    public void StopForceMove(Action onComplete = null)
    {
        throw new NotImplementedException();
    }
    #endregion

    #region 행동 트리 (IBehaviorTileAgent)
    /// <summary>사망이나 라운드 정지 중에는 진행 중인 한 칸을 칸 중앙으로 되돌리고 트리 시계와 예고 시간을 멈추며, 맵 배치가 바뀌면 탈출 기록을 비우고, 새 생애면 실행기를 묶은 뒤 예고 시간과 트리를 갱신한다.</summary>
    private void UpdateBehavior()
    {
        if (IsDead || Managers.Stage.Phase != EStagePhase.Playing)
        {
            Stepper.Cancel();
            return;
        }
        if (EscapeTracker.IsTraversalStale)
        {
            // 맵의 정적 배치가 바뀌면 진행 중인 한 칸을 칸 중앙으로 되돌리고 탈출 기록을 비운다 (시선은 그대로).
            Stepper.Cancel();
            EscapeTracker.Reset();
        }
        if (_lifeSerial != SpawnSerial)
        {
            // 스폰 번호는 SetInfo 뒤에 매겨지므로 첫 갱신에서 이 생애의 처음 상태로 묶는다.
            _lifeSerial = SpawnSerial;
            BehaviorRunner.Bind(_behaviorTree);
        }
        EscapeTracker.TickPreparation(Time.deltaTime);
        _behaviorClock += Time.deltaTime;
        BehaviorRunner.Tick(_behaviorClock);
    }

    /// <summary>행동 트리 노드가 읽는 몬스터의 월드 위치다.</summary>
    Vector2 IBehaviorAgent.Position => transform.position;

    /// <summary>스킬, 기절, 강제 이동 중에는 행동 트리가 새 행동을 시작하지 않는다.</summary>
    bool IBehaviorAgent.IsActionLocked => HasTag(ACTION_LOCK_TAGS);

    /// <summary>행동 트리가 이동을 멈출 때 진행 중인 한 칸을 취소하고 선점한 칸 중앙에 정렬한다.</summary>
    void IBehaviorAgent.StopMoving()
    {
        Stepper.Cancel();
    }

    /// <summary>몬스터 데이터에서 읽는 Blackboard 처음 값 출처가 아직 없어 항상 거짓이다.</summary>
    /// <param name="seed">Blackboard 키가 밝힌 처음 값 출처다.</param>
    /// <param name="value">찾지 못하면 0이다.</param>
    bool IBehaviorAgent.TryGetIntSeed(string seed, out int value)
    {
        value = 0;
        return false;
    }

#if UNITY_EDITOR
    /// <summary>
    /// 편집기 — Play 중 저장한 트리를 다시 읽었을 때 (BehaviorManager.ReloadTree가 몬스터마다 부른다). 이 몬스터의 트리 Id(BehaviorTreeId)가 treeId면 이번 스폰의 트리를 새 정의로 바꾸고,
    /// 생애를 시작했으면 실행기를 다시 묶는다 (노드 칸 · Blackboard 처음 값 · 루트부터 — 새 생애의 묶기와 같다, 탈출 기록 · 시선은 그대로). 실행기가 실행 중이었으면 진행 중인 한 칸을 칸 중앙으로 되돌리고,
    /// 멈춰 있던 실행기 (죽음)는 멈춘 채 둔다. 바꿨으면 true. O(칸 수 + 키 수)
    /// </summary>
    /// <param name="treeId">다시 읽은 트리 Id다.</param>
    /// <param name="definition">이 몬스터의 트리 Id로 찾은 새 정의다.</param>
    public bool ReloadBehaviorTree(string treeId, BehaviorTreeDefinition definition)
    {
        if (definition == null || !string.Equals(BehaviorTreeId, treeId, StringComparison.Ordinal))
            return false;
        _behaviorTree = definition;
        if (_lifeSerial != SpawnSerial)
            return true; // 첫 갱신이 새 정의로 묶는다
        bool stopped = BehaviorRunner.IsStopped;
        if (!stopped)
            Stepper.Cancel();
        BehaviorRunner.Bind(definition);
        if (stopped)
            BehaviorRunner.Stop();
        return true;
    }
#endif
    #endregion
}
