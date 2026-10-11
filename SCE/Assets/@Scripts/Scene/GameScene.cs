using Data;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using static Define;

public class GameScene : BaseScene
{
#if UNITY_EDITOR

    private readonly StageCatalog _catalog = new StageCatalog();
    private int _stageIndex;

    private bool _wasOutside;

    // 검증할 Stage의 Addressable 키로 바꿔서 사용
    private const string MAP_DATA_KEY = "DevMapData";
    private const string MAP_PREFAB_KEY = "DevMap";

    private const float DEBUG_ROUND_SECONDS = 30f; // 3분을 기다리지 않도록 짧게

    private readonly RoundRunner _roundRunner = new RoundRunner();
    private StagePlayData _playData;
    private Villager _player;

    private void Start()
    {
        if (Managers.Data.IsDataLoaded == false)
        {
            Managers.Scene.LoadScene(EScene.TitleScene);
            return;
        }

        _catalog.Load();
        Managers.Stage.PhaseChanged += OnPhaseChanged;
        Managers.Stage.OpenSelect();

        //_roundRunner.SetInfo(OnRoundEnded);

        /*Managers.Map.LoadMap("DevMapData");
        Managers.Stage.Begin();*/

        // Managers.UI.ShowPopupUI<UI_DayTransition>();
        /*var handle = Managers.UI.ShowPopupUI<UI_FocusGroup>();
        handle.SetInfo();
        handle.OnClosed();
        Managers.UI.ClosePopupUI();*/

        /*for (int i = 0; i < 11; i++)
        {
            var mosnter = Managers.Object.Spawn<MonsterBase>(0, new Vector2(i+5, 0)); // test monster
        }*/
    }

    private void OnDestroy()
    {
        if (Managers.Stage != null)
            Managers.Stage.PhaseChanged -= OnPhaseChanged; // 씬 객체의 구독은 반드시 해제
    }

    private void OnPhaseChanged(EStagePhase previous, EStagePhase next)
    {
        StageManager stage = Managers.Stage;
        LogPrinter.Log($"[StageDebug] 통지 {previous} → {next} (Round {stage.CurrentRound}, Result {stage.Result}, 몬스터 {Managers.Object.Monsters.Count})");
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || _catalog.IsLoaded == false)
            return;

        if (keyboard.tabKey.wasPressedThisFrame)
        {
            _stageIndex = (_stageIndex + 1) % _catalog.Stages.Count;
            LogPrinter.Log($"[StageDebug] 선택: {_catalog.Stages[_stageIndex].DisplayName}");
        }
        if (keyboard.f1Key.wasPressedThisFrame) Managers.Stage.RequestEnter(_catalog.Stages[_stageIndex]);
        if (keyboard.f3Key.wasPressedThisFrame) Managers.Stage.RequestProceed();
        if (keyboard.f7Key.wasPressedThisFrame) Managers.Stage.DebugSkipRound();
        if (keyboard.f8Key.wasPressedThisFrame) Managers.Stage.DebugFailStage();

        // 영역 경계를 넘을 때만 로그
        if (_player.IsValid() && Managers.Map.Area != null)
        {
            bool outside = Managers.Map.IsOutsideArea(Managers.Map.WorldToCell(_player.transform.position));
            if (outside != _wasOutside)
            {
                _wasOutside = outside;
                LogPrinter.Log(outside ? "[StageDebug] 플레이어가 영역 밖으로 나감" : "[StageDebug] 플레이어가 영역 안으로 들어옴");
            }
        }
    }


    

    private void OnRoundEnded()
    {
        LogPrinter.Log("[StageDebug] 라운드 종료 통지");
    }

    public override void Clear()
    {
        
    }

    #region Debug
    private void OnGUI()
    {
        StageManager stage = Managers.Stage;
        string selected = _catalog.IsLoaded ? _catalog.Stages[_stageIndex].DisplayName : "(목록 없음)";
        GUI.Label(new Rect(10, 10, 600, 24),
            $"{stage.Phase} | Round {stage.CurrentRound} | 남은 {stage.RoundRemainingSeconds:F1}s | 선택 {selected}");
    }

    /// <summary>현재 라운드의 영역을 초록 테두리로 그린다. Gizmos를 켜야 보인다.</summary>
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
    }
    #endregion
}
#endif