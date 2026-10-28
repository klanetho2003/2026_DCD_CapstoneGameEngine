using Data;
using System;
using UnityEngine;
using static Define;

/// <summary>몬스터의 데이터와 전투 처리를 관리하고 공통 이동 컴포넌트에 이동을 요청한다.</summary>
public class MonsterBase : CombatCreature, IMovable
{
    /// <summary>현재 몬스터의 템플릿에서 읽은 전용 데이터다.</summary>
    public MonsterData Data { get; private set; }

    /// <summary>정지 중에도 유지되는 행동 제어기의 시선 방향이며 초기 방향은 오른쪽이다.</summary>
    public override Vector2 LookDirection => MovementController != null
        ? (Vector2)MovementController.Facing : Vector2.right;

    /// <summary>프리팹에 연결된 몬스터 애니메이션이며 없는 경우 null로 안전하게 처리한다.</summary>
    public override BaseCreatureAnim CreatureAnim => GetComponent<BaseCreatureAnim>();

    /// <summary>현재 생애의 순찰, 반복 감지, 카운트다운, BFS 이탈을 관리한다.</summary>
    public MonsterMovementController MovementController { get; private set; }

    /// <summary>타일 중앙 사이의 실제 이동을 담당하는 공통 컴포넌트다.</summary>
    public CreatureMovement Movement { get; private set; }

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
        if (GetComponent<MonsterEscapeIndicator>() == null)
            gameObject.AddComponent<MonsterEscapeIndicator>();

        return true;
    }

    /// <summary>템플릿 데이터와 이동 소유자를 연결하고 새 생애의 오른쪽 순찰을 준비한다.</summary>
    /// <param name="objectID">몬스터를 구성할 템플릿 데이터의 식별자다.</param>
    public override void SetInfo(int objectID)
    {
        base.SetInfo(objectID);

        Data = GetCreatureData<MonsterData>();
        Movement.SetInfo(this);
        if (MovementController == null)
            MovementController = new MonsterMovementController(this);
        else
            MovementController.Reset();
    }

    /// <summary>초기화한 몬스터의 행동과 예고 타이머를 게임 시간에 맞춰 갱신한다.</summary>
    private void Update()
    {
        MovementController?.Tick(Time.deltaTime);
    }

    /// <summary>풀 반환 시 이전 생애의 순환 기록과 경로, 카운트다운을 비운다.</summary>
    private void OnDisable()
    {
        MovementController?.Reset();
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
        MovementController?.Reset();
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
}
