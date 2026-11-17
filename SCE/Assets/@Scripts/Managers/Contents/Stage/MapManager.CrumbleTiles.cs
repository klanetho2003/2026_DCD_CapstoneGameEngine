using UnityEngine;

/// <summary>칸을 이번 라운드의 타일 데이터에서 빼는 처리(부서지는 발판 등)와 라운드 전환 알림을 맡는다.</summary>
public partial class MapManager
{
    /// <summary>라운드가 바뀐 뒤(SetRound 끝) 1회. 라운드 수명 상태를 가진 개체(부서지는 발판 등)가 처음 상태로 돌아간다.</summary>
    public event System.Action RoundChanged;

    /// <summary>
    /// 칸을 이번 라운드의 타일 데이터에서 뺀다 — 조종 캐릭터와 몬스터 모두 들어올 수 없고 점령도 지운다.
    /// 그림(바닥 타일)은 호출자가 정한 때에 ClearGroundTile로 지운다.
    /// </summary>
    /// <param name="cell">없앨 칸이다.</param>
    public void RemoveTile(Vector2Int cell)
    {
        if (!IsLoaded)
            return;
        SetCellBlocked(cell, true);
        if (_territory.Remove(cell))
            _view.SetTerritoryOwner(cell, TileOwner.Neutral);
    }

    /// <summary>현재 라운드 바닥(Tilemap_Terrain)에서 칸의 그림을 지운다.</summary>
    /// <param name="cell">그림을 지울 칸이다.</param>
    public void ClearGroundTile(Vector2Int cell)
    {
        if (IsLoaded)
            _view.ClearRoundGround(CellCenterToWorld(cell));
    }
}
