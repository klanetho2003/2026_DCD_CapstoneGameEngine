using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Data;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using static Define;

/// <summary>순환 기록의 세 바퀴 판정과 기록 칸을 통과하는 BFS 동작을 검증한다.</summary>
public class MonsterMovementPolicyTests
{
    /// <summary>우회전 규칙으로 한 바퀴를 구성하는 네 출발 칸이다.</summary>
    private static readonly Vector2Int[] Cells =
        { new(1, 1), new(2, 1), new(2, 0), new(1, 0) };

    /// <summary>순환의 네 출발 칸에서 다음 칸으로 향하는 방향이다.</summary>
    private static readonly Vector2Int[] Directions =
        { Vector2Int.right, Vector2Int.down, Vector2Int.left, Vector2Int.up };

    /// <summary>같은 순환의 도착 기록을 세 번 완성해야 반복 횟수가 3이 되는지 확인한다.</summary>
    [Test]
    public void History_CountsCompletedLapsAndClearsEntries()
    {
        // 성공한 도착과 순환 판정을 반복해서 검증할 기록이다.
        var history = new MonsterPatrolHistory();
        // lap은 실제로 완주한 순환 횟수다.
        for (int lap = 1; lap <= 3; lap++)
        {
            // step은 현재 순환에서 성공한 도착을 기록할 출발 칸의 순서다.
            for (int step = 0; step < Cells.Length; step++)
                history.RecordArrival(Cells[step], Directions[step]);
            Assert.IsTrue(history.DetectCycle(Cells[0], Directions[0]));
            Assert.AreEqual(lap, history.Repetitions);
            Assert.AreEqual(0, history.Count);
            Assert.AreEqual(4, history.ExcludedCells.Count);
            Assert.IsFalse(history.DetectCycle(Cells[0], Directions[0]));
        }
    }

    /// <summary>출발 칸이 같아도 방향이 다르면 순환으로 판정하지 않는지 확인한다.</summary>
    [Test]
    public void History_DifferentDirectionDoesNotTriggerCycle()
    {
        // 타일별 마지막 성공 출발 방향을 확인할 기록이다.
        var history = new MonsterPatrolHistory();
        history.RecordArrival(Cells[0], Vector2Int.right);
        Assert.IsFalse(history.DetectCycle(Cells[0], Vector2Int.up));
        Assert.AreEqual(1, history.Count);
        history.RecordArrival(Cells[0], Vector2Int.down);
        Assert.AreEqual(1, history.Count);
    }

    /// <summary>순환 판정은 목적지 기록의 존재가 아니라 현재 칸의 저장 방향만 비교하는지 확인한다.</summary>
    [Test]
    public void History_MatchingCurrentDirectionDoesNotRequireTargetEntry()
    {
        // 목적지 타일의 항목이 없어도 현재 칸의 방향을 조회할 이동 내역이다.
        var history = new MonsterPatrolHistory();
        history.RecordArrival(Vector2Int.zero, Vector2Int.right);
        Assert.IsTrue(history.DetectCycle(Vector2Int.zero, Vector2Int.right));
        Assert.AreEqual(1, history.Repetitions);
        Assert.AreEqual(0, history.Count);
    }

    /// <summary>초기화 후에도 같은 기록 집합을 재사용하고 반복 횟수만 초기화하는지 확인한다.</summary>
    [Test]
    public void History_ClearReusesCollections()
    {
        // 재사용 중인 기록과 목적지 제외 집합이다.
        var history = new MonsterPatrolHistory();
        // 초기화 후에도 같은 인스턴스를 사용하는지 확인할 목적지 제외 집합이다.
        var excluded = history.ExcludedCells;
        history.MarkBlocked(Vector2Int.zero);
        history.RecordArrival(Vector2Int.zero, Vector2Int.right);
        history.Clear();
        Assert.AreSame(excluded, history.ExcludedCells);
        Assert.AreEqual(0, history.Count);
        Assert.AreEqual(0, history.Repetitions);
        Assert.IsEmpty(history.ExcludedCells);
    }

    /// <summary>이동 내역에 있는 칸을 경유하여 기록 밖의 타일을 목적지로 찾는지 확인한다.</summary>
    [Test]
    public void Bfs_CanCrossHistoryButCannotChooseItAsGoal()
    {
        // 한 줄 통로에서 이미 이동한 두 타일이다.
        var excluded = new HashSet<Vector2Int> { Vector2Int.zero, Vector2Int.right };
        // 탐색기가 반환할 실제 한 칸 이동 순서다.
        var path = new List<Vector2Int>();
        Assert.IsTrue(new MonsterEscapePathfinder().TryFindPath(Vector2Int.zero, excluded,
            cell => cell.y == 0 && cell.x >= 0 && cell.x <= 2, path));
        CollectionAssert.AreEqual(new[] { Vector2Int.right, new Vector2Int(2, 0) }, path);
    }

    /// <summary>같은 거리의 목적지가 있으면 위쪽을 우선하는지 확인한다.</summary>
    [Test]
    public void Bfs_EqualDistanceUsesUpFirst()
    {
        // 현재 칸만 기록에 있는 작은 열린 보드다.
        var excluded = new HashSet<Vector2Int> { new(1, 1) };
        // 가장 가까운 새 목적지를 반환받을 경로다.
        var path = new List<Vector2Int>();
        Assert.IsTrue(new MonsterEscapePathfinder().TryFindPath(new Vector2Int(1, 1), excluded,
            cell => cell.x >= 0 && cell.x < 3 && cell.y >= 0 && cell.y < 3, path));
        Assert.AreEqual(new Vector2Int(1, 2), path[0]);
    }

    /// <summary>진입 가능한 모든 칸이 기록에 있으면 경로를 반환하지 않는지 확인한다.</summary>
    [Test]
    public void Bfs_NoUnvisitedReachableCellReturnsNoPath()
    {
        // 유일한 두 통로 타일 모두가 이미 방문한 타일이다.
        var excluded = new HashSet<Vector2Int> { Vector2Int.zero, Vector2Int.right };
        // 이전 탐색 결과도 실패 시 지워지는지 확인할 경로다.
        var path = new List<Vector2Int> { new(99, 99) };
        Assert.IsFalse(new MonsterEscapePathfinder().TryFindPath(Vector2Int.zero, excluded,
            cell => cell.y == 0 && cell.x >= 0 && cell.x <= 1, path));
        Assert.IsEmpty(path);
    }
}

/// <summary>실제 Unity 2D 물리 장면에서 도착 선점, 이동 취소, 순환과 탈출 상태를 검증한다.</summary>
public partial class MonsterMovementIntegrationTests
{
    /// <summary>테스트가 사용하는 공통 물리 갱신 간격이다.</summary>
    private const float STEP = 0.02f;
    /// <summary>테스트가 끝나면 복원할 원래 전역 매니저다.</summary>
    private Managers _previousManager;
    /// <summary>테스트용 매니저를 보관하는 임시 오브젝트다.</summary>
    private GameObject _managerObject;
    /// <summary>기존 장면에 영향을 주지 않을 독립 물리 장면이다.</summary>
    private Scene _scene;
    /// <summary>테스트에서 명시적으로 진행하는 2D 물리 시뮬레이션이다.</summary>
    private PhysicsScene2D _physics;
    /// <summary>벽과 선점을 조회할 테스트 전용 맵이다.</summary>
    private MapManager _map;
    /// <summary>생성한 오브젝트를 종료 시 확실히 파괴하기 위한 목록이다.</summary>
    private readonly List<GameObject> _objects = new();
    /// <summary>물리 루프에서 갱신할 이동 컴포넌트 목록이다.</summary>
    private readonly List<CreatureMovement> _movers = new();

    /// <summary>격리된 매니저와 2D 물리 장면을 준비한다.</summary>
    [UnitySetUp]
    public IEnumerator SetUp()
    {
        // 독립 2D 물리 장면은 플레이 모드에서만 생성할 수 있다.
        yield return new EnterPlayMode();
        _previousManager = (Managers)typeof(Managers).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        _managerObject = new GameObject("MonsterMovementTestManagers");
        // 실제 게임 초기화를 호출하지 않고 테스트에 필요한 매니저 객체만 사용한다.
        Managers manager = _managerObject.AddComponent<Managers>();
        typeof(Managers).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, manager);
        _map = new MapManager();
        SetField(typeof(Managers), manager, "_map", _map);
        SetField(typeof(StageManager), Managers.Stage, "<Phase>k__BackingField", EStagePhase.Playing);
        _scene = SceneManager.CreateScene("MonsterMovementTest-" + Guid.NewGuid(),
            new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        _physics = _scene.GetPhysicsScene2D();
        SetMap(4, 4);
    }

    /// <summary>생성한 물리 오브젝트를 파괴하고 원래 매니저를 복원한다.</summary>
    [UnityTearDown]
    public IEnumerator TearDown()
    {
        // instance는 이번 테스트에서 생성하여 제거할 물리 오브젝트다.
        foreach (GameObject instance in _objects)
            UnityEngine.Object.DestroyImmediate(instance);
        _objects.Clear();
        _movers.Clear();
        UnityEngine.Object.DestroyImmediate(_managerObject);
        typeof(Managers).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _previousManager);
        if (_scene.IsValid())
            yield return SceneManager.UnloadSceneAsync(_scene);
        yield return new ExitPlayMode();
    }

    /// <summary>이동 시작 시에는 출발 칸만 선점하고 도착 후에 목적지로 갱신하는지 확인한다.</summary>
    [Test]
    public void Movement_ClaimsOnlyOnArrivalAndCallsBackAfterClaim()
    {
        // 타일 사이를 실제 물리 속도로 이동할 테스트 캐릭터다.
        CreatureMovement mover = CreateMover(new Vector2Int(1, 1));
        // 성공한 도착을 통지받은 횟수다.
        int completed = 0;
        Assert.IsTrue(mover.TryMoveToCell(new Vector2Int(2, 1), 1f, () =>
        {
            completed++;
            Assert.IsFalse(_map.CanEnterCell(new Vector2Int(2, 1), null));
        }));
        Assert.IsFalse(_map.CanEnterCell(new Vector2Int(1, 1), null));
        Assert.IsTrue(_map.CanEnterCell(new Vector2Int(2, 1), null));
        Tick(40);
        Assert.AreEqual(1, completed);
        Assert.AreEqual(new Vector2Int(2, 1), mover.CurrentCell);
        Assert.IsTrue(_map.CanEnterCell(new Vector2Int(1, 1), null));
    }

    /// <summary>입력 해제 후 이번 한 칸을 끝내고 중앙에서 정지하는 기존 동작을 보존한다.</summary>
    [Test]
    public void Movement_ReleasedInputCompletesOneCell()
    {
        // 오른쪽으로 한 칸 이동할 테스트 캐릭터다.
        CreatureMovement mover = CreateMover(new Vector2Int(1, 1));
        mover.Move(Vector2.right);
        Tick(3);
        mover.Move(Vector2.zero);
        Tick(50);
        Assert.AreEqual(new Vector2Int(2, 1), mover.CurrentCell);
        Assert.IsFalse(mover.IsMoving);
        Assert.Less(Vector2.Distance(mover.RB2D.position, new Vector2(2.5f, 1.5f)), 0.001f);
    }

    /// <summary>벽, 영역 밖, 다른 캐릭터가 선점한 타일로 이동을 시작하지 않는지 확인한다.</summary>
    [Test]
    public void Movement_RejectsBlockedAndOccupiedDestinations()
    {
        // 출발 칸의 선점을 유지할 첫 번째 캐릭터다.
        CreatureMovement mover = CreateMover(new Vector2Int(1, 1));
        CreateMover(new Vector2Int(2, 1));
        Assert.IsFalse(mover.TryMoveToCell(new Vector2Int(2, 1)));
        _map.SetCellBlocked(new Vector2Int(1, 2), true);
        Assert.IsFalse(mover.TryMoveToCell(new Vector2Int(1, 2)));
        Assert.IsFalse(_map.CanEnterCell(new Vector2Int(-1, 1), mover));
    }

    /// <summary>사망 통지와 다음 웨이브 스폰 사이에도 죽은 캐릭터의 선점이 새 스폰을 막지 않는지 확인한다.</summary>
    [Test]
    public void Map_DeadClaimDoesNotBlockNextWaveOrEraseNewOwner()
    {
        // 사망 처리 직전의 체력 상태를 재현할 몬스터다.
        MonsterBase monster = CreateMonster(new Vector2Int(1, 1));
        monster.GetResourceStat(EStatResourceType.CurrentHp).SetCurrent(0f);
        Assert.IsTrue(_map.CanEnterCell(new Vector2Int(1, 1), null));
        // 같은 칸에 새 웨이브가 먼저 등록되어도 이전 디스폰이 새 선점을 지우면 안 된다.
        CreatureMovement replacement = CreateMover(new Vector2Int(1, 1));
        monster.gameObject.SetActive(false);
        Assert.IsFalse(_map.CanEnterCell(new Vector2Int(1, 1), null));
        Assert.IsTrue(_map.CanEnterCell(new Vector2Int(1, 1), replacement));
    }

    /// <summary>두 이동 주체가 빈 목적지에 동시에 접근해도 도착 선점이 겹치지 않는지 확인한다.</summary>
    [Test]
    public void Movement_SameEmptyDestinationHasOnlyOneArrival()
    {
        // 같은 빈 칸을 서로 다른 출발 칸에서 향하는 두 이동 주체다.
        CreatureMovement first = CreateMover(new Vector2Int(0, 1));
        // 반대편에서 같은 목적지로 이동을 요청하는 두 번째 물리 이동 주체다.
        CreatureMovement second = CreateMover(new Vector2Int(2, 1));
        Assert.IsTrue(first.TryMoveToCell(new Vector2Int(1, 1)));
        Assert.IsTrue(second.TryMoveToCell(new Vector2Int(1, 1)));
        Tick(100);
        Assert.AreNotEqual(first.CurrentCell, second.CurrentCell);
        Assert.IsTrue(first.CurrentCell == new Vector2Int(1, 1) || second.CurrentCell == new Vector2Int(1, 1));
        Assert.IsFalse(first.IsMoving);
        Assert.IsFalse(second.IsMoving);
    }

    /// <summary>프리팹처럼 한 칸 크기의 실제 충돌체도 동시 목적지에서 선점과 이동을 끝내는지 확인한다.</summary>
    [Test]
    public void Movement_FullCellCollidersFinishDestinationConflict()
    {
        // 같은 빈 타일을 향해 이동할 두 개의 실제 물리 이동 주체다.
        CreatureMovement first = CreateMover(new Vector2Int(0, 1));
        // 반대편에서 같은 목적지로 이동을 요청하는 두 번째 물리 이동 주체다.
        CreatureMovement second = CreateMover(new Vector2Int(2, 1));
        first.GetComponent<BoxCollider2D>().size = Vector2.one;
        second.GetComponent<BoxCollider2D>().size = Vector2.one;
        Assert.IsTrue(first.TryMoveToCell(new Vector2Int(1, 1)));
        Assert.IsTrue(second.TryMoveToCell(new Vector2Int(1, 1)));
        Tick(150);
        Assert.AreNotEqual(first.CurrentCell, second.CurrentCell);
        Assert.IsTrue(first.CurrentCell == new Vector2Int(1, 1) || second.CurrentCell == new Vector2Int(1, 1));
        Assert.IsFalse(first.IsMoving);
        Assert.IsFalse(second.IsMoving);
    }

    /// <summary>먼저 움직인 몬스터도 같은 빈 칸을 향하는 플레이어에게 양보하는지 확인한다.</summary>
    [Test]
    public void Movement_PlayerWinsAgainstEarlierMonsterIntent()
    {
        // 플레이어보다 먼저 목적지로 출발할 몬스터다.
        MonsterBase monster = CreateMonster(new Vector2Int(0, 1));
        // 반대편 출발 칸에서 같은 목적지를 선택할 플레이어 이동 컴포넌트다.
        CreatureMovement playerMovement = CreateMover(new Vector2Int(2, 1));
        // 플레이어 우선권과 기본 배율을 제공할 실제 Villager 컴포넌트다.
        Villager player = playerMovement.gameObject.AddComponent<Villager>();
        ConfigureStats(player);
        playerMovement.SetInfo(player);
        Assert.AreEqual(12f, playerMovement.EffectiveMoveSpeed);
        Assert.IsTrue(monster.Movement.TryMoveToCell(new Vector2Int(1, 1)));
        Assert.IsTrue(playerMovement.TryMoveToCell(new Vector2Int(1, 1)));
        Tick(100);
        Assert.AreEqual(new Vector2Int(1, 1), playerMovement.CurrentCell);
        Assert.AreEqual(new Vector2Int(0, 1), monster.Movement.CurrentCell);
    }

    /// <summary>현재 라운드에서 제외된 칸은 공통 진입 판정에서도 막히는지 확인한다.</summary>
    [Test]
    public void Map_RoundAreaRejectsUnpaintedCells()
    {
        // 세 타일만 칠해진 작은 라운드 영역 데이터다.
        var data = new TerrainAreaData { Rows = new List<string> { "11", "10" } };
        // area는 실제 영역 조회 데이터이며 error는 영역 생성이 실패한 이유다.
        Assert.IsTrue(TerrainArea.TryBuild(data, 4, 4, out TerrainArea area, out string error), error);
        SetField(typeof(MapManager), _map, "_area", area);
        Assert.IsTrue(_map.CanEnterCell(new Vector2Int(1, 0), null));
        Assert.IsFalse(_map.CanEnterCell(new Vector2Int(1, 1), null));
        Assert.IsFalse(_map.CanEnterCell(new Vector2Int(2, 0), null));
    }

    /// <summary>비활성화와 외부 재배치가 이전 선점을 남기지 않는지 확인한다.</summary>
    [Test]
    public void Movement_RepositionAndDisableUpdateClaims()
    {
        // 외부 배치와 풀 반환에 사용할 이동 주체다.
        CreatureMovement mover = CreateMover(new Vector2Int(1, 1));
        Assert.IsTrue(mover.TryPlaceAt(new Vector2(2.5f, 2.5f)));
        Assert.IsTrue(_map.CanEnterCell(new Vector2Int(1, 1), null));
        Assert.IsFalse(_map.CanEnterCell(new Vector2Int(2, 2), null));
        mover.gameObject.SetActive(false);
        Assert.IsTrue(_map.CanEnterCell(new Vector2Int(2, 2), null));
    }

    /// <summary>오른쪽 출발 후 장애물을 만나면 아래쪽으로 우회전하는지 확인한다.</summary>
    [Test]
    public void Monster_StartsRightAndTurnsRightWhenBlocked()
    {
        // 오른쪽 벽을 만날 몬스터와 행동 제어기다.
        MonsterBase monster = CreateMonster(new Vector2Int(2, 2));
        // 현재 테스트 몬스터의 순환과 예고, 탈출 상태를 갱신할 행동 제어기다.
        var controller = new MonsterMovementController(monster);
        controller.Tick(STEP);
        Assert.AreEqual(Vector2Int.right, controller.Facing);
        Tick(20);
        controller.Tick(STEP);
        Assert.AreEqual(Vector2Int.down, controller.Facing);
    }

    /// <summary>같은 순환 세 바퀴 뒤에만 예고를 시작하고 4초 전에는 탈출하지 않는지 확인한다.</summary>
    [Test]
    public void Monster_ThreeLapsThenFourSecondPreparation()
    {
        // 내부의 새 타일로 탈출할 수 있는 4x4 보드의 몬스터다.
        MonsterBase monster = CreateMonster(new Vector2Int(0, 3));
        // 현재 테스트 몬스터의 순환과 예고, 탈출 상태를 갱신할 행동 제어기다.
        var controller = new MonsterMovementController(monster);
        RunUntil(controller, MonsterMovementState.PreparingEscape, 1000);
        Assert.AreEqual(3, controller.LoopRepetitions);
        Assert.AreEqual(4f, controller.PreparationRemaining, 0.001f);
        TickController(controller, 100);
        Assert.AreEqual(MonsterMovementState.PreparingEscape, controller.State);
        Assert.AreEqual(2f, controller.PreparationRemaining, 0.01f);
        RunUntil(controller, MonsterMovementState.Escaping, 150);
        RunUntil(controller, MonsterMovementState.Patrol, 60);
        Assert.AreEqual(0, controller.LoopRepetitions);
    }

    /// <summary>탈출할 새 타일이 없으면 워프 없이 머물고 차단 해제 후 다시 움직이는지 확인한다.</summary>
    [Test]
    public void Monster_NoEscapeRemainsTrappedUntilEnvironmentChanges()
    {
        SetMap(3, 2);
        _map.SetCellBlocked(new Vector2Int(2, 0), true);
        _map.SetCellBlocked(new Vector2Int(2, 1), true);
        // 네 타일 순환에 갇힐 몬스터와 행동 제어기다.
        MonsterBase monster = CreateMonster(new Vector2Int(0, 1));
        // 현재 테스트 몬스터의 순환과 예고, 탈출 상태를 갱신할 행동 제어기다.
        var controller = new MonsterMovementController(monster);
        RunUntil(controller, MonsterMovementState.Trapped, 1000);
        // 워프와 자동 재시작이 발생하지 않는지 비교할 봉쇄 위치다.
        Vector2 position = monster.Movement.RB2D.position;
        TickController(controller, 200);
        Assert.AreEqual(MonsterMovementState.Trapped, controller.State);
        Assert.AreEqual(position, monster.Movement.RB2D.position);
        _map.SetCellBlocked(new Vector2Int(2, 1), false);
        TickController(controller, 1);
        Assert.AreEqual(MonsterMovementState.Patrol, controller.State);
        Assert.IsTrue(monster.Movement.IsMoving);
    }

    /// <summary>박스 배치 변경에 해당하는 통행 버전 변경 시 예고를 취소하고 기록을 비우는지 확인한다.</summary>
    [Test]
    public void Monster_ObstacleChangeCancelsPreparation()
    {
        // 순환 예고 중 배치 변경을 받을 몬스터다.
        MonsterBase monster = CreateMonster(new Vector2Int(0, 3));
        // 현재 테스트 몬스터의 순환과 예고, 탈출 상태를 갱신할 행동 제어기다.
        var controller = new MonsterMovementController(monster);
        RunUntil(controller, MonsterMovementState.PreparingEscape, 1000);
        _map.SetCellBlocked(new Vector2Int(1, 1), true);
        TickController(controller, 1);
        Assert.AreEqual(MonsterMovementState.Patrol, controller.State);
        Assert.AreEqual(0, controller.LoopRepetitions);
        Assert.AreEqual(0f, controller.PreparationRemaining);
    }

    /// <summary>결과 화면에서 행동과 4초 타이머가 계속 진행되지 않는지 확인한다.</summary>
    [Test]
    public void Monster_ResultPhaseStopsMovementAndTimer()
    {
        // 순환 예고 도중 결과 화면으로 전환할 몬스터다.
        MonsterBase monster = CreateMonster(new Vector2Int(0, 3));
        // 현재 테스트 몬스터의 순환과 예고, 탈출 상태를 갱신할 행동 제어기다.
        var controller = new MonsterMovementController(monster);
        RunUntil(controller, MonsterMovementState.PreparingEscape, 1000);
        // 결과 화면 진입 시점에 남아 있던 예고 시간이다.
        float remaining = controller.PreparationRemaining;
        SetField(typeof(StageManager), Managers.Stage, "<Phase>k__BackingField", EStagePhase.RoundResult);
        TickController(controller, 100);
        Assert.AreEqual(remaining, controller.PreparationRemaining);
        Assert.IsFalse(monster.Movement.IsMoving);
    }

    /// <summary>테스트 맵의 격자와 벽 데이터를 직접 구성하여 리소스 로딩과 분리한다.</summary>
    /// <param name="width">테스트 보드의 가로 타일 수다.</param>
    /// <param name="height">테스트 보드의 세로 타일 수다.</param>
    private void SetMap(int width, int height)
    {
        // 화면 원점이 0이고 한 칸 크기가 1인 테스트 격자다.
        var grid = new GridSpec { CellSize = 1f, Width = width, Height = height };
        // 모든 칸을 바닥으로 시작하는 테스트 데이터다.
        var data = new MapData { Grid = grid, CollisionRows = new List<string>() };
        // row는 바닥 문자열을 추가할 세로 타일 번호다.
        for (int row = 0; row < height; row++)
            data.CollisionRows.Add(new string('0', width));
        SetField(typeof(MapManager), _map, "<CurrentMap>k__BackingField", data);
        SetField(typeof(MapManager), _map, "_grid", grid);
    }

    /// <summary>독립 물리 장면에 테스트용 이동 주체를 생성한다.</summary>
    /// <param name="cell">처음 선점할 테스트 타일이다.</param>
    private CreatureMovement CreateMover(Vector2Int cell)
    {
        // 실제 Rigidbody2D가 붙은 테스트 오브젝트다.
        GameObject instance = CreateBody(cell);
        // 데이터 로딩 없이 일반 입력을 검증할 이동 컴포넌트다.
        CreatureMovement movement = instance.AddComponent<CreatureMovement>();
        movement.Init();
        movement.SetInfo(new TestMovable());
        _movers.Add(movement);
        return movement;
    }

    /// <summary>몬스터 생존과 이동에 필요한 최소 스탯만 구성한 테스트 몬스터를 생성한다.</summary>
    /// <param name="cell">처음 선점할 테스트 타일이다.</param>
    private MonsterBase CreateMonster(Vector2Int cell)
    {
        // 독립 물리 장면에서 몬스터가 사용할 실제 오브젝트다.
        GameObject instance = CreateBody(cell);
        // 행동 제어기에 전달할 몬스터 컴포넌트다.
        MonsterBase monster = instance.AddComponent<MonsterBase>();
        monster.Init();
        ConfigureStats(monster);
        monster.Movement.Init();
        monster.Movement.SetInfo(monster);
        _movers.Add(monster.Movement);
        return monster;
    }

    /// <summary>실제 캐릭터의 생존과 이동을 조회할 최소 스탯을 게임 리소스 없이 구성한다.</summary>
    /// <param name="creature">기본 체력과 이동 속도를 부여할 테스트 캐릭터다.</param>
    private static void ConfigureStats(CreatureBase creature)
    {
        // 생존 여부와 이동 속도를 제공할 최소 기본 스탯이다.
        var stats = new CreatureStatDefine();
        stats.InitDefineStat(new List<StatData>
        {
            new() { sType = EStatType.MaxHp, sValue = 10f },
            new() { sType = EStatType.MoveSpeed, sValue = 4f }
        });
        // IsDead가 정상 조회할 수 있는 현재 체력 자원이다.
        var resources = new CreatureStatResource();
        resources.InitResourcesStat(new List<StatResourceData>
        {
            new() { sType = EStatType.MaxHp, rType = EStatResourceType.CurrentHp, rValue = -1f }
        }, stats);
        SetField(typeof(CreatureBase), creature, "_creatureStat", stats);
        SetField(typeof(CreatureBase), creature, "_creatureResource", resources);
    }

    /// <summary>현재 타일 중앙에 실제 충돌체와 중력 없는 동적 물리 바디를 생성한다.</summary>
    /// <param name="cell">오브젝트를 배치할 타일이다.</param>
    private GameObject CreateBody(Vector2Int cell)
    {
        // 생성과 정리를 추적할 테스트 캐릭터 오브젝트다.
        var instance = new GameObject("MovementTestActor");
        _objects.Add(instance);
        SceneManager.MoveGameObjectToScene(instance, _scene);
        // 실제 속도 이동을 시뮬레이션할 물리 컴포넌트다.
        Rigidbody2D body = instance.AddComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        body.constraints = RigidbodyConstraints2D.FreezeRotation;
        body.position = _map.CellCenterToWorld(cell);
        instance.AddComponent<BoxCollider2D>().size = Vector2.one * 0.8f;
        return instance;
    }

    /// <summary>모든 이동 컴포넌트를 갱신한 뒤 독립 물리 장면을 지정 횟수만큼 진행한다.</summary>
    /// <param name="count">진행할 물리 업데이트 횟수다.</param>
    private void Tick(int count)
    {
        // frame은 현재 테스트에서 진행한 물리 업데이트 횟수다.
        for (int frame = 0; frame < count; frame++)
        {
            // mover는 이번 물리 주기에서 속도를 갱신할 이동 주체다.
            foreach (CreatureMovement mover in _movers)
            {
                if (mover.isActiveAndEnabled)
                    typeof(CreatureMovement).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(mover, null);
            }
            _physics.Simulate(STEP);
        }
    }

    /// <summary>몬스터 판단과 실제 물리 진행을 같은 시간 간격으로 반복한다.</summary>
    /// <param name="controller">갱신할 몬스터 행동 제어기다.</param>
    /// <param name="count">진행할 갱신 횟수다.</param>
    private void TickController(MonsterMovementController controller, int count)
    {
        // frame은 판단과 물리를 함께 진행한 테스트 갱신 횟수다.
        for (int frame = 0; frame < count; frame++)
        {
            controller.Tick(STEP);
            Tick(1);
        }
    }

    /// <summary>제한 횟수 안에 지정한 행동 상태가 나타나는지 검증한다.</summary>
    /// <param name="controller">관찰할 행동 제어기다.</param>
    /// <param name="state">도달해야 할 행동 상태다.</param>
    /// <param name="limit">무한 반복을 방지하는 최대 물리 갱신 횟수다.</param>
    private void RunUntil(MonsterMovementController controller, MonsterMovementState state, int limit)
    {
        // frame은 목표 상태를 기다리며 소비한 갱신 횟수다.
        for (int frame = 0; frame < limit && controller.State != state; frame++)
            TickController(controller, 1);
        Assert.AreEqual(state, controller.State);
    }

    /// <summary>리소스 로딩과 분리된 테스트 구성을 위해 비공개 필드에 테스트 데이터를 넣는다.</summary>
    /// <param name="type">해당 필드가 선언된 형식이다.</param>
    /// <param name="instance">테스트 데이터를 받을 객체다.</param>
    /// <param name="name">대입할 필드의 정확한 이름이다.</param>
    /// <param name="value">테스트에서 사용할 필드 값이다.</param>
    private static void SetField(Type type, object instance, string name, object value)
    {
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
    }

    /// <summary>게임 데이터에 의존하지 않고 공통 이동 기능을 검증하는 최소 이동 주체다.</summary>
    private sealed class TestMovable : IMovable
    {
        /// <summary>테스트 캐릭터의 기본 월드 이동 속도다.</summary>
        public float MoveSpeed => 4f;
        /// <summary>공통 이동 컴포넌트가 기록하는 실제 이동 방향이다.</summary>
        public Vector2 CacheMoveDirection { get; set; }
        /// <summary>테스트에서는 직접 이동 컴포넌트를 호출하므로 추가 처리를 하지 않는다.</summary>
        /// <param name="direction">테스트 인터페이스 계약을 만족하는 이동 방향이다.</param>
        /// <param name="speedWeight">테스트 인터페이스 계약을 만족하는 속도 배율이다.</param>
        public void Move(Vector2 direction, float speedWeight = 1f) { }
        /// <summary>테스트에서는 직접 이동 컴포넌트를 호출하므로 추가 처리를 하지 않는다.</summary>
        /// <param name="destination">테스트 인터페이스 계약을 만족하는 목적지다.</param>
        /// <param name="speedWeight">테스트 인터페이스 계약을 만족하는 속도 배율이다.</param>
        /// <param name="onComplete">테스트에서 호출하지 않는 인터페이스 완료 콜백이다.</param>
        public void ForceMoveTo(Vector2 destination, float speedWeight = 1f, Action onComplete = null) { }
        /// <summary>이번 테스트 대상에 시간 기반 강제 이동이 포함되지 않음을 표시한다.</summary>
        /// <param name="direction">테스트 인터페이스 계약을 만족하는 강제 이동 방향이다.</param>
        /// <param name="duration">테스트 인터페이스 계약을 만족하는 유지 시간이다.</param>
        /// <param name="speedWeight">테스트 인터페이스 계약을 만족하는 속도 배율이다.</param>
        /// <param name="onComplete">테스트에서 호출하지 않는 인터페이스 완료 콜백이다.</param>
        public void ForceMoveFor(Vector2 direction, float duration, float speedWeight = 1f, Action onComplete = null) { }
        /// <summary>이번 테스트 대상에 외부 강제 이동 중단이 포함되지 않음을 표시한다.</summary>
        /// <param name="onComplete">테스트에서 호출하지 않는 인터페이스 완료 콜백이다.</param>
        public void StopForceMove(Action onComplete = null) { }
    }
}
