using UnityEngine;
using static Define;
using static UnityEngine.UI.GridLayoutGroup;

/// <summary>
/// Villager의 행동 상태 머신이자 입력 수신자.
/// </summary>
public class VillagerStateMachine : InitBase, IMovementInputReceiver
{
    private Villager _owner;

    public BaseInputHandler CurrentInputHandler { get; private set; }

    #region pending - 내부에서 SetState가 호출되면 즉시 전환하지 않고 보류했다가 현재 전환이 끝난 뒤 처리
    private bool _isTransitioning;
    private bool _hasPending;
    private EUserInputState _pendingState;
    #endregion

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        return true;
    }

    public void SetInfo(Villager owner)
    {
        _owner = owner;
        SetState(EUserInputState.Movement);
    }

    private void Update()
    {
        CurrentInputHandler?.UpdateState(_owner);
    }

    #region IMovementInputReceiver — InputManager가 빙의 중일 때만 호출한다
    EActionMap IInputReceiver.RequiredContext
    {
        get { return CurrentInputHandler != null ? CurrentInputHandler.RequiredContext : EActionMap.None; }
    }

    void IMovementInputReceiver.OnMoveInput(Vector2 direction)
    {
        CurrentInputHandler?.OnMoveInput(_owner, direction);
    }

    void IMovementInputReceiver.OnInteractInput()
    {
        CurrentInputHandler?.OnInteractInput(_owner);
    }

    void IMovementInputReceiver.OnSkillInput(ESkillSlot slot)
    {   
        CurrentInputHandler?.OnSkillEventInvoke(_owner.Anim, slot);
    }
    #endregion

    /// <summary>
    /// 상태 전환.
    /// Exit(이전) → CurrentInputHandler 교체 → [빙의 중이면] InputManager.Acquire → Enter(다음)
    /// </summary>
    public void SetState(EUserInputState nextStateType)
    {
        if (_isTransitioning)
        {
            _hasPending = true;
            _pendingState = nextStateType; // 마지막 요청만 유효
            return;
        }

        BaseInputHandler next = InputHandlerRegistry.Get(nextStateType);
        if (next == null)
        {
            LogPrinter.LogError($"[VillagerStateMachine] Invalid State >> {nextStateType}");
            return;
        }
        if (CurrentInputHandler == next) // 같은 상태 재요청은 무시 (2중 SetState 방어)
            return;

        _isTransitioning = true;

        CurrentInputHandler?.Exit(_owner);
        CurrentInputHandler = next;

        // [시점 A] 컨텍스트 전환 + 수신자 결합.
        if (_owner.IsPossessed)
            Managers.Input.Acquire(this);

        CurrentInputHandler.Enter(_owner); // 필요한가 실패했는데 처리부 호출을 해야할까

        _isTransitioning = false;

        if (_hasPending)
        {
            _hasPending = false;
            SetState(_pendingState);
        }
    }

    /// <summary>[시점 B] Villager.OnPossessed에서 호출</summary>
    public void OnPossessed()
    {
        Managers.Input.Acquire(this);
    }

    /// <summary>[시점 C] Villager.OnUnpossessed에서 호출. 소유자 검증은 InputManager가 한다.</summary>
    public void OnUnpossessed()
    {
        Managers.Input.Release(this);
    }

    /// <summary>애니메이션 이벤트 진입점</summary>
    public void OnAnimationEnd()
    {
        CurrentInputHandler?.OnAnimationEnd(_owner);
    }
}