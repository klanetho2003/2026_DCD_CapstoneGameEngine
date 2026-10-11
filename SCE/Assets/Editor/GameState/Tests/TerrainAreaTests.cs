using Data;
using NUnit.Framework;
using UnityEngine;

public class TerrainAreaTests
{
    private static TerrainAreaData MakeData(int minX, int minY, params string[] rowsBottomToTop)
    {
        var data = new TerrainAreaData { MinX = minX, MinY = minY };
        data.Rows.AddRange(rowsBottomToTop);
        return data;
    }

    [Test]
    public void Contains_NonRectangularShape()
    {
        // y=4: 1 1 1
        // y=3: 1 0 0     (x = 2, 3, 4)
        TerrainAreaData data = MakeData(2, 3, "100", "111");

        Assert.IsTrue(TerrainArea.TryBuild(data, 10, 10, out TerrainArea area, out string error), error);
        Assert.AreEqual(4, area.CellCount);
        Assert.AreEqual(new RectInt(2, 3, 3, 2), area.Bounds);

        Assert.IsTrue(area.Contains(new Vector2Int(2, 3)));
        Assert.IsFalse(area.Contains(new Vector2Int(3, 3))); // 사각형 안이지만 칠하지 않은 셀
        Assert.IsTrue(area.Contains(new Vector2Int(4, 4)));
        Assert.IsFalse(area.Contains(new Vector2Int(1, 3))); // 왼쪽 밖
        Assert.IsFalse(area.Contains(new Vector2Int(2, 5))); // 위쪽 밖
        Assert.IsFalse(area.Contains(new Vector2Int(2, 2))); // 아래쪽 밖
    }

    [Test]
    public void GetWorldRect_UsesGridOriginAndCellSize()
    {
        Assert.IsTrue(TerrainArea.TryBuild(MakeData(2, 3, "100", "111"), 10, 10, out TerrainArea area, out string error), error);

        var grid = new GridSpec { OriginX = -5f, OriginY = -3f, CellSize = 2f, Width = 10, Height = 10 };
        Assert.AreEqual(new Rect(-1f, 3f, 6f, 4f), area.GetWorldRect(grid));
    }

    [Test]
    public void TryBuild_EmptyRows_Fails()
    {
        Assert.IsFalse(TerrainArea.TryBuild(new TerrainAreaData(), 10, 10, out TerrainArea area, out string error));
        Assert.IsNull(area);
        Assert.IsNotNull(error);
    }

    [Test]
    public void TryBuild_RowLengthMismatch_Fails()
    {
        Assert.IsFalse(TerrainArea.TryBuild(MakeData(0, 0, "111", "11"), 10, 10, out _, out string error));
        Assert.IsNotNull(error);
    }

    [Test]
    public void TryBuild_OutsideMapGrid_Fails()
    {
        Assert.IsFalse(TerrainArea.TryBuild(MakeData(8, 0, "111"), 10, 10, out _, out string error)); // 8 + 3 > 10
        Assert.IsNotNull(error);
    }

    [Test]
    public void TryBuild_NoInsideCell_Fails()
    {
        Assert.IsFalse(TerrainArea.TryBuild(MakeData(0, 0, "000"), 10, 10, out _, out string error));
        Assert.IsNotNull(error);
    }
}