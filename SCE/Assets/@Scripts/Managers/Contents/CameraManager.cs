using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Scene의 CinemachineCamera에서 스스로 등록
/// </summary>
public class CameraManager
{
    public Camera MainCamera { get { return Camera.main; } }
    public CinemachineCamera FollowCamera { get; private set; }
    private CinemachineImpulseSource _impulseSource;
    private bool _impulseTimeScaleSet;

    public void Register(CinemachineCamera cam)
    {
        FollowCamera = cam;
        _impulseSource = cam.GetComponent<CinemachineImpulseSource>();

        // 히트스톱(timeScale=0) 중에도 셰이크가 감쇠하도록
        if (_impulseTimeScaleSet == false)
        {
            CinemachineImpulseManager.Instance.IgnoreTimeScale = true;
            _impulseTimeScaleSet = true;
        }
    }

    public void Unregister(CinemachineCamera cam)
    {
        if (FollowCamera == cam) FollowCamera = null;
    }

    /// <summary>Player 스폰/리스폰 시 호출 — 풀 재사용으로 Transform이 바뀌어도 재주입으로 해결.</summary>
    public void SetFollowTarget(Transform target)
    {
        if (FollowCamera == null)
        {
            LogPrinter.Log("[CameraManager] CinemachineCamera 미등록 — Scene 구성 확인");
            return;
        }

        FollowCamera.Target.TrackingTarget = target;
    }

    /// <summary>
    /// 타격 셰이크 — worldPos에서 direction 방향으로 strength 크기의 충격.
    /// direction이 zero면 아래 방향 기본 충격
    /// </summary>
    public void Shake(float strength, Vector3 worldPos, Vector3 direction)
    {
        if (_impulseSource == null || strength <= 0f) return;

        Vector3 velocity = direction.sqrMagnitude > 0.0001f
            ? direction.normalized * strength
            : Vector3.down * strength;

        _impulseSource.GenerateImpulseAt(worldPos, velocity);
    }
}