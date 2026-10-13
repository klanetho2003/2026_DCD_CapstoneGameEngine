#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using static Define;

/// <summary>
/// 에디터 전용 Stage 검증 도구. GameScene이 에디터에서만 코드로 붙인다 — 씬에 저장되지 않는다.
///
/// F7  라운드 즉시 종료
/// F8  실패 (플레이어 사망과 같은 경로)
/// F10 자동 왕복 10회 시작/중지 — Stage를 번갈아 진입 → 라운드 A·B·C 즉시 종료 → 선택으로
///
/// 게임 종료 → Stage 선택으로 돌아올 때마다 남은 것이 없는지 자동으로 점검한다.
/// </summary>
public sealed class StageDebugTool : MonoBehaviour
{
    private const int AUTO_ROUND_TRIPS = 10;
    private const int STUCK_FRAME_LIMIT = 300;

    private readonly StageCatalog _catalog = new StageCatalog();
    private readonly List<string> _violations = new();

    // 자동 왕복
    private bool _autoRunning;
    private int _autoCompleted;
    private int _autoStageIndex;
    private EStagePhase _actedPhase = EStagePhase.None; // 마지막으로 행동한 상태 — 상태 진입마다 1번만 행동
    private int _framesInPhase;

    // 정리 점검
    private EStagePhase _observedPhase = EStagePhase.None;
    private int _cleanChecks;
    private int _cleanFailures;

    private void Start()
    {
        _catalog.Load();
    }

    private void Update()
    {
        StageManager stage = Managers.Stage;
        ObservePhase(stage.Phase);

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.f7Key.wasPressedThisFrame) stage.DebugSkipRound();
            if (keyboard.f8Key.wasPressedThisFrame) stage.DebugFailStage();
            if (keyboard.f10Key.wasPressedThisFrame) ToggleAuto();
        }

        if (_autoRunning)
            StepAuto(stage);
    }

    #region 정리 점검
    /// <summary>게임 종료 → Stage 선택 전이를 감지한다. 이 경로의 전이는 한 프레임에 하나뿐이라 폴링으로 충분하다.</summary>
    private void ObservePhase(EStagePhase current)
    {
        EStagePhase previous = _observedPhase;
        if (current == previous)
            return;
        _observedPhase = current;

        if (previous == EStagePhase.StageResult && current == EStagePhase.Selecting)
        {
            if (_autoRunning)
                _autoCompleted++;
            CheckCleanState();
        }
    }

    private void CheckCleanState()
    {
        _violations.Clear();

        if (Managers.Map.IsLoaded) _violations.Add("맵 데이터가 남아 있음");
        if (Managers.Map.Area != null) _violations.Add("라운드 영역이 남아 있음");
        if (Managers.Object.PossessedTarget != null) _violations.Add("빙의 대상이 남아 있음");
        if (Managers.Object.Monsters.Count > 0) _violations.Add($"Monster {Managers.Object.Monsters.Count}개");
        if (Managers.Object.NPCs.Count > 0) _violations.Add($"NPC {Managers.Object.NPCs.Count}개");
        if (Managers.Object.Villagers.Count > 0) _violations.Add($"Villager {Managers.Object.Villagers.Count}개");
        if (Managers.Stage.CurrentStageInfo != null) _violations.Add("Stage 정보가 남아 있음");
        if (Managers.UI.GetPopupCount() > 0) _violations.Add($"Popup {Managers.UI.GetPopupCount()}개가 열려 있음");
        if (Managers.Input.HasOverlay == false) _violations.Add("선택 UI의 입력 오버레이가 없음");

        _cleanChecks++;
        if (_violations.Count == 0)
        {
            LogPrinter.Log($"[StageDebug] 정리 점검 {_cleanChecks}회차 통과");
            return;
        }

        _cleanFailures++;
        LogPrinter.LogError($"[StageDebug] 정리 점검 {_cleanChecks}회차 실패 — {string.Join(", ", _violations)}");
    }
    #endregion

    #region 자동 왕복
    private void ToggleAuto()
    {
        if (_autoRunning)
        {
            _autoRunning = false;
            LogPrinter.Log($"[StageDebug] 자동 왕복 중지 — {_autoCompleted}/{AUTO_ROUND_TRIPS}회 완료");
            return;
        }

        if (_catalog.IsLoaded == false || Managers.Stage.Phase != EStagePhase.Selecting)
        {
            LogPrinter.LogWarning("[StageDebug] 자동 왕복은 Stage 선택 화면에서 시작");
            return;
        }

        _autoRunning = true;
        _autoCompleted = 0;
        _autoStageIndex = 0;
        _actedPhase = EStagePhase.None;
        _framesInPhase = 0;
        LogPrinter.Log($"[StageDebug] 자동 왕복 시작 — {AUTO_ROUND_TRIPS}회");
    }

    private void StepAuto(StageManager stage)
    {
        EStagePhase phase = stage.Phase;
        if (phase == _actedPhase)
        {
            // 요청은 다음 OnUpdate에서 처리되므로 몇 프레임은 같은 상태가 정상이다
            if (++_framesInPhase > STUCK_FRAME_LIMIT)
            {
                _autoRunning = false;
                LogPrinter.LogError($"[StageDebug] 자동 왕복 중단 — {phase}에서 {STUCK_FRAME_LIMIT}프레임 동안 진행 없음 ({_autoCompleted}회 완료)");
            }
            return;
        }

        _actedPhase = phase;
        _framesInPhase = 0;

        switch (phase)
        {
            case EStagePhase.Selecting:
                if (_autoCompleted >= AUTO_ROUND_TRIPS)
                {
                    _autoRunning = false;
                    LogPrinter.Log($"[StageDebug] 자동 왕복 완료 — {AUTO_ROUND_TRIPS}회, 정리 점검 실패 {_cleanFailures}회");
                    return;
                }
                stage.RequestEnter(_catalog.Stages[_autoStageIndex % _catalog.Stages.Count]); // Stage를 번갈아 진입
                _autoStageIndex++;
                break;

            case EStagePhase.Playing:
                stage.DebugSkipRound(); // 스폰 직후 정리 — 풀 반환·재사용 경로를 매 라운드 지난다
                break;

            case EStagePhase.RoundResult:
            case EStagePhase.StageResult:
                stage.RequestProceed(); // 버튼 없이 진행 — 팝업은 Presenter의 Dismiss 경로로 닫힌다
                break;
        }
    }
    #endregion

    #region 표시
    /*private void OnGUI()
    {
        StageManager stage = Managers.Stage;
        GUI.Label(new Rect(10, 10, 900, 24),
            $"{stage.Phase} | Round {stage.CurrentRound} | 남은 {stage.RoundRemainingSeconds:F1}s | " +
            $"Monster {Managers.Object.Monsters.Count} NPC {Managers.Object.NPCs.Count} Villager {Managers.Object.Villagers.Count}");
        GUI.Label(new Rect(10, 34, 900, 24),
            $"자동 왕복 {(_autoRunning ? "실행 중" : "꺼짐")} {_autoCompleted}/{AUTO_ROUND_TRIPS} | 정리 점검 {_cleanChecks}회 중 실패 {_cleanFailures}회");
    }*/

    /// <summary>현재 라운드의 영역(초록 셀)과 카메라 경계(노란 사각형)를 그린다.</summary>
    private void OnDrawGizmos()
    {
        if (Application.isPlaying == false || Managers.Map.Area == null)
            return;

        TerrainArea area = Managers.Map.Area;
        RectInt bounds = area.Bounds;
        float cellSize = Managers.Map.Grid.CellSize;
        Vector3 size = new Vector3(cellSize, cellSize, 0f) * 0.9f;

        Gizmos.color = Color.green;
        for (int y = bounds.yMin; y < bounds.yMax; y++)
        {
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                var cell = new Vector2Int(x, y);
                if (area.Contains(cell))
                    Gizmos.DrawWireCube(Managers.Map.CellCenterToWorld(cell), size);
            }
        }

        Rect cameraBounds = area.GetWorldRect(Managers.Map.Grid);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(cameraBounds.center, new Vector3(cameraBounds.width, cameraBounds.height, 0f));
    }
    #endregion
}
#endif