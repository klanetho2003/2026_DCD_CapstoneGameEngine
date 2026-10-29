using System;
using System.Collections.Generic;
using System.Reflection;
using Data;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using static Define;

/// <summary>탈취와 중복 방지, 라운드 초기화 및 정산 결과 보존을 검증한다.</summary>
public class TileTerritoryPolicyTests
{
    /// <summary>미시작 상태에서는 점령하지 않고 새 라운드의 칸은 중립인지 확인한다.</summary>
    [Test]
    public void Territory_StartsNeutralAndRequiresOpenRound()
    {
        // territory는 화면 표시와 물리 환경 없이 검증할 소유권 기록이다.
        var territory = new TileTerritory();
        Assert.IsFalse(territory.TryCapture(Vector2Int.zero, TileOwner.Player));
        territory.BeginRound();
        Assert.AreEqual(TileOwner.Neutral, territory.GetOwner(Vector2Int.zero));
        Assert.IsFalse(territory.TryCapture(Vector2Int.zero, TileOwner.Neutral));
        Assert.IsTrue(territory.TryCapture(Vector2Int.zero, TileOwner.Player));
        Assert.AreEqual(1, territory.PlayerTiles);
    }

    /// <summary>같은 타일을 반복해서 밟아도 중복 가산하지 않는지 확인한다.</summary>
    [Test]
    public void Territory_RepeatedOwnTileDoesNotAddScore()
    {
        // territory는 반복 점령의 집계 결과를 검증할 기록이다.
        var territory = new TileTerritory();
        territory.BeginRound();
        Assert.IsTrue(territory.TryCapture(Vector2Int.zero, TileOwner.Player));
        Assert.IsFalse(territory.TryCapture(Vector2Int.zero, TileOwner.Player));
        Assert.AreEqual(1, territory.PlayerTiles);
    }

    /// <summary>양방향 탈취에서 이전 진영의 수를 빼고 새로운 진영에 더하는지 확인한다.</summary>
    [Test]
    public void Territory_StealingTransfersCountsInBothDirections()
    {
        // territory는 동일 타일을 번갈아 탈취할 소유권 기록이다.
        var territory = new TileTerritory();
        territory.BeginRound();
        territory.TryCapture(Vector2Int.zero, TileOwner.Monster);
        territory.TryCapture(Vector2Int.zero, TileOwner.Player);
        Assert.AreEqual(1, territory.PlayerTiles);
        Assert.AreEqual(0, territory.MonsterTiles);
        territory.TryCapture(Vector2Int.zero, TileOwner.Monster);
        Assert.AreEqual(0, territory.PlayerTiles);
        Assert.AreEqual(1, territory.MonsterTiles);
    }

    /// <summary>정산은 소유권을 보존하면서 추가 점령만 잠그는지 확인한다.</summary>
    [Test]
    public void Territory_FinishLocksChangesAndPreservesOwnership()
    {
        // territory는 정산 뒤 변화를 차단할 점령 기록이다.
        var territory = new TileTerritory();
        territory.BeginRound();
        territory.TryCapture(Vector2Int.zero, TileOwner.Player);
        // score는 이후 라운드 변경과 분리되는 이번 라운드의 확정 값이다.
        TerritoryScore score = territory.FinishRound();
        Assert.IsFalse(territory.TryCapture(Vector2Int.zero, TileOwner.Monster));
        Assert.IsFalse(territory.TryCapture(Vector2Int.up, TileOwner.Player));
        Assert.AreEqual(TileOwner.Player, territory.GetOwner(Vector2Int.zero));
        territory.BeginRound();
        Assert.AreEqual(TileOwner.Neutral, territory.GetOwner(Vector2Int.zero));
        Assert.AreEqual(0, territory.PlayerTiles);
        Assert.AreEqual(1, score.PlayerTiles);
    }
}

/// <summary>기존 독립 물리 장면을 재사용하여 이동 진입과 점령 및 정산의 연결을 검증한다.</summary>
public partial class MonsterMovementIntegrationTests
{
    /// <summary>라운드 시작과 도중 스폰 모두 현재 서 있는 칸을 점령하는지 확인한다.</summary>
    [Test]
    public void Territory_RoundStartAndNewSpawnCaptureStandingCells()
    {
        // player는 라운드 시작 전에 배치된 플레이어 이동 주체다.
        CreatureMovement player = CreateTerritoryPlayer(new Vector2Int(0, 1));
        Assert.AreEqual(TileOwner.Neutral, _map.GetTileOwner(player.CurrentCell));
        _map.BeginTerritoryRound();
        Assert.AreEqual(TileOwner.Player, _map.GetTileOwner(player.CurrentCell));
        Assert.AreEqual(12f, player.EffectiveMoveSpeed);
        // monster는 라운드가 열린 뒤 추가 스폰될 몬스터다.
        MonsterBase monster = CreateMonster(new Vector2Int(3, 1));
        Assert.AreEqual(TileOwner.Monster, _map.GetTileOwner(monster.Movement.CurrentCell));
        Assert.AreEqual(1, _map.PlayerTerritoryCount);
        Assert.AreEqual(1, _map.MonsterTerritoryCount);
    }

    /// <summary>네 방향 모두 실제 충돌체 진입 시점에 중앙 도착보다 먼저 점령하는지 확인한다.</summary>
    /// <param name="x">진입할 목적지 방향의 가로 성분이다.</param>
    /// <param name="y">진입할 목적지 방향의 세로 성분이다.</param>
    [TestCase(1, 0), TestCase(-1, 0), TestCase(0, 1), TestCase(0, -1)]
    public void Territory_EntersBeforeCenterInAllDirections(int x, int y)
    {
        // player는 기본 속도 4로 진입 시점을 관찰할 플레이어다.
        CreatureMovement player = CreateTerritoryPlayer(new Vector2Int(1, 1));
        // target은 이번 테스트 방향으로 이동할 인접 타일이다.
        Vector2Int target = player.CurrentCell + new Vector2Int(x, y);
        _map.BeginTerritoryRound();
        Assert.IsTrue(player.TryMoveToCell(target));
        Assert.AreEqual(TileOwner.Neutral, _map.GetTileOwner(target));
        Tick(1);
        Assert.AreEqual(TileOwner.Neutral, _map.GetTileOwner(target));
        Tick(3);
        Assert.AreEqual(TileOwner.Player, _map.GetTileOwner(target));
        Assert.IsTrue(player.IsMoving);
        Assert.AreNotEqual(target, player.CurrentCell);
        Assert.IsTrue(_map.CanEnterCell(target, null), "점령은 중앙 도착 후 선점과 분리되어야 한다.");
        Tick(30);
        Assert.AreEqual(target, player.CurrentCell);
        Assert.AreEqual(2, _map.PlayerTerritoryCount);
    }

    /// <summary>출발 칸의 소유권이 남고 비워진 상대 칸을 밟으면 탈취되는지 확인한다.</summary>
    [Test]
    public void Territory_LeavingPersistsAndOpponentCanStealEmptyTile()
    {
        // player는 먼저 가운데 타일을 점령한 뒤 위로 이동할 플레이어다.
        CreatureMovement player = CreateTerritoryPlayer(new Vector2Int(0, 1));
        // monster는 플레이어가 비운 타일을 오른쪽에서 탈취할 몬스터다.
        MonsterBase monster = CreateMonster(new Vector2Int(3, 1));
        _map.BeginTerritoryRound();
        player.TryMoveToCell(new Vector2Int(1, 1));
        Tick(30);
        Assert.IsFalse(monster.Movement.TryMoveToCell(player.CurrentCell));
        player.TryMoveToCell(new Vector2Int(1, 2));
        Tick(30);
        Assert.AreEqual(TileOwner.Player, _map.GetTileOwner(new Vector2Int(1, 1)));
        monster.Movement.TryMoveToCell(new Vector2Int(2, 1));
        Tick(30);
        monster.Movement.TryMoveToCell(new Vector2Int(1, 1));
        Tick(30);
        Assert.AreEqual(TileOwner.Monster, _map.GetTileOwner(new Vector2Int(1, 1)));
        Assert.AreEqual(2, _map.PlayerTerritoryCount);
        Assert.AreEqual(3, _map.MonsterTerritoryCount);
    }

    /// <summary>진입 전에 목적지가 막혀 복귀하면 소유권과 점령 수가 바뀌지 않는지 확인한다.</summary>
    [Test]
    public void Territory_CancelBeforeEntryDoesNotCapture()
    {
        // player는 아직 목적지에 실제로 진입하지 않은 플레이어다.
        CreatureMovement player = CreateTerritoryPlayer(new Vector2Int(1, 1));
        _map.BeginTerritoryRound();
        player.TryMoveToCell(new Vector2Int(2, 1));
        _map.SetCellBlocked(new Vector2Int(2, 1), true);
        Tick(30);
        Assert.AreEqual(TileOwner.Neutral, _map.GetTileOwner(new Vector2Int(2, 1)));
        Assert.AreEqual(1, _map.PlayerTerritoryCount);
    }

    /// <summary>진입한 뒤 복귀하더라도 실제로 밟았던 타일의 소유권은 유지하는지 확인한다.</summary>
    [Test]
    public void Territory_CancelAfterEntryKeepsCapturedTile()
    {
        // player는 충돌체 진입 후 중앙 도착 전에 복귀할 플레이어다.
        CreatureMovement player = CreateTerritoryPlayer(new Vector2Int(1, 1));
        _map.BeginTerritoryRound();
        player.TryMoveToCell(new Vector2Int(2, 1));
        Tick(4);
        Assert.AreEqual(TileOwner.Player, _map.GetTileOwner(new Vector2Int(2, 1)));
        _map.SetCellBlocked(new Vector2Int(2, 1), true);
        Tick(30);
        Assert.AreEqual(new Vector2Int(1, 1), player.CurrentCell);
        Assert.AreEqual(TileOwner.Player, _map.GetTileOwner(new Vector2Int(2, 1)));
        Assert.AreEqual(2, _map.PlayerTerritoryCount);
    }

    /// <summary>같은 목적지를 향할 때 양보하는 몬스터가 먼저 점령하지 않는지 확인한다.</summary>
    [Test]
    public void Territory_SameDestinationUsesExistingPlayerPriority()
    {
        // monster는 같은 빈 칸으로 먼저 이동을 요청할 몬스터다.
        MonsterBase monster = CreateMonster(new Vector2Int(0, 1));
        // player는 기존 동시 진입 규칙에 따라 우선권을 얻을 플레이어다.
        CreatureMovement player = CreateTerritoryPlayer(new Vector2Int(2, 1));
        _map.BeginTerritoryRound();
        monster.Movement.TryMoveToCell(new Vector2Int(1, 1));
        player.TryMoveToCell(new Vector2Int(1, 1));
        Tick(4);
        Assert.AreEqual(TileOwner.Player, _map.GetTileOwner(new Vector2Int(1, 1)));
        Assert.AreEqual(1, _map.MonsterTerritoryCount);
        Tick(40);
        Assert.AreEqual(new Vector2Int(1, 1), player.CurrentCell);
        Assert.AreEqual(new Vector2Int(0, 1), monster.Movement.CurrentCell);
    }

    /// <summary>원격 좌표나 벽과 상대 선점 칸을 임의로 점령하지 못하는지 확인한다.</summary>
    [Test]
    public void Territory_RejectsRemoteBlockedAndOccupiedCells()
    {
        // player는 자신의 실제 위치와 인접 이동 목적지만 점령할 수 있는 플레이어다.
        CreatureMovement player = CreateTerritoryPlayer(new Vector2Int(1, 1));
        CreateMonster(new Vector2Int(2, 1));
        _map.BeginTerritoryRound();
        Assert.IsFalse(_map.TryCaptureCell(new Vector2Int(3, 3), player));
        Assert.IsFalse(_map.TryCaptureCell(new Vector2Int(2, 1), player));
        _map.SetCellBlocked(new Vector2Int(1, 2), true);
        Assert.IsFalse(player.TryMoveToCell(new Vector2Int(1, 2)));
        Assert.IsFalse(_map.TryCaptureCell(new Vector2Int(-1, 1), player));
        Assert.AreEqual(1, _map.PlayerTerritoryCount);
    }

    /// <summary>정산 후에는 남은 입력과 이동으로 소유권이 바뀌지 않는지 확인한다.</summary>
    [Test]
    public void Territory_ResultLocksMovementAndOwnership()
    {
        // player는 목적지에 진입하기 전에 제한시간이 끝날 플레이어다.
        CreatureMovement player = CreateTerritoryPlayer(new Vector2Int(1, 1));
        _map.BeginTerritoryRound();
        player.Move(Vector2.right);
        // score는 결과 화면에서 변하면 안 되는 소유 타일 수다.
        TerritoryScore score = _map.FinishTerritoryRound();
        Tick(40);
        player.Move(Vector2.up);
        Assert.IsFalse(player.IsMoving);
        Assert.AreEqual(1, score.PlayerTiles);
        Assert.AreEqual(1, _map.PlayerTerritoryCount);
        Assert.AreEqual(TileOwner.Neutral, _map.GetTileOwner(new Vector2Int(2, 1)));
    }

    /// <summary>마지막 물리 진행 뒤 다음 FixedUpdate 전에 정산해도 실제 진입한 칸은 집계하는지 확인한다.</summary>
    [Test]
    public void Territory_SettlementReadsLastPhysicsPositionBeforeLocking()
    {
        // player는 마지막 물리 진행으로 목적지 경계 안에 진입할 플레이어다.
        CreatureMovement player = CreateTerritoryPlayer(new Vector2Int(1, 1));
        _map.BeginTerritoryRound();
        player.TryMoveToCell(new Vector2Int(2, 1));
        Tick(2);
        Assert.AreEqual(TileOwner.Neutral, _map.GetTileOwner(new Vector2Int(2, 1)));
        // score는 다음 물리 갱신을 기다리지 않고 현재 실제 위치에서 확정한 점수다.
        TerritoryScore score = _map.FinishTerritoryRound();
        Assert.AreEqual(2, score.PlayerTiles);
        Assert.AreEqual(TileOwner.Player, _map.GetTileOwner(new Vector2Int(2, 1)));
    }

    /// <summary>시간 종료로 몬스터가 먼저 디스폰되어도 직전 물리 진입이 정산에서 빠지지 않는지 확인한다.</summary>
    [Test]
    public void Territory_RoundDespawnPreservesLastMonsterEntry()
    {
        // monster는 정산 콜백보다 먼저 정리되는 라운드 몬스터다.
        MonsterBase monster = CreateMonster(new Vector2Int(1, 1));
        _map.BeginTerritoryRound();
        monster.Movement.TryMoveToCell(new Vector2Int(2, 1));
        Tick(2);
        Assert.AreEqual(TileOwner.Neutral, _map.GetTileOwner(new Vector2Int(2, 1)));
        monster.gameObject.SetActive(false);
        Assert.AreEqual(2, _map.FinishTerritoryRound().MonsterTiles);
    }

    /// <summary>점령 상태가 계속 바뀌어도 몬스터 순환과 탈출 예고를 무효화하지 않는지 확인한다.</summary>
    [Test]
    public void Territory_DoesNotResetMonsterPatrolHistory()
    {
        // monster는 열린 점령 라운드에서 같은 경로를 순환할 몬스터다.
        MonsterBase monster = CreateMonster(new Vector2Int(0, 3));
        _map.BeginTerritoryRound();
        // version은 소유권 변경으로 증가하면 안 되는 정적 이동 환경 버전이다.
        int version = _map.TraversalVersion;
        // controller는 점령 처리와 함께 기존 순환 규칙을 수행할 행동 제어기다.
        var controller = new MonsterMovementController(monster);
        RunUntil(controller, MonsterMovementState.PreparingEscape, 1000);
        Assert.AreEqual(3, controller.LoopRepetitions);
        Assert.AreEqual(version, _map.TraversalVersion);
        Assert.Greater(_map.MonsterTerritoryCount, 1);
    }

    /// <summary>동일 종료는 한 번만 누적하고 새 라운드는 소유권만 초기화하는지 확인한다.</summary>
    [Test]
    public void Territory_StageSettlesOnceAndAccumulatesSeparateRounds()
    {
        // player는 두 라운드에 걸쳐 점수를 남길 플레이어다.
        CreatureMovement player = CreateTerritoryPlayer(new Vector2Int(1, 1));
        _map.BeginTerritoryRound();
        player.TryMoveToCell(new Vector2Int(2, 1));
        Tick(30);
        InvokeStage("OnRoundEnded");
        InvokeStage("OnRoundEnded");
        Assert.AreEqual(1, Managers.Stage.CompletedRoundCount);
        Assert.AreEqual(2, Managers.Stage.TotalPlayerTiles);
        _map.BeginTerritoryRound();
        Assert.AreEqual(TileOwner.Neutral, _map.GetTileOwner(new Vector2Int(1, 1)));
        Assert.AreEqual(1, _map.PlayerTerritoryCount);
        SetField(typeof(StageManager), Managers.Stage, "_roundSettled", false);
        SetField(typeof(StageManager), Managers.Stage, "<Phase>k__BackingField", EStagePhase.Playing);
        InvokeStage("OnRoundEnded");
        Assert.AreEqual(2, Managers.Stage.CompletedRoundCount);
        Assert.AreEqual(1, Managers.Stage.LastRoundScore.PlayerTiles);
        Assert.AreEqual(3, Managers.Stage.TotalPlayerTiles);
        InvokeStage("ResetSession");
        Assert.AreEqual(0, Managers.Stage.TotalPlayerTiles);
        Assert.AreEqual(0, Managers.Stage.CompletedRoundCount);
    }

    /// <summary>실패한 라운드의 진행 중 점령을 누적 점수에 더하지 않는지 확인한다.</summary>
    [Test]
    public void Territory_FailureLocksWithoutSettlingUnfinishedRound()
    {
        CreateTerritoryPlayer(new Vector2Int(1, 1));
        _map.BeginTerritoryRound();
        InvokeStage("FailStage");
        Assert.AreEqual(EStageResult.Failed, Managers.Stage.Result);
        Assert.AreEqual(0, Managers.Stage.TotalPlayerTiles);
        Assert.AreEqual(0, Managers.Stage.CompletedRoundCount);
        Assert.IsFalse(_map.IsTerritoryOpen);
    }

    /// <summary>맵 이탈 시 점령과 선점이 함께 정리되는지 확인한다.</summary>
    [Test]
    public void Territory_UnloadClearsOwnershipAndRoundLock()
    {
        CreateTerritoryPlayer(new Vector2Int(1, 1));
        _map.BeginTerritoryRound();
        _map.UnloadMap();
        Assert.AreEqual(TileOwner.Neutral, _map.GetTileOwner(new Vector2Int(1, 1)));
        Assert.AreEqual(0, _map.PlayerTerritoryCount);
        Assert.IsFalse(_map.HasTerritoryRound);
    }

    /// <summary>기존 이동 테스트 바디에 실제 플레이어의 진영과 최소 생존 스탯을 연결한다.</summary>
    /// <param name="cell">처음 배치할 타일 좌표다.</param>
    private CreatureMovement CreateTerritoryPlayer(Vector2Int cell)
    {
        // movement는 실제 물리를 이용하는 공통 이동 컴포넌트다.
        CreatureMovement movement = CreateMover(cell);
        // player는 플레이어 점령 진영과 이동 속도를 제공할 실제 캐릭터다.
        Villager player = movement.gameObject.AddComponent<Villager>();
        ConfigureStats(player);
        movement.SetInfo(player);
        return movement;
    }

    /// <summary>결과 화면이나 사망과 같은 기존 스테이지 전이를 리소스 로딩 없이 검증한다.</summary>
    /// <param name="methodName">테스트에서 실행할 스테이지 내부 전이의 이름이다.</param>
    private static void InvokeStage(string methodName)
    {
        typeof(StageManager).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(Managers.Stage, null);
    }
}

/// <summary>스테이지별 스프라이트 교체와 격자 정렬, 표시 순서 및 초기화를 검증한다.</summary>
public class TileTerritoryViewTests
{
    /// <summary>각 테스트에서 만든 임시 오브젝트와 스프라이트를 정리할 목록이다.</summary>
    private readonly List<UnityEngine.Object> _objects = new();

    /// <summary>점령 표시 테스트에서 만든 모든 임시 리소스를 정리한다.</summary>
    [TearDown]
    public void TearDown()
    {
        // instance는 테스트에서 생성한 맵 루트나 임시 스프라이트다.
        foreach (UnityEngine.Object instance in _objects)
            UnityEngine.Object.DestroyImmediate(instance);
        _objects.Clear();
    }

    /// <summary>음수 원점과 다른 칸 크기에서도 실제 타일 중심과 표시 중심이 일치하는지 확인한다.</summary>
    [Test]
    public void View_UsesMapOriginAndCellSizeWithoutAddingColliders()
    {
        // view는 스프라이트와 실제 맵 격자를 연결할 표시 컴포넌트다.
        TileTerritoryView view = CreateView();
        // grid는 기존 맵처럼 원점이 음수이고 한 칸 크기가 다른 좌표 규약이다.
        var grid = new GridSpec { OriginX = -19f, OriginY = -4f, CellSize = 2f };
        view.Initialize(in grid);
        view.SetOwner(new Vector2Int(2, 1), TileOwner.Player);
        Assert.AreEqual(new Vector3(-14f, -1f, 0f), view.DisplayTilemap.GetCellCenterWorld(new Vector3Int(2, 1, 0)));
        Assert.AreEqual(Tile.ColliderType.None, view.DisplayTilemap.GetColliderType(new Vector3Int(2, 1, 0)));
        Assert.IsEmpty(view.GetComponentsInChildren<Collider2D>());
        Assert.AreEqual(-1, view.DisplayTilemap.GetComponent<TilemapRenderer>().sortingOrder);
    }

    /// <summary>스테이지의 두 스프라이트를 교체하면 기존 표시도 바뀌고 초기화는 바닥을 드러내는지 확인한다.</summary>
    [Test]
    public void View_SpriteReplacementRefreshesExistingOwnershipAndClearRemovesTiles()
    {
        // view는 기존 소유권 표시를 유지한 채 외형을 교체할 표시 컴포넌트다.
        TileTerritoryView view = CreateView();
        // grid는 한 칸 크기가 1인 기본 표시 격자다.
        var grid = new GridSpec { CellSize = 1f };
        view.Initialize(in grid);
        view.SetOwner(Vector2Int.zero, TileOwner.Monster);
        // replacement는 다른 스테이지에서 몬스터 색상으로 사용할 스프라이트다.
        Sprite replacement = CreateSprite();
        view.SetSprites(CreateSprite(), replacement);
        Assert.AreSame(replacement, view.DisplayTilemap.GetSprite(Vector3Int.zero));
        view.Clear();
        Assert.IsFalse(view.DisplayTilemap.HasTile(Vector3Int.zero));
    }

    /// <summary>바닥의 상대 순서를 보존하며 캐릭터 순서보다 뒤에서 점령을 그리는지 확인한다.</summary>
    [Test]
    public void View_ReordersOnlyGroundAndPreservesWalls()
    {
        // view는 기존 바닥과 벽 표시 사이에 점령 표시를 추가할 컴포넌트다.
        TileTerritoryView view = CreateView();
        // visual은 기존 시각적 바닥의 표시 컴포넌트다.
        TilemapRenderer visual = CreateGround(view.transform, "Tilemap_Visual", 0);
        // terrain은 기존 라운드 영역 바닥이며 시각적 바닥보다 한 단계 뒤에 있다.
        TilemapRenderer terrain = CreateGround(view.transform, MAP_TILEMAP_TERRAIN, -1);
        // wall은 점령 표시로 정렬을 변경하면 안 되는 충돌 타일맵이다.
        TilemapRenderer wall = CreateGround(view.transform, MAP_TILEMAP_COLLISION, 0);
        // grid는 바닥과 점령 표시가 공유할 기본 한 칸 크기다.
        var grid = new GridSpec { CellSize = 1f };
        view.Initialize(in grid);
        Assert.AreEqual(-2, visual.sortingOrder);
        Assert.AreEqual(-3, terrain.sortingOrder);
        Assert.AreEqual(0, wall.sortingOrder);
        Assert.AreSame(visual.sharedMaterial, view.DisplayTilemap.GetComponent<TilemapRenderer>().sharedMaterial);
    }

    /// <summary>스테이지별로 별도 스프라이트를 지정할 수 있는 임시 맵 루트를 만든다.</summary>
    private TileTerritoryView CreateView()
    {
        // root는 표시 컴포넌트와 런타임 격자를 보관할 임시 맵 루트다.
        var root = new GameObject("TerritoryViewTest");
        _objects.Add(root);
        // view는 프리팹의 Inspector 설정을 대신하여 임시 스프라이트를 받을 컴포넌트다.
        TileTerritoryView view = root.AddComponent<TileTerritoryView>();
        view.SetSprites(CreateSprite(), CreateSprite());
        return view;
    }

    /// <summary>외부 에셋에 의존하지 않고 교체와 참조를 검증할 작은 스프라이트를 만든다.</summary>
    private Sprite CreateSprite()
    {
        // texture는 이번 테스트 스프라이트가 사용할 작은 원본 이미지다.
        var texture = new Texture2D(2, 2);
        _objects.Add(texture);
        // sprite는 표시 교체 전후를 비교할 독립 스프라이트다.
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f, 2f);
        _objects.Add(sprite);
        return sprite;
    }

    /// <summary>기존 맵처럼 이름과 표시 순서가 있는 타일맵을 임시 생성한다.</summary>
    /// <param name="parent">바닥 타일맵을 보관할 임시 맵 루트다.</param>
    /// <param name="name">기존 맵 규약에 맞는 타일맵 이름이다.</param>
    /// <param name="order">점령 표시를 추가하기 전의 표시 순서다.</param>
    private static TilemapRenderer CreateGround(Transform parent, string name, int order)
    {
        // instance는 바닥 여부와 표시 순서를 검사할 임시 타일맵 오브젝트다.
        var instance = new GameObject(name, typeof(Tilemap), typeof(TilemapRenderer));
        instance.transform.SetParent(parent, false);
        // renderer는 테스트에서 초기 순서를 설정하고 결과를 조회할 표시 컴포넌트다.
        TilemapRenderer renderer = instance.GetComponent<TilemapRenderer>();
        renderer.sortingOrder = order;
        return renderer;
    }
}
