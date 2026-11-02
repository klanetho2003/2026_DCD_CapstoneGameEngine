using System;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 카드를 잇는 선 — 디버거가 진행 경로의 선을 칠한다 (SetDebugColor — 색 · 조금 굵게). 고른 선 · 끄는 선은 GraphView 색 그대로.
/// GraphView가 선의 색을 다시 정하는 곳 (UpdateEdgeControl · 고르기 · 스타일)마다 칠한 색을 다시 입힌다
/// </summary>
public sealed class BehaviorGraphEdge : Edge
{
    private Color? _debugColor;

    /// <summary>칠한 색 — 없으면 null</summary>
    public Color? DebugColor => _debugColor;

    /// <summary>진행 경로의 색을 칠한다 (null이면 GraphView 색으로)</summary>
    public void SetDebugColor(Color? color)
    {
        if (Nullable.Equals(_debugColor, color))
            return;
        _debugColor = color;
        UpdateEdgeControl(); // GraphView 색을 다시 정하고 (그래프에 붙어 있을 때) 칠한 색을 입힌다
        ApplyDebugColor();
    }

    public override bool UpdateEdgeControl()
    {
        bool updated = base.UpdateEdgeControl();
        ApplyDebugColor();
        return updated;
    }

    public override void OnSelected()
    {
        base.OnSelected();
        ApplyDebugColor();
    }

    public override void OnUnselected()
    {
        base.OnUnselected();
        ApplyDebugColor();
    }

    protected override void OnCustomStyleResolved(ICustomStyle styles)
    {
        base.OnCustomStyleResolved(styles);
        ApplyDebugColor();
    }

    private void ApplyDebugColor()
    {
        if (_debugColor.HasValue == false || selected || isGhostEdge)
            return;
        Color color = _debugColor.Value;
        edgeControl.inputColor = color;
        edgeControl.outputColor = color;
        edgeControl.toCapColor = color;
        edgeControl.fromCapColor = color;
        edgeControl.edgeWidth = edgeWidth + 1;
    }
}