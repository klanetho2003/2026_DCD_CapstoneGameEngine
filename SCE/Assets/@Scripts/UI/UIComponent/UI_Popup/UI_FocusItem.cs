using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>포커스 가능한 UI 항목 최소 단위. 실제 프로젝트의 Button/Slider 등으로 대체될 자리.</summary>
public class UI_FocusItem : UI_Base
{
    public event Action Submitted;

    private Color defaultColor = Color.white;

    public void SetFocused(bool focused)
    {
        Color color = focused ? Color.red: defaultColor;

        GetComponent<Button>().image.color = color;
    }

    public void Submit()
    {
        Submitted?.Invoke();
    }
}