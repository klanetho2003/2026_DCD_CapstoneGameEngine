using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 카메라 화면이 현재 라운드 영역(Managers.Map.Area)의 최소 사각형 밖을 비추지 않게 한다.
/// CinemachineCamera에 붙는 확장 — CameraManager.Register가 코드로 붙인다.
///
/// - 경계의 원본은 MapManager.Area. 매 프레임 참조만 비교하고 바뀌었을 때만 사각형을 다시 계산한다.
/// - 화면 크기는 매 프레임 렌즈에서 읽는다 — 해상도·화면비가 바뀌어도 따로 처리할 것이 없다.
/// - 영역이 화면보다 작은 축은 영역 가운데에 고정한다.
/// - 영역이 없으면(Stage 선택 중) 아무 일도 하지 않는다.
/// 비용: 프레임당 비교 몇 회 + Clamp 2회, 할당 없음.
/// </summary>
[AddComponentMenu("")] // 메뉴에서 숨김 — 코드가 붙인다
public sealed class CinemachineAreaConfiner : CinemachineExtension
{
    private TerrainArea _cachedArea; // 마지막으로 사각형을 계산한 영역
    private Rect _cachedBounds;
    private bool _warnedPerspective;

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        // Body 직후 = 추종(감쇠 포함) 위치가 정해진 시점.
        // Impulse 셰이크는 이후 단계에서 더해지므로 제한하지 않는다 — 경계에서도 흔들림이 살아 있다
        if (stage != CinemachineCore.Stage.Body)
            return;

        if (TryGetBounds(out Rect bounds) == false)
            return;

        Vector2 halfExtent = GetHalfExtent(in state.Lens);
        Vector3 position = state.GetCorrectedPosition();

        float x = ClampAxis(position.x, halfExtent.x, bounds.xMin, bounds.xMax);
        float y = ClampAxis(position.y, halfExtent.y, bounds.yMin, bounds.yMax);

        // z는 건드리지 않는다 (Cell Swizzle XYZ — 월드 XY 평면)
        state.PositionCorrection += new Vector3(x - position.x, y - position.y, 0f);
    }

    private bool TryGetBounds(out Rect bounds)
    {
        MapManager map = Managers.Map;
        TerrainArea area = map != null ? map.Area : null;
        if (area == null)
        {
            _cachedArea = null;
            bounds = default;
            return false;
        }

        if (ReferenceEquals(area, _cachedArea) == false) // 라운드가 바뀐 프레임에만 계산
        {
            _cachedArea = area;
            _cachedBounds = area.GetWorldRect(map.Grid);
        }

        bounds = _cachedBounds;
        return true;
    }

    /// <summary>화면의 반 너비·반 높이(월드 단위). 원근 카메라면 0 — 카메라 중심만 영역 안으로 제한한다.</summary>
    private Vector2 GetHalfExtent(in LensSettings lens)
    {
        if (lens.Orthographic == false)
        {
            if (_warnedPerspective == false)
            {
                _warnedPerspective = true;
                LogPrinter.LogWarning("[CinemachineAreaConfiner] 원근 카메라 — 화면 크기를 고려하지 않고 카메라 중심만 제한");
            }
            return Vector2.zero;
        }

        float aspect = lens.Aspect > 0f ? lens.Aspect : 1f; // 출력 카메라가 없는 순간의 방어
        float halfHeight = lens.OrthographicSize;
        return new Vector2(halfHeight * aspect, halfHeight);
    }

    /// <summary>화면이 [min, max] 안에 들어가도록 중심을 제한한다. 화면이 더 크면 가운데에 고정.</summary>
    private static float ClampAxis(float center, float halfExtent, float min, float max)
    {
        float low = min + halfExtent;
        float high = max - halfExtent;
        if (low > high)
            return (min + max) * 0.5f;

        return Mathf.Clamp(center, low, high);
    }
}