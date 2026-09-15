using System;
using UnityEngine;
using UnityEngine.EventSystems;
using static Define;

/// <summary>
/// UI_Base에 적절한 EventHandler를 달아주고, UIEvent 타입에 맞는 이벤트를 구독하는 Class
/// </summary>

public static class UI_EventBinder
{
    public static void BindEvent(GameObject go, UIEvent type, Action<PointerEventData> callback)
    {
        if (type == UIEvent.None || callback == null) return;
        InternalBind(go, type, callback);
    }

    public static void BindEvent(GameObject go, UIEvent type1, Action<PointerEventData> cb1, UIEvent type2, Action<PointerEventData> cb2)
    {
        BindEvent(go, type1, cb1);
        BindEvent(go, type2, cb2);
    }

    public static void BindEvent(GameObject go, UIEvent type1, Action<PointerEventData> cb1, UIEvent type2, Action<PointerEventData> cb2, UIEvent type3, Action<PointerEventData> cb3)
    {
        BindEvent(go, type1, cb1);
        BindEvent(go, type2, cb2);
        BindEvent(go, type3, cb3);
    }

    // 기존에 사용하던 방식, params를 사용하면 호출할 때마다 Heap에 배열이 동작할당되어 GC 부담
    // 최근 C#에는 Stack에 할당되는 params ReadOnlySpan<T>가 있지만, Unity에서는 아직 지원되지 않음
    /*
    public static void BindEvent_Multiple(GameObject go, params (UIEvent type, Action<PointerEventData> callback)[] handlers)
    {
        foreach (var handler in handlers)
            BindEvent(go, handler.type, handler.callback);
    }
    */

    #region Internal Logic

    private static void InternalBind(GameObject go, UIEvent type, Action<PointerEventData> callback)
    {
        if (IsDragEvent(type))
        {
            var evt = Util.GetOrAddComponent<UI_DragEventHandler>(go);
            
            UnsubscribeDrag(evt, type, callback);
            SubscribeDrag(evt, type, callback);
        }
        else
        {
            var evt = Util.GetOrAddComponent<UI_PointerEventHandler>(go);
            
            UnsubscribePointer(evt, type, callback);
            SubscribePointer(evt, type, callback);
        }
    }

    // Helper
    private static bool IsDragEvent(UIEvent type)
    {
        return type == UIEvent.BeginDrag || type == UIEvent.Drag || type == UIEvent.EndDrag;
    }

    // Pointer 구독 관리
    private static void SubscribePointer(UI_PointerEventHandler evt, UIEvent type, Action<PointerEventData> cb)
    {
        switch (type)
        {
            case UIEvent.PointerEnter:  evt.OnPointerEnterHandler += cb; break;
            case UIEvent.PointerExit:   evt.OnPointerExitHandler += cb; break;
            case UIEvent.PointerDown:   evt.OnPointerDownHandler += cb; break;
            case UIEvent.PointerUp:     evt.OnPointerUpHandler += cb; break;
            case UIEvent.Click:         evt.OnClickHandler += cb; break;
        }
    }

    private static void UnsubscribePointer(UI_PointerEventHandler evt, UIEvent type, Action<PointerEventData> cb)
    {
        switch (type)
        {
            case UIEvent.PointerEnter:  evt.OnPointerEnterHandler -= cb; break;
            case UIEvent.PointerExit:   evt.OnPointerExitHandler -= cb; break;
            case UIEvent.PointerDown:   evt.OnPointerDownHandler -= cb; break;
            case UIEvent.PointerUp:     evt.OnPointerUpHandler -= cb; break;
            case UIEvent.Click:         evt.OnClickHandler -= cb; break;
        }
    }

    // Drag 구독 관리
    private static void SubscribeDrag(UI_DragEventHandler evt, UIEvent type, Action<PointerEventData> cb)
    {
        switch (type)
        {
            case UIEvent.BeginDrag: evt.OnBeginDragHandler += cb; break;
            case UIEvent.Drag:      evt.OnDragHandler += cb; break;
            case UIEvent.EndDrag:   evt.OnEndDragHandler += cb; break;
        }
    }

    private static void UnsubscribeDrag(UI_DragEventHandler evt, UIEvent type, Action<PointerEventData> cb)
    {
        switch (type)
        {
            case UIEvent.BeginDrag: evt.OnBeginDragHandler -= cb; break;
            case UIEvent.Drag:      evt.OnDragHandler -= cb; break;
            case UIEvent.EndDrag:   evt.OnEndDragHandler -= cb; break;
        }
    }
    #endregion
}