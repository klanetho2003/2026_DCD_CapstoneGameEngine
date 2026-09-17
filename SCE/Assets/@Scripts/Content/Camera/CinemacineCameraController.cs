using Unity.Cinemachine;
using UnityEngine;

[RequireComponent(typeof(CinemachineCamera))]
public class CinemacineCameraController : InitBase
{
    private CinemachineCamera _cam;

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        _cam = GetComponent<CinemachineCamera>();

        return true;
    }

    private void OnEnable() { Managers.Camera?.Register(_cam); }
    private void OnDisable() { Managers.Camera?.Unregister(_cam); }
}