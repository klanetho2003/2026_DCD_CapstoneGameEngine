using Data;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using static Define;

public class GameScene : BaseScene
{
#if UNITY_EDITOR

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

    private void Update()
    {
        _roundRunner.Tick(Time.deltaTime);

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.f1Key.wasPressedThisFrame) EnterStage();
        if (keyboard.f2Key.wasPressedThisFrame) BeginRound(ERound.A);
        if (keyboard.f3Key.wasPressedThisFrame) BeginRound(ERound.B);
        if (keyboard.f4Key.wasPressedThisFrame) BeginRound(ERound.C);
        if (keyboard.f5Key.wasPressedThisFrame) ExitStage();
        if (keyboard.f7Key.wasPressedThisFrame) _roundRunner.Tick(DEBUG_ROUND_SECONDS); // 라운드 길이만큼 한 번에 진행
    }


    private void EnterStage()
    {
        ExitStage();

        if (Managers.Map.LoadMap(MAP_DATA_KEY, MAP_PREFAB_KEY) == false)
            return;

        if (StagePlayData.TryBuild(Managers.Map.CurrentMap, out _playData, out string error) == false)
        {
            LogPrinter.LogError($"[StageDebug] TryBuild 실패 — {error}");
            Managers.Map.UnloadMap();
            return;
        }

        Vector3 spawnPosition = Managers.Map.CellCenterToWorld(_playData.PlayerSpawn.Cell);
        _player = Managers.Object.Spawn<Villager>(_playData.PlayerSpawn.DataId, spawnPosition);
        if (_player != null)
        {
            Managers.Object.SetPossession(_player);
            _player.StateMachine.SetState(EUserInputState.Combat);
        }

        LogPrinter.Log("[StageDebug] Stage 진입 — F2 / F3 / F4로 라운드 시작");
    }

    private void BeginRound(ERound round)
    {
        if (_playData == null)
        {
            LogPrinter.LogWarning("[StageDebug] 먼저 F1로 Stage에 진입");
            return;
        }

        Managers.Map.ShowRound(round);
        _roundRunner.Begin(_playData.GetRound(round), DEBUG_ROUND_SECONDS);
        LogPrinter.Log($"[StageDebug] Round {round} 시작 — {DEBUG_ROUND_SECONDS}s");
    }

    private void OnRoundEnded()
    {
        LogPrinter.Log("[StageDebug] 라운드 종료 통지");
    }

    private void ExitStage()
    {
        _roundRunner.Stop();

        if (_player.IsValid())
        {
            Managers.Object.SetPossession(null); // 빙의 해제가 디스폰보다 먼저
            Managers.Object.Despawn(_player);
        }
        _player = null;
        _playData = null;

        Managers.Map.UnloadMap();
    }

    public override void Clear()
    {
        
    }
}
#endif