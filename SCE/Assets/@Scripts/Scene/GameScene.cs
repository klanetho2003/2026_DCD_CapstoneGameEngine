using UnityEngine;
using static Define;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 게임 Scene. Stage 흐름의 UI 배선만 소유한다 — 진행은 StageManager, 화면 전환은 StageFlowPresenter.
/// 들어올 때: Presenter 구독 → OpenSelect. 떠날 때(Clear): Presenter 해제 → StageManager 정리.
/// </summary>
public class GameScene : BaseScene
{
    private readonly StageFlowPresenter _presenter = new StageFlowPresenter();

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        SceneType = EScene.GameScene;
        return true;
    }

    private void Start()
    {
        // 데이터 로드 없이 GameScene에서 바로 Play한 경우
        if (Managers.Data.IsDataLoaded == false)
        {
            Managers.Scene.LoadScene(EScene.TitleScene);
            return;
        }

        _presenter.SetInfo();        // 순서: 구독이 먼저
        Managers.Stage.OpenSelect(); // 그 다음 첫 상태 진입
    }

    /// <summary>
    /// SceneManagerEx.Clear가 Pool·UI 정리보다 먼저 호출한다.
    /// 이 순서라야 StageManager가 살아 있는 풀로 디스폰한다.
    /// </summary>
    public override void Clear()
    {
        _presenter.Clear();     // 구독 해제가 먼저 — 정리 중에 UI 콜백이 오지 않도록
        Managers.Stage.Clear(); // 진행 중인 Stage 정리 + Phase None — 다음 진입 때 OpenSelect가 통한다
    }

    private void OnDestroy()
    {
        _presenter.Clear(); // 안전망 — 두 번 불려도 안전
    }

#if UNITY_EDITOR
    #region Debug — Step 7에서 정리
    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.f7Key.wasPressedThisFrame) Managers.Stage.DebugSkipRound();
        if (keyboard.f8Key.wasPressedThisFrame) Managers.Stage.DebugFailStage();
    }

    private void OnGUI()
    {
        StageManager stage = Managers.Stage;
        GUI.Label(new Rect(10, 10, 600, 24),
            $"{stage.Phase} | Round {stage.CurrentRound} | 남은 {stage.RoundRemainingSeconds:F1}s");
    }

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
#endif
}