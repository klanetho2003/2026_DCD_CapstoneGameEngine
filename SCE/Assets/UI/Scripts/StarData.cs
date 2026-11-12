using UnityEngine;
using UnityEngine.UI;

/// <summary>생성된 별 하나의 위치·기본 색·기본 밝기와 그리는 Image를 담는다.</summary>
public class StarData
{
    /// <summary>생성 순서대로 붙인 별 번호다.</summary>
    public int Index { get; }
    /// <summary>픽셀아트 좌표(칸)이며 왼쪽 위가 (0,0)이다.</summary>
    public Vector2Int Cell { get; }
    /// <summary>투명도를 제외한 기본 색이다.</summary>
    public Color BaseColor { get; }
    /// <summary>기본 밝기(투명도)다 (예: 0.35 ~ 1.0).</summary>
    public float BaseAlpha { get; }
    /// <summary>이 별을 그리는 UI Image다.</summary>
    public Image Image { get; }

    public StarData(int index, Vector2Int cell, Color baseColor, float baseAlpha, Image image)
    {
        Index = index;
        Cell = cell;
        BaseColor = baseColor;
        BaseAlpha = baseAlpha;
        Image = image;
    }

    /// <summary>기본 색을 유지한 채 투명도만 지정한다.</summary>
    /// <param name="alpha">적용할 투명도(0~1)다.</param>
    public void SetAlpha(float alpha)
    {
        if (Image == null)
            return;
        Color color = BaseColor;
        color.a = Mathf.Clamp01(alpha);
        Image.color = color;
    }

    /// <summary>기본 색과 기본 밝기로 되돌린다.</summary>
    public void ResetBrightness()
    {
        SetAlpha(BaseAlpha);
    }
}
