using UnityEngine;

/// <summary>픽셀아트 좌표(칸)를 Canvas 좌표(기준 해상도 px)로 바꾼다. 왼쪽 위 칸이 (0,0)이고 화면 중앙이 Canvas 좌표 (0,0)이다.</summary>
public readonly struct PixelGrid
{
    /// <summary>원본 픽셀아트의 가로 칸 수다 (예: 640).</summary>
    public readonly int DesignWidth;
    /// <summary>원본 픽셀아트의 세로 칸 수다 (예: 360).</summary>
    public readonly int DesignHeight;
    /// <summary>픽셀아트 1칸이 Canvas 기준 해상도에서 몇 px인지다 (예: 3).</summary>
    public readonly int PixelScale;

    public PixelGrid(int designWidth, int designHeight, int pixelScale)
    {
        DesignWidth = Mathf.Max(1, designWidth);
        DesignHeight = Mathf.Max(1, designHeight);
        PixelScale = Mathf.Max(1, pixelScale);
    }

    /// <summary>Canvas 기준 가로 px다 (예: 640 × 3 = 1920).</summary>
    public int ScreenWidth => DesignWidth * PixelScale;

    /// <summary>Canvas 기준 세로 px다 (예: 360 × 3 = 1080).</summary>
    public int ScreenHeight => DesignHeight * PixelScale;

    /// <summary>칸 (x, y)의 중앙 Canvas 좌표다. 피벗이 중앙인 RectTransform의 anchoredPosition으로 쓴다.</summary>
    /// <param name="x">왼쪽에서부터의 칸 번호다.</param>
    /// <param name="y">위에서부터의 칸 번호다.</param>
    public Vector2 CellCenter(int x, int y)
    {
        return new Vector2(
            -ScreenWidth * 0.5f + (x + 0.5f) * PixelScale,
            ScreenHeight * 0.5f - (y + 0.5f) * PixelScale);
    }
}
