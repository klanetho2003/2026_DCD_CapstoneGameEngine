using Data;
using UnityEngine;
using static Define;

/// <summary>
/// 영역 — Tilemap_Terrain에 칠한 셀 집합의 런타임 형태. 만든 뒤에는 바뀌지 않는다.
/// TerrainAreaData(JSON)의 유일한 디코더.
/// 메모리: 최소 사각형의 셀당 1바이트. 조회: O(1), 할당 없음.
/// </summary>
public sealed class TerrainArea
{
    private readonly bool[] _inside; // 최소 사각형 안의 채움 여부, 행 우선

    // RectInt 필드 하나로 두지 않는다 — readonly 필드의 구조체는 프로퍼티를 읽을 때마다 방어 복사가 일어난다
    private readonly int _minX;
    private readonly int _minY;
    private readonly int _width;
    private readonly int _height;

    /// <summary>영역에 속한 셀 수.</summary>
    public int CellCount { get; }

    /// <summary>영역을 감싸는 최소 사각형 (맵 셀 좌표).</summary>
    public RectInt Bounds { get { return new RectInt(_minX, _minY, _width, _height); } }

    private TerrainArea(bool[] inside, int minX, int minY, int width, int height, int cellCount)
    {
        _inside = inside;
        _minX = minX;
        _minY = minY;
        _width = width;
        _height = height;
        CellCount = cellCount;
    }

    /// <summary>셀이 영역 안인가. 최소 사각형 밖이면 배열을 보지 않고 false.</summary>
    public bool Contains(Vector2Int cell)
    {
        int x = cell.x - _minX;
        int y = cell.y - _minY;
        if ((uint)x >= (uint)_width || (uint)y >= (uint)_height)
            return false;
        return _inside[y * _width + x];
    }

    /// <summary>최소 사각형의 월드 범위. 카메라 경계처럼 사각형이면 충분한 곳에 쓴다.</summary>
    public Rect GetWorldRect(in GridSpec grid)
    {
        return new Rect(
            grid.OriginX + _minX * grid.CellSize,
            grid.OriginY + _minY * grid.CellSize,
            _width * grid.CellSize,
            _height * grid.CellSize);
    }

    /// <summary>TerrainAreaData → TerrainArea. 실패하면 false와 사유. Managers에 의존하지 않는다.</summary>
    public static bool TryBuild(TerrainAreaData data, int mapWidth, int mapHeight, out TerrainArea area, out string error)
    {
        area = null;

        if (data == null || data.Rows == null || data.Rows.Count == 0)
        {
            error = "영역 데이터가 비어 있음 — 재Export 필요";
            return false;
        }

        int height = data.Rows.Count;
        int width = data.Rows[0] != null ? data.Rows[0].Length : 0;
        if (width == 0)
        {
            error = "영역의 첫 행이 비어 있음";
            return false;
        }
        if (data.MinX < 0 || data.MinY < 0 || data.MinX + width > mapWidth || data.MinY + height > mapHeight)
        {
            error = $"영역이 맵 격자({mapWidth}x{mapHeight}) 밖으로 나감 — Min({data.MinX},{data.MinY}), 크기 {width}x{height}";
            return false;
        }

        var inside = new bool[width * height];
        int count = 0;
        for (int y = 0; y < height; y++)
        {
            string row = data.Rows[y];
            if (row == null || row.Length != width)
            {
                error = $"영역 Rows[{y}]의 길이가 첫 행({width})과 다름";
                return false;
            }

            int offset = y * width;
            for (int x = 0; x < width; x++)
            {
                if (row[x] != MAP_AREA_INSIDE)
                    continue;
                inside[offset + x] = true;
                count++;
            }
        }

        if (count == 0)
        {
            error = "영역에 속한 셀이 없음";
            return false;
        }

        area = new TerrainArea(inside, data.MinX, data.MinY, width, height, count);
        error = null;
        return true;
    }
}