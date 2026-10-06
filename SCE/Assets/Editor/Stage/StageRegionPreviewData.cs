using System;
using System.Collections.Generic;
using Data;
using UnityEngine;

/// <summary>미리보기 1회분 결과. 원본(Scene/Export 파일)과 무관하게 같은 형태로 그린다.</summary>
public sealed class StageRegionPreviewData
{
    public GridSpec Spec;
    public int[] Cells = Array.Empty<int>();       // 0-base, 행 우선(y 오름차순)
    public readonly List<Vector2Int> OverlapCells = new();
    public float PlaneZ;
    public int ErrorCount;
    public int WarningCount;
    public string Status = "";

    public bool IsValid
    {
        get { return Cells.Length > 0 && Cells.Length == Spec.Width * Spec.Height; }
    }

    public void Clear()
    {
        Spec = default;
        Cells = Array.Empty<int>();
        OverlapCells.Clear();
        PlaneZ = 0f;
        ErrorCount = 0;
        WarningCount = 0;
        Status = "";
    }
}