using System.Collections.Generic;
using UnityEngine;
using static Define;

/// <summary>박스의 실제 칸과 이동 중 차단, 밀기 주체만의 원래 칸 진입 권한을 관리한다.</summary>
public partial class MapManager
{
    /// <summary>실제 도착한 박스의 칸이며 캐릭터 선점 및 타일 소유권과 별도로 보관한다.</summary>
    private readonly Dictionary<Vector2Int, GridBox> _boxes = new();
    /// <summary>이동 중 박스가 도착할 칸을 다른 주체가 차지하지 못하도록 임시 차단한다.</summary>
    private readonly Dictionary<Vector2Int, GridBox> _boxDestinations = new();
    /// <summary>현재 박스를 미는 플레이어에게만 박스의 원래 칸 진입을 허용한다.</summary>
    private readonly Dictionary<GridBox, CreatureMovement> _boxPushers = new();
    /// <summary>취소 중 사전이 변경되어도 안전하게 순회하기 위한 재사용 밀기 목록이다.</summary>
    private readonly List<BoxPushController> _boxPushBuffer = new();

    /// <summary>박스 차단을 제외한 벽, 외부 차단, 좌표 범위 및 현재 라운드 영역을 검사한다.</summary>
    /// <param name="cell">실제 바닥으로 통행 가능한지 검사할 타일이다.</param>
    private bool IsTerrainCellWalkable(Vector2Int cell)
    {
        return IsLoaded && cell.x >= 0 && cell.x < _grid.Width &&
            cell.y >= 0 && cell.y < _grid.Height &&
            CurrentMap.CollisionRows[cell.y][cell.x] != MAP_TOOL_WALL &&
            !_blockedCells.Contains(cell) && !IsOutsideArea(cell);
    }

    /// <summary>박스와 그 도착 예정 칸을 차단하며 현재 밀기 주체에게만 원래 박스 칸을 허용한다.</summary>
    /// <param name="cell">박스의 공간 차단을 검사할 타일이다.</param>
    /// <param name="requester">밀기 참여자 여부를 확인할 이동 주체이며 일반 조회에서는 null이다.</param>
    private bool IsBoxCellAccessible(Vector2Int cell, CreatureMovement requester)
    {
        // destinationBox는 이 칸으로 이동 중인 박스이며 누구에게나 진입을 제한한다.
        if (_boxDestinations.TryGetValue(cell, out GridBox destinationBox) && destinationBox != null)
            return false;
        // box는 아직 이 칸을 논리 위치로 보유한 실제 박스다.
        if (!_boxes.TryGetValue(cell, out GridBox box) || box == null)
            return true;
        // pusher는 현재 박스의 원래 칸에 따라 들어갈 수 있는 유일한 플레이어다.
        return requester != null && box.IsMoving &&
            _boxPushers.TryGetValue(box, out CreatureMovement pusher) && pusher == requester;
    }

    /// <summary>타일에 배치된 실제 박스를 조회하며 파괴된 박스는 없는 것으로 취급한다.</summary>
    /// <param name="cell">박스를 찾을 현재 타일이다.</param>
    /// <param name="box">해당 칸에 등록된 박스이며 없으면 null이다.</param>
    public bool TryGetBox(Vector2Int cell, out GridBox box)
    {
        return _boxes.TryGetValue(cell, out box) && box != null;
    }

    /// <summary>다른 물체와 겹치지 않는 현재 박스의 실제 칸을 통행 차단에 등록한다.</summary>
    /// <param name="box">해당 칸을 차지하는 박스 인스턴스다.</param>
    /// <param name="cell">박스를 등록할 논리 타일 좌표다.</param>
    public bool TryRegisterBox(GridBox box, Vector2Int cell)
    {
        if (box == null || !IsTerrainCellWalkable(cell) || IsPlayerSpawnCell(cell))
            return false;
        // existing은 같은 박스의 중복 등록만 허용하기 위해 확인할 현재 박스다.
        if (_boxes.TryGetValue(cell, out GridBox existing) && existing == box)
            return true;
        if (!CanEnterCell(cell, null))
            return false;
        _boxes[cell] = box;
        TraversalVersion++;
        return true;
    }

    /// <summary>플레이어가 최초 스폰부터 박스와 겹치지 않도록 저작 스폰 칸을 검사한다.</summary>
    /// <param name="cell">박스를 놓으려는 타일 좌표다.</param>
    private bool IsPlayerSpawnCell(Vector2Int cell)
    {
        if (CurrentMap.Stages == null)
            return false;
        // stage는 현재 맵에 기록된 구역별 플레이어 스폰 정보다.
        foreach (Data.StageData stage in CurrentMap.Stages)
        {
            if (stage != null && stage.HasPlayerSpawn && stage.PlayerSpawnInfo.Cell == cell)
                return true;
        }
        return false;
    }

    /// <summary>새 라운드의 박스 초기 배치와 플레이어의 유지 위치가 겹치는지 조회한다.</summary>
    /// <param name="cell">플레이어가 서 있는 칸이다.</param>
    public bool IsBoxSpawnCell(Vector2Int cell)
    {
        return _view.IsBoxSpawnCell(cell);
    }

    /// <summary>플레이어 재배치가 끝난 뒤 활성 박스의 최종 등록을 다시 시도한다.</summary>
    public void RefreshBoxes()
    {
        _view.InitializeBoxes(this);
    }

    /// <summary>자기 박스의 칸과 예약만 해제하여 다른 박스나 새 맵의 등록을 보호한다.</summary>
    /// <param name="box">맵에서 해제할 박스 인스턴스다.</param>
    public void UnregisterBox(GridBox box)
    {
        if (box == null)
            return;
        // changed는 이번 해제로 실제 차단 정보가 달라졌는지 나타낸다.
        bool changed = false;
        // existing은 다른 박스의 등록을 지우지 않도록 비교할 현재 칸의 박스다.
        if (_boxes.TryGetValue(box.CurrentCell, out GridBox existing) && existing == box)
            changed |= _boxes.Remove(box.CurrentCell);
        // reserved는 도착 예정 칸에서 제거하려는 예약이 자기 것인지 확인할 박스다.
        if (_boxDestinations.TryGetValue(box.TargetCell, out GridBox reserved) && reserved == box)
            changed |= _boxDestinations.Remove(box.TargetCell);
        changed |= _boxPushers.Remove(box);
        if (changed)
            TraversalVersion++;
    }

    /// <summary>한 박스만 밀 수 있고 다음 칸에 벽, 박스, 캐릭터 또는 진입 중인 캐릭터가 없는지 확인한다.</summary>
    /// <param name="box">플레이어 정면 한 칸에 있는 박스다.</param>
    /// <param name="pusher">실제 칸을 선점하고 있는 플레이어 이동 주체다.</param>
    /// <param name="direction">밀려는 상하좌우 한 칸 방향이다.</param>
    public bool CanPushBox(GridBox box, CreatureMovement pusher, Vector2Int direction)
    {
        // current는 요청한 박스가 실제 원래 칸의 등록 주체인지 확인할 조회 결과다.
        if (box == null || !box.HasCell || box.IsMoving || pusher == null ||
            !pusher.HasCell || !pusher.CanOccupyCell || !pusher.IsPlayerControlled ||
            pusher.IsMoving || (HasTerritoryRound && !IsTerritoryOpen) ||
            Mathf.Abs(direction.x) + Mathf.Abs(direction.y) != 1 ||
            pusher.CurrentCell + direction != box.CurrentCell ||
            !TryGetBox(box.CurrentCell, out GridBox current) || current != box ||
            _boxPushers.ContainsKey(box))
            return false;
        // destination은 박스를 한 칸 밀었을 때 차지할 타일이다.
        Vector2Int destination = box.CurrentCell + direction;
        return CanEnterCell(destination, null) && !HasIncomingCreature(destination, pusher);
    }

    /// <summary>현재 비어 보여도 다른 캐릭터가 이미 향하고 있는 칸인지 확인한다.</summary>
    /// <param name="cell">박스가 도착할 후보 타일이다.</param>
    /// <param name="except">자기 밀기 요청을 검사에서 제외할 플레이어다.</param>
    private bool HasIncomingCreature(Vector2Int cell, CreatureMovement except)
    {
        // entry는 실제 칸을 선점하고 있는 모든 캐릭터의 이동 상태다.
        foreach (KeyValuePair<Vector2Int, CreatureMovement> entry in _occupants)
        {
            // movement는 아직 도착하지 않은 목적지가 겹치는지 검사할 주체다.
            CreatureMovement movement = entry.Value;
            if (movement != null && movement != except && movement.CanOccupyCell &&
                movement.IsMoving && !movement.IsReturning && movement.TargetCell == cell)
                return true;
        }
        return false;
    }

    /// <summary>검증된 박스 밀기의 목적지를 임시 차단하고 밀기 주체의 진입 권한을 등록한다.</summary>
    /// <param name="box">이동할 박스다.</param>
    /// <param name="pusher">박스 원래 칸으로 따라 들어갈 플레이어다.</param>
    /// <param name="direction">상하좌우로 한 칸 이동할 방향이다.</param>
    public bool TryReserveBoxPush(GridBox box, CreatureMovement pusher, Vector2Int direction)
    {
        if (!CanPushBox(box, pusher, direction))
            return false;
        _boxDestinations.Add(box.CurrentCell + direction, box);
        _boxPushers.Add(box, pusher);
        TraversalVersion++;
        return true;
    }

    /// <summary>밀기 도중 벽이나 라운드 영역이 바뀌어 진행할 수 없게 되었는지 다시 확인한다.</summary>
    /// <param name="box">현재 한 칸 이동 중인 박스다.</param>
    /// <param name="pusher">처음 밀기를 시작한 플레이어다.</param>
    public bool IsReservedBoxPushValid(GridBox box, CreatureMovement pusher)
    {
        // registered와 reserved는 같은 밀기의 참여자 및 목적지 예약이 유지되는지 확인할 조회 결과다.
        return box != null && pusher != null &&
            _boxPushers.TryGetValue(box, out CreatureMovement registered) && registered == pusher &&
            _boxDestinations.TryGetValue(box.TargetCell, out GridBox reserved) && reserved == box &&
            IsTerrainCellWalkable(box.CurrentCell) && IsTerrainCellWalkable(box.TargetCell);
    }

    /// <summary>도착한 박스의 차단 칸을 옮기거나 원래 칸으로 복원하고 임시 권한을 해제한다.</summary>
    /// <param name="box">이번 이동을 마친 박스다.</param>
    /// <param name="pusher">이 예약을 만든 실제 플레이어다.</param>
    /// <param name="commit">참이면 목표 칸, 거짓이면 출발 칸에 박스를 남긴다.</param>
    public void FinishBoxPush(GridBox box, CreatureMovement pusher, bool commit)
    {
        // registered는 다른 실행기가 만든 밀기 예약을 해제하지 않도록 확인할 실제 참여자다.
        if (box == null || !_boxPushers.TryGetValue(box, out CreatureMovement registered) || registered != pusher)
            return;
        // origin은 밀기 직전 박스가 차지하던 칸이다.
        Vector2Int origin = box.CurrentCell;
        // target은 예약으로 보호했던 박스의 도착 칸이다.
        Vector2Int target = box.TargetCell;
        _boxDestinations.Remove(target);
        _boxPushers.Remove(box);
        if (commit)
        {
            _boxes.Remove(origin);
            _boxes[target] = box;
        }
        box.FinishPush(commit ? target : origin);
        TraversalVersion++;
    }

    /// <summary>라운드 전환과 맵 이탈 시 실행 중인 밀기를 안전한 복사 목록으로 취소한다.</summary>
    private void CancelBoxPushes()
    {
        _boxPushBuffer.Clear();
        // entry는 밀기를 진행 중인 박스와 참여 플레이어의 등록이다.
        foreach (KeyValuePair<GridBox, CreatureMovement> entry in _boxPushers)
        {
            if (entry.Key != null && entry.Key.ActivePush != null)
                _boxPushBuffer.Add(entry.Key.ActivePush);
        }
        // controller는 순회 중 원본 사전을 변경할 수 있는 실제 밀기 실행기다.
        foreach (BoxPushController controller in _boxPushBuffer)
            controller.CancelPush();
        _boxPushBuffer.Clear();
    }
}
