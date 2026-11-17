using System.Collections.Generic;
using UnityEngine;

/// <summary>부서지는 발판의 칸 등록과, 발판이 부서진 칸을 이번 라운드의 타일 데이터에서 빼는 처리를 맡는다.</summary>
public partial class MapManager
{
    /// <summary>칸마다 놓인 부서지는 발판이다. 부서진 발판도 라운드가 바뀔 때까지 남는다 (IsBroken).</summary>
    private readonly Dictionary<Vector2Int, GridCrumbleTile> _crumbleTiles = new();

    /// <summary>칸에 등록된 부서지는 발판을 찾는다. 파괴된 오브젝트는 없는 것으로 본다. O(1)</summary>
    /// <param name="cell">발판을 찾을 칸이다.</param>
    /// <param name="tile">그 칸의 발판이며 없으면 null이다.</param>
    public bool TryGetCrumbleTile(Vector2Int cell, out GridCrumbleTile tile)
    {
        return _crumbleTiles.TryGetValue(cell, out tile) && tile != null;
    }

    /// <summary>현재 라운드에서 밟을 수 있는 칸에만 발판을 등록한다. 같은 칸에 다른 발판이 있으면 false다.</summary>
    /// <param name="tile">등록할 발판이다.</param>
    /// <param name="cell">발판이 놓인 칸이다.</param>
    public bool TryRegisterCrumbleTile(GridCrumbleTile tile, Vector2Int cell)
    {
        if (tile == null || !IsTerrainCellWalkable(cell))
            return false;
        // existing은 다른 발판의 등록을 덮지 않도록 확인할 기존 발판이다.
        if (_crumbleTiles.TryGetValue(cell, out GridCrumbleTile existing) && existing != null && existing != tile)
            return false;
        _crumbleTiles[cell] = tile;
        return true;
    }

    /// <summary>자기 발판의 등록만 해제하여 다른 발판의 기록을 보호한다.</summary>
    /// <param name="tile">해제할 발판이다.</param>
    public void UnregisterCrumbleTile(GridCrumbleTile tile)
    {
        // existing은 같은 칸에 등록된 발판이 자기 자신인지 확인할 조회 결과다.
        if (tile != null && _crumbleTiles.TryGetValue(tile.Cell, out GridCrumbleTile existing) && existing == tile)
            _crumbleTiles.Remove(tile.Cell);
    }

    /// <summary>
    /// 칸을 이번 라운드의 타일 데이터에서 뺀다 — 조종 캐릭터와 몬스터 모두 들어올 수 없고 점령도 지운다.
    /// 그림(바닥 타일)은 호출자가 정한 때에 ClearGroundTile로 지운다.
    /// </summary>
    /// <param name="cell">없앨 칸이다.</param>
    public void RemoveTile(Vector2Int cell)
    {
        SetCellBlocked(cell, true);
        if (_territory.Remove(cell))
            _view.SetTerritoryOwner(cell, TileOwner.Neutral);
    }

    /// <summary>현재 라운드 바닥(Tilemap_Terrain)에서 칸의 그림을 지운다.</summary>
    /// <param name="cell">그림을 지울 칸이다.</param>
    public void ClearGroundTile(Vector2Int cell)
    {
        _view.ClearRoundGround(CellCenterToWorld(cell));
    }
}
