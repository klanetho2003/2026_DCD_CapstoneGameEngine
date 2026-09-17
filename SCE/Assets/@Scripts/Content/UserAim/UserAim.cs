using Unity.Cinemachine;
using UnityEngine;
using static LogPrinter;

/// <summary>
/// Player의 시선 방향(LookDirection) 책임자
/// 
/// 1) 입력 소스 >> Aim 월드 위치: IAimInputProvider가 결정 (외부 주입)
/// 2) Aim 위치 >> LookDirection: 본 클래스가 8방향 snap + dead zone 적용 (공통 로직)
/// _aimMarker는 코드로 동적 생성
/// - 게임 디자인 상수는 const로 명시 — 캐릭터별 차이 필요해지면 CreatureData로 이주
/// - 입력 소스 교체 가능 — SetAimInputProvider 외부 주입 진입점
/// </summary>
public class UserAim : InitBase
{
    // To Do 캐릭터별 차이 필요 시 CreatureData / AimConfigSO로 이주.
    private const bool SNAP_TO_8_DIRECTION = true;
    private const float DEAD_ZONE_RADIUS = 0.2f;
    private const float DEAD_ZONE_RADIUS_SQ = DEAD_ZONE_RADIUS * DEAD_ZONE_RADIUS;
    private const string AIM_MARKER_NAME = "Aim";

    private Villager _owner;
    private CinemachineCamera _mainCamera => Managers.Camera.FollowCamera;

    // 동적 생성되는 Aim GameObject
    private Transform _aimMarker;

    // 입력 소스 — 외부 주입 가능
    private IAimInputProvider _aimProvider;

    private Vector2 _lookDir = Vector2.down;
    public Vector2 LookDirection { get { return _lookDir; } }

    /// <summary>
    /// _aimMarker의 월드 위치. 조준점 UI, AI, 무기 시스템 등 외부에서 조회.
    /// </summary>
    public Vector3 AimWorldPosition { get { return _aimMarker != null ? _aimMarker.position : transform.position; } }

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        return true;
    }

    public void SetInfo(Villager owner)
    {
        _owner = owner;

        if (_mainCamera == null)
            LogError("[UserAim] Camera.main이 null. Scene의 'MainCamera' 태그를 확인하세요.");

        // 기본 입력 소스: 마우스
        if (_aimProvider == null)
        {
            var handleProvider = new MouseAimInputProvider();
            SetAimInputProvider(handleProvider);
        }

        EnsureAimMarker();
    }

    /// <summary>
    /// 외부에서 입력 소스를 교체.
    /// 예시: 게임패드 감지 시 GamepadAimInputProvider, AI 모드 진입 시 AutoAimInputProvider로 스왑.
    /// </summary>
    public void SetAimInputProvider(IAimInputProvider provider)
    {
        if (provider == null)
        {
            LogError("[PlayerAim] AimInputProvider는 null일 수 없습니다.");
            return;
        }
        _aimProvider = provider;
    }

    /// <summary>
    /// _aimMarker GameObject를 Player의 자식으로 동적 생성
    /// </summary>
    private void EnsureAimMarker()
    {
        if (_aimMarker != null)
            return;

        var aimGO = new GameObject(AIM_MARKER_NAME);
        aimGO.transform.SetParent(_owner.transform, worldPositionStays: false);
        aimGO.transform.localPosition = Vector3.zero;

        _aimMarker = aimGO.transform;
    }

    private void Update()
    {
        if (_owner == null || _owner.IsDead)
            return;
        if (_aimMarker == null)
            return;
        if (_aimProvider == null)
            return;

        // 입력 소스 >> Aim 월드 위치
        UpdateAimPosition();

        // Aim 위치 >> LookDirection
        UpdateLookDirection();
    }

    /// <summary>
    /// 현재 주입된 _aimProvider에게 Aim 위치를 위임
    /// </summary>
    private void UpdateAimPosition()
    {
        _aimMarker.position = _aimProvider.ResolveAimPosition(_owner.transform.position);
    }

    /// <summary>
    /// _aimMarker 위치 기반 LookDirection 계산
    /// </summary>
    private void UpdateLookDirection()
    {
        Vector2 dirFromPlayer = (Vector2)_aimMarker.position - (Vector2)_owner.transform.position;

        // Dead zone — sqrMagnitude로 sqrt 회피
        if (dirFromPlayer.sqrMagnitude < DEAD_ZONE_RADIUS_SQ)
            return;

        _lookDir = SNAP_TO_8_DIRECTION
            ? VectorUtil.SnapTo8Direction(dirFromPlayer)
            : dirFromPlayer.normalized;
    }
}