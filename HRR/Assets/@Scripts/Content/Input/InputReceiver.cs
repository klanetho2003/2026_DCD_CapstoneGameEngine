using UnityEngine;
using static Define;

/// <summary>
/// 입력 수신 주체의 공통 계약.
/// 수신자는 어떤 ActionMap이 필요한지를 선언하고, 맵을 켜고 끄는 시점은 InputManager가 결정한다.
/// </summary>
public interface IInputReceiver
{
    EActionMap RequiredContext { get; }
}

/// <summary>
/// MAmovement 맵의 입력 수신. 값은 해석하지 않고 전달한다.
/// 새 ActionMap을 추가할 때는 별도로 인터페이스를 만든다.
/// </summary>
public interface IMovementInputReceiver : IInputReceiver
{
    /// <param name="direction">performed: 현재 입력 값 / canceled: Vector2.zero (정지 신호)</param>
    void OnMoveInput(Vector2 direction);
    void OnInteractInput();
}

/// <summary>
/// MAui 맵의 입력 수신 계약.
/// </summary>
public interface IUIInputReceiver : IInputReceiver
{
    /// <param name="direction">정규화되지 않은 원본 Vector2. 축 판정은 수신자 책임.</param>
    void OnNavigate(Vector2 direction);
    void OnSubmit();

    /// <summary>Cancel 처리. true를 반환하면 소비된 것으로 보고 InputManager가 자동 Pop하지 않는다.</summary>
    bool OnCancel();
}