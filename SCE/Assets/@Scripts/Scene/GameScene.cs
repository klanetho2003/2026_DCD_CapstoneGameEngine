using Data;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using static Define;

public class GameScene : BaseScene
{
#if UNITY_EDITOR


    private readonly StageFlowPresenter _presenter = new StageFlowPresenter();


    private void Start()
    {
        if (Managers.Data.IsDataLoaded == false)
        {
            Managers.Scene.LoadScene(EScene.TitleScene);
            return;
        }

        _presenter.SetInfo();        // 순서: 구독이 먼저
        Managers.Stage.OpenSelect(); // 그 다음 첫 상태 진입

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
        _presenter.Clear();
    }

    private void OnPhaseChanged(EStagePhase previous, EStagePhase next)
    {
        StageManager stage = Managers.Stage;
        LogPrinter.Log($"[StageDebug] 통지 {previous} → {next} (Round {stage.CurrentRound}, Result {stage.Result}, 몬스터 {Managers.Object.Monsters.Count})");
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.f7Key.wasPressedThisFrame) Managers.Stage.DebugSkipRound();
        if (keyboard.f8Key.wasPressedThisFrame) Managers.Stage.DebugFailStage();
    }

    public override void Clear()
    {
        
    }

    #region Debug
    private void OnGUI()
    {
        StageManager stage = Managers.Stage;
        GUI.Label(new Rect(10, 10, 600, 24),
            $"{stage.Phase} | Round {stage.CurrentRound} | 남은 {stage.RoundRemainingSeconds:F1}s");
    }

    /// <summary>현재 라운드의 영역을 초록 테두리로 그린다.</summary>
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