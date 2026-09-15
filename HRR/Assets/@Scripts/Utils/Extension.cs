using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.EventSystems;
using static Define;
using Object = UnityEngine.Object;

public static class Extension
{
    #region UI Event Binding
    public static void BindEvent(this GameObject go, Action<PointerEventData> action, UIEvent type = UIEvent.Click)
    {
        UI_EventBinder.BindEvent(go, type, action);
    }

    public static void BindEvent(this GameObject go, Action<PointerEventData> action1, UIEvent type1, Action<PointerEventData> action2, UIEvent type2)
    {
        UI_EventBinder.BindEvent(go, type1, action1, type2, action2);
    }

    public static void BindEvent(this GameObject go, Action<PointerEventData> action1, UIEvent type1, Action<PointerEventData> action2, UIEvent type2, Action<PointerEventData> action3, UIEvent type3)
    {
        UI_EventBinder.BindEvent(go, type1, action1, type2, action2, type3, action3);
    }

    /// Component 버전들
    public static void BindEvent(this Component component, Action<PointerEventData> action, UIEvent type = UIEvent.Click)
    {
        UI_EventBinder.BindEvent(component.gameObject, type, action);
    }

    public static void BindEvent(this Component component, Action<PointerEventData> action1, UIEvent type1, Action<PointerEventData> action2, UIEvent type2)
    {
        UI_EventBinder.BindEvent(component.gameObject, type1, action1, type2, action2);
    }

    public static void BindEvent(this Component component, Action<PointerEventData> action1, UIEvent type1, Action<PointerEventData> action2, UIEvent type2, Action<PointerEventData> action3, UIEvent type3)
    {
        UI_EventBinder.BindEvent(component.gameObject, type1, action1, type2, action2, type3, action3);
    }
    #endregion

    #region Validation Check
    public static bool IsValid(this GameObject go)
    {
        return go != null && go.activeSelf;
    }

    public static bool IsValid(this UI_Base ui)
    {
        return ui != null && ui.gameObject.activeSelf;
    }

    public static bool IsValid(this CreatureBase creature)
    {
        // ToDo. Hp Check 추가

        return creature != null && creature.gameObject.activeSelf;
    }
    #endregion

    public static void AddLayer(this ref LayerMask mask, Define.ELayer layer)
    {
        mask |= (1 << (int)layer);
    }
}
