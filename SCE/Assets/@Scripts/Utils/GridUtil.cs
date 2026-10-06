using UnityEngine;
using Data;

public static class GridUtil
{
    /// <summary>
    /// 월드 좌표를 격자(Cell) 좌표로 변환합니다.
    /// </summary>
    public static Vector2Int WorldToCell(in GridSpec grid, Vector3 worldPos)
    {
        int cellX = Mathf.FloorToInt((worldPos.x - grid.OriginX) / grid.CellSize);
        int cellY = Mathf.FloorToInt((worldPos.y - grid.OriginY) / grid.CellSize);

        // Vector2Int의 y 컴포넌트가 Z축 인덱스를 담당
        return new Vector2Int(cellX, cellY);
    }

    /// <summary>
    /// 격자(Cell) 좌표를 해당 셀의 중심 좌표로 변환
    /// </summary>
    public static Vector2 CellCenterToWorld(in GridSpec grid, Vector2Int cell, float z = 0f)
    {
        // cell(x,z)의 중심 >> Origin + (x + 0.5, z + 0.5) * CellSize
        float worldX = grid.OriginX + (cell.x + 0.5f) * grid.CellSize;
        float worldY = grid.OriginY + (cell.y + 0.5f) * grid.CellSize;

        return new Vector2(worldX, worldY);
    }

    /// <summary>
    /// footprint 한 변의 월드 길이.
    /// 3*3 = FootprintWorldSize(grid, 1) = 3 * CellSize
    /// 1*1 = FootprintWorldSize(grid, 0) = 1 * CellSize
    /// </summary>
    public static float FootprintWorldSize(in GridSpec grid, int footprintRadius)
    {
        return (footprintRadius * 2 + 1) * grid.CellSize;
    }

    /// <summary>
    /// 임의 월드 좌표를 포함 셀의 중심으로 snap. z 보존.
    /// </summary>
    public static Vector2 SnapToCellCenter(in GridSpec grid, Vector3 worldPos)
    {
        Vector2Int cell = WorldToCell(grid, worldPos);
        return CellCenterToWorld(grid, cell, worldPos.z);
    }
}