using System;
using System.Collections.Generic;
using Data;
using UnityEngine;
using Unity.Profiling;
using static Define;

/// <summary>
/// Stage 흐름과 Stage 한 판의 수명.
///
/// 흐름: Selecting → Playing(A) → RoundResult → Playing(B) → RoundResult → Playing(C) → StageResult → Selecting
///
/// 규칙
///   1. 화면에 무엇이 떠야 하는지의 원본은 Phase 하나다. UI는 PhaseChanged를 구독해 따라온다 (이 클래스는 UI를 모른다).
///   2. 외부 요청(RequestEnter, RequestProceed)과 사망 통지는 플래그만 세우고 OnUpdate에서 실행한다.
///      UI·입력 콜백이나 피해 처리 도중에 스폰·디스폰·맵 교체가 끼어들지 않게 하기 위함.
///   3. 상태 전이는 ChangePhase 한 곳. 순서: 내부 상태 완성 → PhaseChanged 통지.
///   4. 라운드 시작은 BeginRound 한 곳 — A·B·C가 같은 함수를 지난다.
///   5. Stage 한 판의 상태는 ResetSession 한 곳에서 초기화한다.
/// </summary>
public class StageManager
{
    private static readonly ProfilerMarker s_updateMarker = new ProfilerMarker("StageManager.OnUpdate");

    private const string LOG_CONTEXT = "Stage"; // 상시 오브젝트 스폰 에러 로그의 출처 표기
    private const ERound LAST_ROUND = (ERound)(ROUND_COUNT - 1);

    #region 공개 상태
    public EStagePhase Phase { get; private set; } = EStagePhase.None;
    public ERound CurrentRound { get; private set; }
    public EStageResult Result { get; private set; }

    /// <summary>진행 중인 Stage의 목록 항목. Stage 밖이면 null.</summary>
    public StageInfoData CurrentStageInfo { get; private set; }

    /// <summary>Stage 한 판이 진행 중인가 (맵과 플레이어가 존재하는 구간).</summary>
    public bool IsRunning
    {
        get { return Phase == EStagePhase.Playing || Phase == EStagePhase.RoundResult || Phase == EStagePhase.StageResult; }
    }

    public float RoundRemainingSeconds { get { return _roundRunner.RemainingSeconds; } }

    /// <summary>
    /// (이전 상태, 새 상태). 내부 상태가 모두 갱신된 뒤 호출된다.
    /// 이 매니저는 씬이 바뀌어도 살아 있으므로, 씬 객체가 구독했다면 씬을 떠날 때 반드시 해제할 것.
    /// </summary>
    public event Action<EStagePhase, EStagePhase> PhaseChanged;
    #endregion

    #region 내부 상태
    private readonly RoundRunner _roundRunner = new();
    private readonly List<CreatureBase> _persistentSpawns = new(); // Stage 수명 오브젝트 — 재사용
    private readonly Action<CombatCreature> _onPlayerDied;         // 1회 캐싱

    private StagePlayData _playData;
    private Villager _spawnedPlayer; // 진입 때 스폰한 Villager. 빙의가 다른 대상으로 옮겨가도 이탈 시 정리한다
    private Villager _deathHooked;   // 사망을 감시 중인 대상 (= 마지막으로 확인한 빙의 대상)

    // 요청 — 플래그만 세우고 OnUpdate에서 실행
    private StageInfoData _pendingEnter;
    private bool _pendingProceed;
    private bool _pendingFail;
    #endregion

    public StageManager()
    {
        _onPlayerDied = OnPlayerDied;
        _roundRunner.SetInfo(OnRoundEnded);
    }

    #region 요청 (외부 진입점)
    /// <summary>GameScene 진입 시 1회. Stage 선택 상태로 들어간다.</summary>
    public void OpenSelect()
    {
        if (Phase != EStagePhase.None)
        {
            LogPrinter.LogWarning($"[StageManager] OpenSelect 무시 — 현재 {Phase}");
            return;
        }
        ChangePhase(EStagePhase.Selecting);
    }

    /// <summary>Stage 선택 UI가 호출하는 트리거. 다음 OnUpdate에서 진입한다.</summary>
    public void RequestEnter(StageInfoData info)
    {
        if (Phase != EStagePhase.Selecting)
        {
            LogPrinter.LogWarning($"[StageManager] RequestEnter 무시 — 현재 {Phase}");
            return;
        }
        if (info == null)
        {
            LogPrinter.LogError("[StageManager] RequestEnter(null)");
            return;
        }
        _pendingEnter = info; // 같은 프레임에 여러 번 오면 마지막 것만 남는다
    }

    /// <summary>
    /// 결과 팝업의 버튼이 호출. 무엇이 일어날지는 현재 상태가 정한다 —
    /// RoundResult면 다음 라운드, StageResult면 Stage 선택으로.
    /// </summary>
    public void RequestProceed()
    {
        if (Phase != EStagePhase.RoundResult && Phase != EStagePhase.StageResult)
        {
            LogPrinter.LogWarning($"[StageManager] RequestProceed 무시 — 현재 {Phase}");
            return;
        }
        _pendingProceed = true;
    }
    #endregion

    #region Update

    /// <summary>Managers.Update가 구동. 매 프레임 비용 = 상태 분기 + (Playing일 때) 참조 비교 1회와 RoundRunner.Tick.</summary>
    public void OnUpdate()
    {
        using (s_updateMarker.Auto())
            UpdatePhase();
    }

    private void UpdatePhase()
    {
        switch (Phase)
        {
            case EStagePhase.Selecting:
                if (_pendingEnter != null)
                {
                    StageInfoData info = _pendingEnter;
                    _pendingEnter = null;
                    EnterStage(info);
                }
                break;

            case EStagePhase.Playing:
                if (_pendingFail)
                {
                    FailStage();
                    break;
                }
                UpdateDeathHook();
                _roundRunner.Tick(Time.deltaTime); // 시간이 다 되면 이 안에서 OnRoundEnded가 불린다
                break;

            case EStagePhase.RoundResult:
                if (_pendingFail) // 팝업이 떠 있는 동안 남은 투사체 등으로 사망한 경우
                {
                    FailStage();
                    break;
                }
                UpdateDeathHook();
                if (_pendingProceed)
                {
                    _pendingProceed = false;
                    BeginRound(CurrentRound + 1);
                }
                break;

            case EStagePhase.StageResult:
                if (_pendingProceed)
                {
                    _pendingProceed = false;
                    ExitStage();
                    ChangePhase(EStagePhase.Selecting);
                }
                break;
        }
    }
    #endregion

    #region Stage 진입 / 이탈
    /// <summary>실패하면 아무것도 남기지 않고 Selecting에 머문다 — 선택 UI가 그대로 남는다.</summary>
    private void EnterStage(StageInfoData info)
    {
        // 1. 맵 (데이터 + Stage Prefab)
        if (Managers.Map.LoadMap(info.MapDataKey, info.MapPrefabKey) == false)
        {
            LogPrinter.LogError($"[StageManager] Stage {info.StageId} 진입 실패 — 맵 로드 실패");
            return;
        }

        // 2. 실행용 데이터
        if (StagePlayData.TryBuild(Managers.Map.CurrentMap, out StagePlayData playData, out string error) == false)
        {
            LogPrinter.LogError($"[StageManager] Stage {info.StageId} 진입 실패 — {error}");
            Managers.Map.UnloadMap();
            return;
        }

        ResetSession();
        _playData = playData;
        CurrentStageInfo = info;

        // 3. 상시 오브젝트 → 4. 플레이어
        SpawnPersistent();
        if (SpawnPlayer() == false)
        {
            LogPrinter.LogError($"[StageManager] Stage {info.StageId} 진입 실패 — 플레이어 스폰 실패 (DataId={playData.PlayerSpawn.DataId})");
            ExitStage();
            return;
        }

        LogPrinter.Log($"[StageManager] Stage {info.StageId} '{info.DisplayName}' 진입");

        // 5. 라운드 A 즉시 시작 — 여기서 Playing으로 바뀐다
        BeginRound(ERound.A);
    }

    /// <summary>Stage 한 판을 정리한다. 진입의 역순: 라운드 → 플레이어 → 상시 오브젝트 → 맵.</summary>
    private void ExitStage()
    {
        _roundRunner.Stop();

        if (_deathHooked.IsValid())
            _deathHooked.SetDeathListener(null);

        // 빙의 해제가 디스폰보다 먼저 —
        // InputManager.Release가 살아 있는 대상에게 호출되고, DespawnAll이 빙의 대상을 건너뛰지 않는다
        Villager possessed = Managers.Object.PossessedTarget;
        Managers.Object.SetPossession(null);

        DespawnVillager(possessed);
        if (ReferenceEquals(_spawnedPlayer, possessed) == false)
            DespawnVillager(_spawnedPlayer);

        CreatureSpawnHelper.DespawnAll(_persistentSpawns);
        Managers.Map.UnloadMap();

        ResetSession();
    }

    /// <summary>Stage 한 판의 상태를 처음 값으로. 정리할 요소가 생기면 이 함수에 추가한다.</summary>
    private void ResetSession()
    {
        _pendingEnter = null;
        _pendingProceed = false;
        _pendingFail = false;

        _playData = null;
        _spawnedPlayer = null;
        _deathHooked = null;
        _persistentSpawns.Clear();

        CurrentStageInfo = null;
        CurrentRound = ERound.A;
        Result = EStageResult.Cleared;
    }

    private void SpawnPersistent()
    {
        IReadOnlyList<SpawnInfo> infos = _playData.Persistent;
        for (int i = 0; i < infos.Count; i++)
        {
            CreatureBase creature = CreatureSpawnHelper.Spawn(infos[i], LOG_CONTEXT);
            if (creature != null)
                _persistentSpawns.Add(creature);
        }
    }

    private bool SpawnPlayer()
    {
        SpawnInfo info = _playData.PlayerSpawn;
        Vector3 position = Managers.Map.CellCenterToWorld(info.Cell);

        Villager player = Managers.Object.Spawn<Villager>(info.DataId, position);
        if (player == null)
            return false;

        _spawnedPlayer = player;
        Managers.Object.SetPossession(player);

        // 구역 설정 적용 — 기존 StageManager.OnStageEntered의 내용
        EUserInputState inputState = _playData.Settings.ZoneType == EZoneType.Village
            ? EUserInputState.Movement
            : EUserInputState.Combat;
        player.StateMachine.SetState(inputState);

        return true;
    }

    private static void DespawnVillager(Villager villager)
    {
        if (villager.IsValid())
            Managers.Object.Despawn(villager);
    }
    #endregion

    #region 라운드
    /// <summary>라운드 시작의 유일한 경로 — A·B·C가 모두 이 함수를 지난다.</summary>
    private void BeginRound(ERound round)
    {
        CurrentRound = round;

        // 순서 1: 보이는 것과 영역을 이 라운드의 것으로. RoundRunner의 스폰 검사가 이 영역을 본다
        TerrainArea area = _playData.GetArea(round);
        Managers.Map.SetRound(round, area);

        // 순서 2: 플레이어가 새 영역 밖에 서 있으면 SpawnPoint로
        MovePlayerIntoArea(area);
        UpdateDeathHook();

        // 순서 3: 라운드 실행 — 시작 조건은 "호출 즉시"
        _roundRunner.Begin(_playData.GetRound(round), ROUND_DURATION_SECONDS);

        LogPrinter.Log($"[StageManager] Round {round} 시작 — {ROUND_DURATION_SECONDS}s, 영역 {area.CellCount}셀");
        ChangePhase(EStagePhase.Playing);
    }

    /// <summary>RoundRunner가 시간 종료 시 호출 (Tick 안). 몬스터 정리는 RoundRunner가 이미 끝낸 뒤다.</summary>
    private void OnRoundEnded()
    {
        if (CurrentRound == LAST_ROUND)
        {
            Result = EStageResult.Cleared;
            ChangePhase(EStagePhase.StageResult);
        }
        else
        {
            ChangePhase(EStagePhase.RoundResult);
        }
    }

    /// <summary>SpawnPoint는 모든 라운드의 영역 안이다 (Exporter와 StagePlayData.TryBuild가 보장).</summary>
    private void MovePlayerIntoArea(TerrainArea area)
    {
        Villager villager = Managers.Object.PossessedTarget;
        if (villager.IsValid() == false)
            return;

        Vector2Int cell = Managers.Map.WorldToCell(villager.transform.position);
        if (area.Contains(cell))
            return; // 영역 안이면 위치 유지

        TeleportPossessed(villager, Managers.Map.CellCenterToWorld(_playData.PlayerSpawn.Cell));
        LogPrinter.Log($"[StageManager] 플레이어가 Round {CurrentRound} 영역 밖 {cell} — SpawnPoint로 이동");
    }

    /// <summary>순간 이동의 유일한 경로. 프로젝트의 이동 방식에 맞춰 이 한 곳만 고친다.</summary>
    private static void TeleportPossessed(Villager villager, Vector3 worldPosition)
    {
        villager.transform.position = worldPosition; // 기존 StageManager와 같은 가정: Transform 직접 이동
    }
    #endregion

    #region 사망 / 실패
    /// <summary>사망 감시 대상을 현재 빙의 대상에 맞춘다. 바뀌지 않았으면 참조 비교 1회로 끝난다.</summary>
    private void UpdateDeathHook()
    {
        Villager possessed = Managers.Object.PossessedTarget;
        if (ReferenceEquals(possessed, _deathHooked))
            return;

        if (_deathHooked.IsValid())
            _deathHooked.SetDeathListener(null);

        _deathHooked = possessed;
        if (possessed.IsValid())
            possessed.SetDeathListener(_onPlayerDied);
    }

    /// <summary>피해 처리 도중에 불린다 — 여기서 디스폰·상태 전이를 하지 않고 예약만 한다.</summary>
    private void OnPlayerDied(CombatCreature _)
    {
        if (Phase != EStagePhase.Playing && Phase != EStagePhase.RoundResult)
            return; // 이미 끝나는 중이거나 Stage 밖

        _pendingFail = true;
    }

    private void FailStage()
    {
        _pendingFail = false;
        _pendingProceed = false;

        _roundRunner.Stop(); // 통지 없이 정리 — 실패는 "라운드가 끝났다"가 아니다
        Result = EStageResult.Failed;

        LogPrinter.Log($"[StageManager] Round {CurrentRound}에서 실패");
        ChangePhase(EStagePhase.StageResult);
    }
    #endregion

    #region 상태 전이 / 정리
    /// <summary>상태 전이의 유일한 경로. 호출 전에 내부 상태를 모두 맞춰 둘 것.</summary>
    private void ChangePhase(EStagePhase next)
    {
        EStagePhase previous = Phase;
        if (previous == next)
            return;

        Phase = next;
        LogPrinter.Log($"[StageManager] {previous} → {next}");
        PhaseChanged?.Invoke(previous, next);
    }

    /// <summary>
    /// 씬 전환 시. 진행 중인 Stage를 정리하고 처음 상태로 돌아간다.
    /// 통지하지 않는다 — 구독자인 씬 객체는 이미 정리 중이다.
    /// </summary>
    public void Clear()
    {
        if (IsRunning)
            ExitStage();
        else
            ResetSession();

        Phase = EStagePhase.None;
    }
    #endregion

#if UNITY_EDITOR
    #region 검증용 (Step 7에서 제거)
    /// <summary>현재 라운드를 즉시 시간 종료시킨다.</summary>
    public void DebugSkipRound()
    {
        if (Phase == EStagePhase.Playing)
            _roundRunner.Tick(ROUND_DURATION_SECONDS);
    }

    /// <summary>플레이어 사망과 같은 경로로 실패를 예약한다.</summary>
    public void DebugFailStage()
    {
        OnPlayerDied(null);
    }
    #endregion
#endif
}