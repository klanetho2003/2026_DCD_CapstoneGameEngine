using UnityEngine;
using UnityEngine.InputSystem;
using static Define;
using static ManagerActionAsset;

/// <summary>
/// Input Context = ActionMap 하나를 감싸는 단위.
/// </summary>
public interface IInputControlContext
{
    EActionMap ActionMapType { get; }

    /// <summary>InputManager.Init에서 1회만 호출. SetCallbacks를 여기서 끝내, 전환 시 delegate 할당을 0으로 만든다.</summary>
    void Initialize(ManagerActionAsset asset);

    /// <summary>맵 활성화 + 수신자 결합. InputManager 외부에서 호출 금지.</summary>
    void Enable(IInputReceiver receiver);

    /// <summary>맵 비활성화 + 수신자 해제. InputManager 외부에서 호출 금지.</summary>
    void Disable();
}

#region Movement Context
public class MovementInputReader : IInputControlContext, IMAmovementActions
{
    public EActionMap ActionMapType { get { return EActionMap.Movement; } }

    private ManagerActionAsset _asset;
    private IMovementInputReceiver _receiver;

    public void Initialize(ManagerActionAsset asset)
    {
        _asset = asset;
        _asset.MAmovement.SetCallbacks(this); // 등록
    }

    public void Enable(IInputReceiver receiver)
    {
        _receiver = receiver as IMovementInputReceiver;
        if (receiver != null && _receiver == null)
            LogPrinter.LogError($"[MovementInputReader] 수신자가 IMovementInputReceiver를 구현하지 않음 >> {receiver.GetType().Name}");

        _asset.MAmovement.Enable();
    }

    public void Disable()
    {
        _asset.MAmovement.Disable(); // 진행 중인 액션이 있으면 canceled 콜백이 발생할 수 있기에 map을 먼저 끈다
        _receiver = null;
    }

    #region Action Routing (해석하지 않고 전달)
    public void OnMovement(InputAction.CallbackContext context)
    {
        // performed: 값 변화마다 / canceled: 기본값(0) 복귀 시 >> ReadValue가 zero를 반환.
        if (context.phase == InputActionPhase.Started)
            return;

        _receiver?.OnMoveInput(context.ReadValue<Vector2>());
    }

    public void OnInteract(InputAction.CallbackContext context)
    {
        if (context.performed == false)
            return;

        LogPrinter.Log("<color=Cyan>[InputReader] OnInteract</color>");

        _receiver?.OnInteractInput();
    }

    public void OnSkill_A(InputAction.CallbackContext context)
    {
        if (context.performed == false)
            return;

        _receiver?.OnSkillInput(ESkillSlot.SlotA);
    }

    public void OnSkill_B(InputAction.CallbackContext context)
    {
        if (context.performed == false)
            return;

        _receiver?.OnSkillInput(ESkillSlot.SlotB);
    }
    #endregion
}
#endregion

#region None Context
/// <summary>수신자가 없는 컨텍스트. 빙의 대상이 없을 때의 기본 상태.</summary>
public class NoneInputReader : IInputControlContext, IMAnoneActions
{
    public EActionMap ActionMapType { get { return EActionMap.None; } }

    private ManagerActionAsset _asset;

    public void Initialize(ManagerActionAsset asset)
    {
        _asset = asset;
        _asset.MAnone.SetCallbacks(this);
    }

    public void Enable(IInputReceiver receiver)
    {
        _asset.MAnone.Enable(); // 수신자를 사용하지 않음
    }

    public void Disable()
    {
        _asset.MAnone.Disable();
    }
}
#endregion

#region UI Context
public class UIInputReader : IInputControlContext, IMAuiActions
{
    public EActionMap ActionMapType { get { return EActionMap.UI; } }

    private ManagerActionAsset _asset;
    private IUIInputReceiver _receiver;

    public void Initialize(ManagerActionAsset asset)
    {
        _asset = asset;
        _asset.MAui.SetCallbacks(this);
    }

    public void Enable(IInputReceiver receiver)
    {
        _receiver = receiver as IUIInputReceiver;
        if (receiver != null && _receiver == null)
            LogPrinter.LogError($"[UIInputReader] 수신자가 IUIInputReceiver 미구현 >> {receiver.GetType().Name}");

        _asset.MAui.Enable();
    }

    public void Disable()
    {
        _asset.MAui.Disable();
        _receiver = null;
    }

    #region Action Routing
    public void OnNavigate(InputAction.CallbackContext context)
    {
        /*Action에 Press Interaction이 없으면 performed가 값 변화 시 1회만 오므로,
        길게 눌러 연속 이동하려면 ActionAsset에서 Hold/Press(trigger: Press and Release)나
        별도 리피트 타이머가 필요. 여기서는 1회 전달만*/
        if (context.performed == false)
            return;

        Vector2 dir = context.ReadValue<Vector2>();
        if (dir == Vector2.zero)
            return;

        _receiver?.OnNavigate(dir);
    }

    public void OnSubmit(InputAction.CallbackContext context)
    {
        if (context.performed == false)
            return;

        _receiver?.OnSubmit();
    }

    public void OnCancel(InputAction.CallbackContext context)
    {
        if (context.performed == false)
            return;

        if (_receiver == null)
            return;

        // 수신자가 소비하지 않으면(false) 이 UI를 닫는 것이 기본 동작.
        if (_receiver.OnCancel() == false)
            Managers.Input.PopOverlay(_receiver);
    }
    #endregion
}
#endregion