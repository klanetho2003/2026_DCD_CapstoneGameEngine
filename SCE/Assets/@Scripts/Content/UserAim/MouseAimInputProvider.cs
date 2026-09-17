using Unity.Cinemachine;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Aim 위치 결정자 인터페이스.
/// 
/// 다양한 입력 소스(마우스, 게임패드, AI 자동 조준, 락온 등)는 모두
/// "Player의 현재 위치를 받고, Aim의 월드 위치를 반환"한다로 통일
/// 
/// PlayerAim은 본 인터페이스의 구현체 하나만 알고 있으면 되고,
/// LookDirection 변환 로직(8방향 snap, dead zone)은 입력 소스와 무관하게 재사용
/// </summary>
public interface IAimInputProvider
{
    /// <summary>
    /// Player의 현재 월드 위치를 받고, Aim이 가리켜야 할 월드 위치를 반환한다.
    /// </summary>
    /// <param name="playerPosition">Player의 현재 월드 좌표</param>
    /// <returns>Aim의 목표 월드 좌표</returns>
    Vector2 ResolveAimPosition(Vector2 playerPosition);
}

public class MouseAimInputProvider : IAimInputProvider
{
    private Camera MainRenderCamera => Managers.Camera.MainCamera;

    public Vector2 ResolveAimPosition(Vector2 userPosition)
    {
        if (Mouse.current == null)
            return userPosition;

        Camera mainCam = MainRenderCamera;
        if (mainCam == null)
            return userPosition;

        Vector2 mouseScreen = Mouse.current.position.ReadValue();
        Vector3 mouseWorld = MainRenderCamera.ScreenToWorldPoint(new Vector3(
            mouseScreen.x,
            mouseScreen.y,
            -MainRenderCamera.transform.position.z));

        // 2D 평면 z 정렬
        mouseWorld.z = 0;
        return mouseWorld;
    }
}
