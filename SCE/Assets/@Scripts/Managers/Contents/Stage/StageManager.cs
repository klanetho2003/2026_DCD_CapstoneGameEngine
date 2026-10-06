using DG.Tweening;
using System;
using System.Collections.Generic;
using UnityEngine;
using static Define;

/// <summary>
/// 스테이지 수명 관리 — MapData.Stages(JSON 소비).
///
/// 전환 규칙: 빙의 대상이 현재와 다른 스테이지의 셀에 들어서면 전환.
///           통로(어느 스테이지도 아닌 셀)는 현재 스테이지 유지. 클리어 여부는 전환을 막지 않는다.
/// 맵 전역 오브젝트는 맵 수명 — 스테이지 전환·사망 재시작과 무관.
/// </summary>
public class StageManager
{
    private static readonly Vector2Int NoCell = new Vector2Int(int.MinValue, int.MinValue);

    private Tween _restartTween;
    private readonly Action<CombatCreature> _onPlayerDied; // 1회 캐싱
    /// <summary>스테이지 진입 완료 후 (Load 이후). 구역별 설정을 적용하는 외부 시스템이 구독한다.</summary>
    public event Action<StageRunner> StageEntered;

    /// <summary>스테이지 이탈 직전 (Unload 이전). 적용했던 구역별 설정을 되돌린다.</summary>
    public event Action<StageRunner> StageExited;

    #region Stage
    private readonly Dictionary<int, StageRunner> _stages = new(); // Stage Key → runner
    public StageRunner CurrentStage { get; private set; }
    public bool IsRunning { get { return CurrentStage != null; } }
    #endregion

    #region Stage Region
    private readonly StageRegionGrid _regionGrid = new();
    private readonly List<int> _slotKeys = new(); // 슬롯 → Stage Key. 그리드 셀 값의 해석표이자 맵 데이터 순서
    private Vector2Int _lastCheckedCell = NoCell;  // 셀이 바뀔 때만 그리드 조회
    #endregion

    private readonly MapObjectSpawner _mapObjects = new();

    /// <summary>현재 맵의 스테이지별 진행도. 씬 전환(Clear)에도 유지되며, 다른 맵으로 Begin하면 초기화된다.</summary>
    public StageProgress Progress { get; private set; } = new();

    public event Action OnStageRestarted;

    public StageManager()
    {
        _onPlayerDied = OnPlayerDied;
    }

    #region Begin
    /// <summary>체크포인트가 있으면 거기서, 없으면 맵 데이터의 첫 스테이지에서 시작.</summary>
    public void Begin()
    {
        BeginInternal(hasStartKey: false, startKey: 0);
    }

    /// <summary>지정한 Stage Key에서 시작. 없으면 경고 후 Begin()과 같은 규칙.</summary>
    public void Begin(int startKey)
    {
        BeginInternal(hasStartKey: true, startKey: startKey);
    }

    private void BeginInternal(bool hasStartKey, int startKey)
    {
        var map = Managers.Map.CurrentMap;
        if (map == null || map.Stages == null || map.Stages.Count == 0)
        {
            LogPrinter.Log("[StageManager] MapData에 Stage 없음 — Export 확인");
            return;
        }

        StopRunning(); // 실행 중에 다시 Begin되면 기존 스테이지·맵 오브젝트 정리

        // 순서 불변식: 진행도 초기화(EnsureMap)가 StageRunner 생성보다 먼저 — 러너가 버려진 레코드를 잡지 않도록
        Progress.EnsureMap(map.Name);

        _stages.Clear();
        _slotKeys.Clear();
        for (int i = 0; i < map.Stages.Count; i++)
        {
            var info = map.Stages[i];
            if (_stages.ContainsKey(info.Key)) // Exporter가 막지만 손으로 고친 JSON 방어
            {
                LogPrinter.LogError($"[StageManager] Stage Key 중복 >> {info.Key} — 뒤의 것 무시");
                continue;
            }
            _stages.Add(info.Key, new StageRunner(info.Key, info, Progress.GetOrCreate(info.Key)));
            _slotKeys.Add(info.Key);
        }

        _regionGrid.Build(map, _slotKeys); // 실패해도 진행. 전환만 비활성
        _mapObjects.Spawn(map);

        EnterStage(ResolveStartStage(hasStartKey, startKey));
    }

    private StageRunner ResolveStartStage(bool hasStartKey, int startKey)
    {
        if (hasStartKey)
        {
            if (_stages.TryGetValue(startKey, out StageRunner requested))
                return requested;
            LogPrinter.LogWarning($"[StageManager] 시작 Stage {startKey} 없음 — 체크포인트/첫 스테이지로 대체");
        }

        if (Progress.HasCheckpoint && _stages.TryGetValue(Progress.CheckpointStageKey, out StageRunner checkpoint))
            return checkpoint;

        return _stages[_slotKeys[0]];
    }
    #endregion

    #region Enter / Transition
    private void EnterStage(StageRunner stage)
    {
        CurrentStage = stage;
        _lastCheckedCell = NoCell; // 스폰·재시작으로 위치가 바뀌었을 수 있으므로 다음 프레임 강제 검사

        // SpawnPoint가 있는 스테이지만 재시작 지점이 될 수 있다
        if (stage.HasPlayerSpawn)
            Progress.SetCheckpoint(stage.StageKey);

        var currentVillager = Managers.Object.PossessedTarget;
        if (currentVillager.IsValid() == false && stage.HasPlayerSpawn)
            currentVillager = SpawnPlayer(stage.PlayerSpawnInfo);

        HookPlayerDeath(currentVillager); // 활성 Player는 1명만 있는 것을 전제

        stage.Load();
        OnStageEntered(stage);      // 내부 처리 먼저
        StageEntered?.Invoke(stage); // 외부 구독자
    }

    /// <summary>현재 스테이지 이탈의 유일한 경로. 진입의 역순: 외부 구독자 → 내부 처리 → Unload.</summary>
    private void ExitCurrentStage()
    {
        StageRunner exiting = CurrentStage;
        if (exiting == null)
            return;

        StageExited?.Invoke(exiting); // 스테이지가 아직 활성인 상태에서 정리할 수 있게
        OnStageExiting(exiting);
        exiting.Unload();
        CurrentStage = null;
    }

    private void TransitionTo(StageRunner next)
    {
        LogPrinter.Log($"[StageManager] Stage {(CurrentStage != null ? CurrentStage.StageKey.ToString() : "-")} → {next.StageKey}");
        ExitCurrentStage();
        EnterStage(next);
    }

    /// <summary>Managers.Update가 구동. 매 프레임 비용 = 셀 비교 1회, 셀이 바뀔 때만 그리드 조회 O(1).</summary>
    public void OnUpdate()
    {
        if (_stages.Count == 0)
            return;
        if (_restartTween != null) // 재시작 대기 중 — 시체가 밀려 다른 영역으로 들어가도 전환하지 않는다
            return;

        var villager = Managers.Object.PossessedTarget;
        if (villager.IsValid() == false || villager.IsDead)
            return;

        Vector2Int cell = Managers.Map.WorldToCell(villager.transform.position);
        if (cell == _lastCheckedCell)
            return;
        _lastCheckedCell = cell;

        int slot = _regionGrid.GetSlot(cell);
        if (slot == StageRegionGrid.NoSlot)
            return; // 통로 — 현재 스테이지 유지

        StageRunner next = _stages[_slotKeys[slot]];
        if (next == CurrentStage)
            return;

        TransitionTo(next);
    }
    #endregion

    #region Stage Hooks
    /// <summary>
    /// 스테이지 진입 직후 내부 처리. StageManager가 직접 책임지는 구역별 처리만 여기에 둔다.
    /// 다른 시스템(입력, UI, 사운드)의 처리는 StageEntered 이벤트 구독 쪽에 둘 것 — StageManager가 그 시스템을 알게 되면 안 된다.
    /// </summary>
    private void OnStageEntered(StageRunner stage)
    {
        // 사용 예시:
        Data.StageSettingsData settings = stage.Settings;
        if (settings.ZoneType == EZoneType.Village)
        {
            // 마을에서는 사망 재시작 대신 제자리 부활 등, 스테이지 규칙 자체가 다른 경우
            Managers.Object.PossessedTarget.StateMachine.SetState(EUserInputState.Movement);
        }
        else
        {
            Managers.Object.PossessedTarget.StateMachine.SetState(EUserInputState.Combat);
        }
    }

    /// <summary>스테이지 이탈 직전 내부 처리. OnStageEntered에서 적용한 것을 되돌린다.</summary>
    private void OnStageExiting(StageRunner stage)
    {
        
    }
    #endregion

    #region Player Death & Restart
    private void HookPlayerDeath(Villager villager)
    {
        if (villager.IsValid() == false)
            return;

        villager.SetDeathListener(_onPlayerDied); // SetInfo가 리스너를 리셋하므로 진입마다 재주입
    }

    private void OnPlayerDied(CombatCreature _)
    {
        if (_restartTween != null) // 중복 통지 무시
            return;

        LogPrinter.Log($"[StageManager] Player 사망 — {RESTART_DELAY}s 후 재시작");
        _restartTween = DOVirtual.DelayedCall(RESTART_DELAY, RestartFromCheckpoint);
    }

    private void RestartFromCheckpoint()
    {
        _restartTween = null;
        
        ExitCurrentStage(); // 진행도는 웨이브 시작 시점에 이미 기록되어 있다
        RemoveDeadPlayer();

        if (Progress.HasCheckpoint == false || _stages.TryGetValue(Progress.CheckpointStageKey, out StageRunner checkpoint) == false)
        {
            LogPrinter.LogError("[StageManager] 체크포인트 없음 — 재시작 불가 (SpawnPoint가 있는 스테이지에 한 번도 진입하지 않음)");
            return;
        }

        EnterStage(checkpoint); // to do 마을로 가기로 변경
        OnStageRestarted?.Invoke();
    }

    /// <summary>죽은 빙의 대상 제거. 빙의 해제가 디스폰보다 먼저여야 InputManager.Release가 살아 있는 대상에게 호출된다.</summary>
    private void RemoveDeadPlayer()
    {
        var villager = Managers.Object.PossessedTarget;
        if (villager.IsValid() == false || villager.IsDead == false)
            return;

        Managers.Object.SetPossession(null);
        Managers.Object.Despawn(villager);
    }
    #endregion

    private Villager SpawnPlayer(SpawnInfo info)
    {
        var cellPos = Managers.Map.CellCenterToWorld(info.Cell);
        var player = Managers.Object.Spawn<Villager>(info.DataId, cellPos);

        Managers.Object.SetPossession(player);

        return player;
    }

    #region Clear
    /// <summary>실행 중인 스테이지·맵 전역 오브젝트·예약된 재시작을 정리. 진행도는 건드리지 않는다.</summary>
    private void StopRunning()
    {
        _restartTween?.Kill();
        _restartTween = null;

        ExitCurrentStage();

        _mapObjects.Despawn();
        _lastCheckedCell = NoCell;
    }

    /// <summary>씬 전환 시. 진행도는 유지 — 같은 맵으로 돌아오면 이어서 진행한다.</summary>
    public void Clear()
    {
        StopRunning();

        _stages.Clear();
        _regionGrid.Clear();
        _slotKeys.Clear();
    }
    #endregion
}