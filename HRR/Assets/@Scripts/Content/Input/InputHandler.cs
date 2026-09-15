using UnityEngine;
using static Define;

/// <summary>
/// Input 처리부
///
/// 규칙:
///   1. 인스턴스 필드 금지. 하나의 인스턴스를 모든 Villager가 공유한다.
///      상태별 데이터(피격 타이머 등)는 Villager 쪽에 둔다.
///   2. owner는 항상 인자로 받는다. Managers.Object.PossessedTarget 조회 금지.
///   3. 상태 전환은 owner.StateMachine.SetState()로 요청한다.
/// </summary>
public abstract class BaseInputHandler
{
    /// <summary>Registry 등록 키. 반드시 자기 자신을 가리킬 것.</summary>
    public abstract EUserInputState StateType { get; }

    /// <summary>이 상태가 요구하는 ActionMap. Reader 인스턴스가 아닌 enum으로 선언 → Input 계층과 순환 의존 없음.</summary>
    public abstract EActionMap RequiredContext { get; }

    // 생명주기 — 빙의 여부와 무관하게 모든 Villager의 StateMachine이 호출
    public virtual void Enter(Villager owner) { }
    public virtual void UpdateState(Villager owner) { }
    public virtual void Exit(Villager owner) { }

    // 입력 — 빙의 중인 Villager의 StateMachine만 호출 (InputManager가 보장)
    public virtual void OnMoveInput(Villager owner, Vector2 direction) { }
    public virtual void OnInteractInput(Villager owner) { }

    // 외부 이벤트
    public virtual void OnAnimationEnd(Villager owner) { }
}

#region Movement
public class MovementHandler : BaseInputHandler
{
    public override EUserInputState StateType { get { return EUserInputState.Movement; } }
    public override EActionMap RequiredContext { get { return EActionMap.Movement; } }

    public override void UpdateState(Villager owner)
    {
        owner.Anim.UpdateLocomotion();
    }
    public override void Exit(Villager owner)
    {
        owner.Move(Vector2.zero);
    }

    public override void OnMoveInput(Villager owner, Vector2 direction)
    {
        owner.Move(direction); // isometric 변환은 Movement 컴포넌트 책임. 여기서는 원본 Vector2를 그대로 넘긴다.
    }

    public override void OnInteractInput(Villager owner)
    {
        Managers.Interaction.RaiseInput(owner); // 어느 NPC와 상호작용할지는 각 NPC의 조건이 결정
    }
}
#endregion

#region Damaged
/// <summary>
/// 피격 중 입력 무시.
/// RequiredContext를 None으로 두는 이유: Movement 맵을 켜둔 채 입력만 버리면, 키를 누른 채 피격이 끝났을 때
/// 값 변화가 없어 performed가 다시 오지 않아 Villager가 멈춰 있게 된다.
/// None으로 전환하면 복귀 시 맵이 다시 Enable되고, Input System의 initial state check가
/// 현재 눌린 값으로 performed를 발생시킨다
/// </summary>
public class DamagedHandler : BaseInputHandler
{
    public override EUserInputState StateType { get { return EUserInputState.Damaged; } }
    public override EActionMap RequiredContext { get { return EActionMap.None; } }

    public override void OnAnimationEnd(Villager owner)
    {
        owner.StateMachine.SetState(EUserInputState.Movement);
    }
}
#endregion