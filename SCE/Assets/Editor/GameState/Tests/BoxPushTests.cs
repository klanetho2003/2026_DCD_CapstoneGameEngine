using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Define;

/// <summary>실제 2D 물리와 상호작용 노드로 박스 밀기, 차단, 취소 및 입력 복귀를 검증한다.</summary>
public partial class MonsterMovementIntegrationTests
{
    /// <summary>상하좌우 모두 플레이어와 박스가 정확히 한 칸 이동하는지 확인한다.</summary>
    /// <param name="x">검증할 밀기 방향의 가로 성분이다.</param>
    /// <param name="y">검증할 밀기 방향의 세로 성분이다.</param>
    [TestCase(1, 0), TestCase(-1, 0), TestCase(0, 1), TestCase(0, -1)]
    public void BoxPush_FourDirectionsFinishAtCellCenters(int x, int y)
    {
        SetMap(5, 5);
        // direction은 이번 테스트에서 박스를 밀 상하좌우 방향이다.
        Vector2Int direction = new(x, y);
        // player는 실제 이동과 충돌체를 가진 빙의 플레이어다.
        Villager player = CreateBoxPlayer(new Vector2Int(2, 2));
        // box는 플레이어 앞 한 칸에서 시작할 실제 물리 박스다.
        GridBox box = CreateGridBox(new Vector2Int(2, 2) + direction);
        player.Move(direction);
        Assert.IsTrue(player.BoxPush.TryPush());
        player.Move(Vector2.zero);
        TickBoxPush(50);
        Assert.AreEqual(new Vector2Int(2, 2) + direction, player.Movement.CurrentCell);
        Assert.AreEqual(new Vector2Int(2, 2) + direction * 2, box.CurrentCell);
        Assert.Less(Vector2.Distance(box.Position, _map.CellCenterToWorld(box.CurrentCell)), 0.001f);
        Assert.IsFalse(player.BoxPush.IsPushing);
        Assert.IsFalse(player.Movement.IsMoving);
        Assert.IsTrue(_map.CanEnterCell(new Vector2Int(2, 2), null));
        Assert.IsFalse(_map.CanEnterCell(box.CurrentCell, null));
    }

    /// <summary>방향키 유지와 Tick/Fire 조립만으로 여러 칸을 밀다가 맵 끝에서 멈추는지 확인한다.</summary>
    [Test]
    public void BoxPush_AssembledTickInteractionRepeatsUntilMapEdge()
    {
        SetMap(6, 3);
        // player는 별도의 상호작용 키 없이 방향키만 유지할 플레이어다.
        Villager player = CreateBoxPlayer(new Vector2Int(0, 1));
        // box는 맵 끝까지 연속으로 밀려갈 박스다.
        GridBox box = CreateGridBox(new Vector2Int(1, 1));
        // interaction은 프로젝트 JSON을 수정하지 않고 메모리에서만 조립한 검증용 카드다.
        InteractionComponent interaction = CreateBoxInteraction(player);
        player.Move(Vector2.right);
        TickBoxPush(160, interaction);
        Assert.AreEqual(new Vector2Int(4, 1), player.Movement.CurrentCell);
        Assert.AreEqual(new Vector2Int(5, 1), box.CurrentCell);
        Assert.IsFalse(player.BoxPush.IsPushing);
    }

    /// <summary>상호작용 카드를 조립하기 전에는 기본 이동만으로 박스를 통과하거나 밀지 않는지 확인한다.</summary>
    [Test]
    public void BoxPush_WithoutInteractionCardBlocksNormalMovement()
    {
        // player는 방향 입력만 전달하고 밀기 효과는 실행하지 않는 플레이어다.
        Villager player = CreateBoxPlayer(new Vector2Int(0, 1));
        // box는 정면의 통행을 차단할 박스다.
        GridBox box = CreateGridBox(new Vector2Int(1, 1));
        player.Move(Vector2.right);
        TickBoxPush(50);
        Assert.AreEqual(new Vector2Int(0, 1), player.Movement.CurrentCell);
        Assert.AreEqual(new Vector2Int(1, 1), box.CurrentCell);
    }

    /// <summary>벽, 두 번째 박스, 캐릭터 또는 이동 중인 캐릭터가 다음 칸에 있으면 밀기를 거절한다.</summary>
    /// <param name="blocker">이번 테스트에서 목적지를 막을 장애물 종류다.</param>
    [TestCase("wall"), TestCase("box"), TestCase("creature"), TestCase("incoming")]
    public void BoxPush_RejectsBlockedDestination(string blocker)
    {
        // player는 막힌 박스를 밀려고 하는 플레이어다.
        Villager player = CreateBoxPlayer(new Vector2Int(0, 1));
        CreateGridBox(new Vector2Int(1, 1));
        if (blocker == "wall")
            _map.SetCellBlocked(new Vector2Int(2, 1), true);
        else if (blocker == "box")
            CreateGridBox(new Vector2Int(2, 1));
        else if (blocker == "creature")
            CreateMover(new Vector2Int(2, 1));
        else
        {
            // incoming은 아직 비어 보이는 박스 목적지를 이미 향하고 있는 이동 주체다.
            CreatureMovement incoming = CreateMover(new Vector2Int(2, 2));
            Assert.IsTrue(incoming.TryMoveToCell(new Vector2Int(2, 1)));
        }
        player.Move(Vector2.right);
        Assert.IsFalse(player.BoxPush.CanPush());
        Assert.IsFalse(player.BoxPush.TryPush());
        Assert.IsFalse(player.BoxPush.IsPushing);
    }

    /// <summary>밀기 중 원래 박스 칸은 참여자만 허용하고 목적지는 모두에게 차단하는지 확인한다.</summary>
    [Test]
    public void BoxPush_ProtectsBothBoxCellsAndRejectsDuplicateStart()
    {
        // player는 한 번만 밀기를 시작해야 하는 플레이어다.
        Villager player = CreateBoxPlayer(new Vector2Int(0, 1));
        CreateGridBox(new Vector2Int(1, 1));
        // other는 박스 옆에서 두 보호 칸으로 접근할 수 없는 다른 이동 주체다.
        CreatureMovement other = CreateMover(new Vector2Int(2, 2));
        player.Move(Vector2.right);
        Assert.IsTrue(player.BoxPush.TryPush());
        Assert.IsFalse(player.BoxPush.TryPush());
        Assert.IsTrue(_map.CanEnterCell(new Vector2Int(1, 1), player.Movement));
        Assert.IsFalse(_map.CanEnterCell(new Vector2Int(1, 1), other));
        Assert.IsFalse(_map.CanEnterCell(new Vector2Int(2, 1), other));
        Assert.IsFalse(other.TryMoveToCell(new Vector2Int(2, 1)));
        player.Move(Vector2.zero);
        TickBoxPush(50);
        Assert.AreEqual(new Vector2Int(1, 1), player.Movement.CurrentCell);
    }

    /// <summary>밀기 도중 들어온 새 방향은 현재 밀기를 마친 다음 일반 이동에서 사용하는지 확인한다.</summary>
    [Test]
    public void BoxPush_ResumesLatestDirectionAfterFinishing()
    {
        // player는 박스를 미는 도중 위쪽 입력으로 변경할 플레이어다.
        Villager player = CreateBoxPlayer(new Vector2Int(0, 1));
        // box는 첫 밀기 뒤 제자리에 남아야 하는 박스다.
        GridBox box = CreateGridBox(new Vector2Int(1, 1));
        player.Move(Vector2.right);
        Assert.IsTrue(player.BoxPush.TryPush());
        TickBoxPush(2);
        player.Move(Vector2.up);
        TickBoxPush(12);
        player.Move(Vector2.zero);
        TickBoxPush(50);
        Assert.AreEqual(new Vector2Int(2, 1), box.CurrentCell);
        Assert.AreEqual(1, player.Movement.CurrentCell.x);
        Assert.Greater(player.Movement.CurrentCell.y, 1);
        Assert.IsFalse(player.Movement.IsMoving);
    }

    /// <summary>중간 취소 후 두 물체를 원래 중앙에 복원하고 목적지 임시 차단을 해제하는지 확인한다.</summary>
    /// <param name="interruption">밀기를 도중에 중단시킬 상황의 종류다.</param>
    [TestCase("cancel"), TestCase("round-end"), TestCase("layout"), TestCase("box-disable"), TestCase("player-disable")]
    public void BoxPush_InterruptedStepReleasesTemporaryBlock(string interruption)
    {
        // player는 밀기 도중 중단될 플레이어다.
        Villager player = CreateBoxPlayer(new Vector2Int(0, 1));
        // box는 취소 후 출발 칸을 유지하거나 비활성화될 박스다.
        GridBox box = CreateGridBox(new Vector2Int(1, 1));
        player.Move(Vector2.right);
        Assert.IsTrue(player.BoxPush.TryPush());
        TickBoxPush(2);
        player.Move(Vector2.zero);
        if (interruption == "round-end")
            SetField(typeof(StageManager), Managers.Stage, "<Phase>k__BackingField", EStagePhase.RoundResult);
        else if (interruption == "layout")
            _map.SetCellBlocked(new Vector2Int(2, 1), true);
        else if (interruption == "box-disable")
            box.gameObject.SetActive(false);
        else if (interruption == "player-disable")
            player.gameObject.SetActive(false);
        else
            player.BoxPush.CancelPush();
        TickBoxPush(4);
        if (interruption == "layout")
            _map.SetCellBlocked(new Vector2Int(2, 1), false);
        Assert.IsFalse(player.BoxPush.IsPushing);
        Assert.IsFalse(box.IsMoving);
        Assert.AreEqual(new Vector2Int(0, 1), player.Movement.CurrentCell);
        Assert.Less(Vector2.Distance(box.Position, _map.CellCenterToWorld(new Vector2Int(1, 1))), 0.001f);
        Assert.IsTrue(_map.CanEnterCell(new Vector2Int(2, 1), null));
        if (interruption == "box-disable")
            Assert.IsTrue(_map.CanEnterCell(new Vector2Int(1, 1), null));
    }

    /// <summary>빙의가 없는 NPC나 결과 화면에서는 같은 입력이 있어도 밀기를 실행하지 않는지 확인한다.</summary>
    /// <param name="isResult">참이면 결과 화면, 거짓이면 빙의 해제 상황을 검증한다.</param>
    [TestCase(false), TestCase(true)]
    public void BoxPush_RequiresPossessionAndPlayingPhase(bool isResult)
    {
        // player는 빙의 상태 또는 게임 진행 상태가 유효하지 않은 플레이어다.
        Villager player = CreateBoxPlayer(new Vector2Int(0, 1));
        CreateGridBox(new Vector2Int(1, 1));
        player.Move(Vector2.right);
        if (isResult)
            SetField(typeof(StageManager), Managers.Stage, "<Phase>k__BackingField", EStagePhase.RoundResult);
        else
            SetField(typeof(ObjectManager), Managers.Object, "<PossessedTarget>k__BackingField", null);
        Assert.IsFalse(player.BoxPush.TryPush());
    }

    /// <summary>박스를 민 플레이어가 밟은 칸만 점령하고 박스의 목적지는 중립으로 남는지 확인한다.</summary>
    [Test]
    public void BoxPush_CapturesPlayerFootstepButNotBoxDestination()
    {
        // player는 점령이 열린 라운드에서 박스를 밀 플레이어다.
        Villager player = CreateBoxPlayer(new Vector2Int(0, 1));
        // box는 이동 자체로 타일 소유권을 얻으면 안 되는 박스다.
        GridBox box = CreateGridBox(new Vector2Int(1, 1));
        _map.BeginTerritoryRound();
        player.Move(Vector2.right);
        Assert.IsTrue(player.BoxPush.TryPush());
        player.Move(Vector2.zero);
        TickBoxPush(50);
        Assert.AreEqual(TileOwner.Player, _map.GetTileOwner(new Vector2Int(1, 1)));
        Assert.AreEqual(TileOwner.Neutral, _map.GetTileOwner(box.CurrentCell));
        Assert.AreEqual(2, _map.PlayerTerritoryCount);
    }

    /// <summary>라운드 복원 칸에 플레이어가 서 있으면 스폰 칸으로 이동한 뒤 박스를 정상 복원하는지 확인한다.</summary>
    [Test]
    public void BoxPush_RoundResetRestoresBoxAndRelocatesOverlappingPlayer()
    {
        // player는 첫 밀기를 마친 뒤 박스의 최초 칸에 서 있을 플레이어다.
        Villager player = CreateBoxPlayer(new Vector2Int(0, 1));
        // box는 라운드가 바뀌면 최초 칸으로 돌아가야 하는 박스다.
        GridBox box = CreateGridBox(new Vector2Int(1, 1));
        // root는 맵 프리팹과 같은 박스 검색 경로를 제공할 테스트 루트다.
        var root = new GameObject("BoxRoundTestMap");
        _objects.Add(root);
        SceneManager.MoveGameObjectToScene(root, _scene);
        box.transform.SetParent(root.transform, true);
        // view는 리소스 로딩 없이 맵 루트를 연결할 기존 표시 담당 객체다.
        MapView view = (MapView)typeof(MapManager).GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_map);
        SetField(typeof(MapView), view, "_root", root);
        view.InitializeBoxes(_map);
        player.Move(Vector2.right);
        Assert.IsTrue(player.BoxPush.TryPush());
        player.Move(Vector2.zero);
        TickBoxPush(50);
        // area는 새 라운드 전체 영역이며 error는 영역 생성에 실패했을 때의 원인이다.
        Assert.IsTrue(TerrainArea.TryBuild(new TerrainAreaData { Rows = new List<string> { "1111", "1111", "1111", "1111" } },
            4, 4, out TerrainArea area, out string error), error);
        _map.SetRound(ERound.A, area);
        Assert.IsFalse(box.HasCell);
        Assert.IsTrue(_map.IsBoxSpawnCell(player.Movement.CurrentCell));
        // playData는 플레이어 재배치에 필요한 기존 공통 스폰 정보만 가진 실행 데이터다.
        StagePlayData playData = (StagePlayData)Activator.CreateInstance(typeof(StagePlayData), true);
        SetField(typeof(StagePlayData), playData, "<PlayerSpawn>k__BackingField",
            new SpawnInfo(EObjectType.Villager, 10, new Vector2Int(0, 1)));
        SetField(typeof(StageManager), Managers.Stage, "_playData", playData);
        typeof(StageManager).GetMethod("MovePlayerIntoArea", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(Managers.Stage, new object[] { area });
        _map.RefreshBoxes();
        Assert.AreEqual(new Vector2Int(0, 1), player.Movement.CurrentCell);
        Assert.IsTrue(box.HasCell);
        Assert.AreEqual(new Vector2Int(1, 1), box.CurrentCell);
        Assert.IsFalse(_map.CanEnterCell(box.CurrentCell, null));
        Assert.IsTrue(_map.CanEnterCell(new Vector2Int(2, 1), null));
    }

    /// <summary>기존 물리 이동 주체를 최소 스탯과 빙의 상태를 가진 박스 밀기 플레이어로 구성한다.</summary>
    /// <param name="cell">플레이어가 처음 서 있을 칸이다.</param>
    private Villager CreateBoxPlayer(Vector2Int cell)
    {
        // movement는 기존 통합 테스트와 같은 충돌체와 이동 설정을 재사용한다.
        CreatureMovement movement = CreateMover(cell);
        // player는 프리팹 없이도 Init에서 밀기 컴포넌트를 준비하는 실제 플레이어 클래스다.
        Villager player = movement.gameObject.AddComponent<Villager>();
        player.Init();
        ConfigureStats(player);
        movement.SetInfo(player);
        player.BoxPush.SetInfo(player);
        SetField(typeof(ObjectManager), Managers.Object, "<PossessedTarget>k__BackingField", player);
        return player;
    }

    /// <summary>독립 물리 장면에 실제 키네마틱 바디와 충돌체를 가진 박스를 등록한다.</summary>
    /// <param name="cell">박스가 처음 차지할 바닥 칸이다.</param>
    private GridBox CreateGridBox(Vector2Int cell)
    {
        // instance는 테스트 종료 때 함께 정리할 박스 오브젝트다.
        var instance = new GameObject("BoxPushTestBox");
        _objects.Add(instance);
        SceneManager.MoveGameObjectToScene(instance, _scene);
        instance.transform.position = _map.CellCenterToWorld(cell);
        instance.AddComponent<Rigidbody2D>();
        instance.AddComponent<BoxCollider2D>().size = Vector2.one * 0.9f;
        // box는 프리팹과 같은 초기화 경로로 맵에 연결할 박스 컴포넌트다.
        GridBox box = instance.AddComponent<GridBox>();
        box.Initialize(_map);
        Assert.IsTrue(box.HasCell);
        return box;
    }

    /// <summary>실제 편집기의 노드 키를 사용한 Tick/Fire 카드를 메모리에만 조립한다.</summary>
    /// <param name="player">이번 카드의 소유자이자 밀기 실행 주체다.</param>
    private static InteractionComponent CreateBoxInteraction(Villager player)
    {
        Managers.Interaction.Init();
        // json은 사용자가 조립할 설정과 같은 조건 및 효과를 가진 테스트 전용 정의다.
        const string json = "{\"id\":\"box_test\",\"interactions\":[{\"id\":\"push\",\"trigger\":\"Tick\",\"mode\":\"Fire\",\"cooldown\":0.05,\"maxActivations\":0,\"conditions\":[{\"type\":\"StagePlaying\"},{\"type\":\"CanPushBox\"}],\"effects\":[{\"type\":\"PushBox\"}]}]}";
        // interaction은 자기 소유자의 Tick에서 Instigator가 null이어도 실행되어야 하는 카드다.
        InteractionComponent interaction = player.gameObject.AddComponent<InteractionComponent>();
        interaction.SetInfo(player, InteractionLoader.Parse(json, null));
        return interaction;
    }

    /// <summary>이동기, 밀기 실행기, 2D 물리를 실제 실행 순서로 진행하고 선택적으로 Tick 카드를 평가한다.</summary>
    /// <param name="count">명시적으로 실행할 물리 주기의 수다.</param>
    /// <param name="interaction">지정하면 매 주기 평가할 자동 밀기 카드다.</param>
    private void TickBoxPush(int count, InteractionComponent interaction = null)
    {
        // frame은 상호작용 재사용 대기 시간에도 사용할 이번 반복의 물리 주기 번호다.
        for (int frame = 0; frame < count; frame++)
        {
            if (interaction != null)
                interaction.OnTrigger(ETriggerType.Tick, Managers.Object.PossessedTarget, frame * STEP);
            // mover는 이번 물리 주기에서 먼저 속도를 계산할 캐릭터다.
            foreach (CreatureMovement mover in _movers)
            {
                if (mover.isActiveAndEnabled)
                    typeof(CreatureMovement).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(mover, null);
            }
            // mover는 속도 계산 뒤 밀기 동기화가 필요한 캐릭터를 찾을 이동 주체다.
            foreach (CreatureMovement mover in _movers)
            {
                // push는 같은 오브젝트에 연결되어 있을 수 있는 박스 밀기 실행기다.
                BoxPushController push = mover.GetComponent<BoxPushController>();
                if (push != null && push.isActiveAndEnabled)
                    typeof(BoxPushController).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(push, null);
            }
            _physics.Simulate(STEP);
        }
    }
}

/// <summary>실제 저작 프리팹의 스프라이트, 충돌 설정 및 비워 둔 상호작용 Set을 검증한다.</summary>
public sealed class BoxPrefabAssetTests
{
    /// <summary>준비된 박스 프리팹이 한 칸 크기의 스프라이트와 키네마틱 충돌체를 갖는지 확인한다.</summary>
    [Test]
    public void GridBoxPrefab_HasSpriteBodyColliderAndRuntimeComponent()
    {
        // prefab은 사용자가 맵에 드래그할 실제 저작 에셋이다.
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/@Resources/Prefabs/Gimmicks/GridBox.prefab");
        Assert.IsNotNull(prefab);
        Assert.IsNotNull(prefab.GetComponent<GridBox>());
        Assert.AreEqual(RigidbodyType2D.Kinematic, prefab.GetComponent<Rigidbody2D>().bodyType);
        Assert.IsFalse(prefab.GetComponent<BoxCollider2D>().isTrigger);
        Assert.IsNotNull(prefab.GetComponent<SpriteRenderer>().sprite);
        Assert.AreEqual(Vector2.one, (Vector2)prefab.GetComponent<SpriteRenderer>().sprite.bounds.size);
        Assert.IsNull(prefab.GetComponent<CreatureBase>());
        Assert.IsNull(prefab.GetComponent<InteractionComponent>());
    }

    /// <summary>사용자가 직접 조립할 player_sokoban Set에 실행 카드가 미리 들어 있지 않은지 확인한다.</summary>
    [Test]
    public void PlayerSokobanSet_IsEmptyAndReadyForUserAssembly()
    {
        // set은 프로젝트에 저장된 빈 준비용 Set의 실제 파싱 결과다.
        InteractionSetDefinition set = InteractionLoader.Parse(
            File.ReadAllText("Assets/@Resources/Data/JsonData/Interactions/player_sokoban.json"), null);
        Assert.IsNotNull(set);
        Assert.AreEqual("player_sokoban", set.Id);
        Assert.IsEmpty(set.Interactions);
    }
}
