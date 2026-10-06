using System;
using UnityEngine;

public class DEVlookPointViewer : MonoBehaviour
{
    [SerializeField]
    Transform _mouseLookDirection;

    [SerializeField]
    Transform _keyboardLookDirection;

    void Update()
    {
        var owner = Managers.Object.PossessedTarget;
        float zPointerRotation = MathUtil.GetZRotationFromDirection(owner.UserAim.PointerDirection);
        _mouseLookDirection.rotation = Quaternion.Euler(0f, 0f, zPointerRotation);

        float zInputRotation = MathUtil.GetZRotationFromDirection(owner.StateMachine.CurrentInputHandler.InputDirection);
        _keyboardLookDirection.rotation = Quaternion.Euler(0f, 0f, zInputRotation);
    }
}
