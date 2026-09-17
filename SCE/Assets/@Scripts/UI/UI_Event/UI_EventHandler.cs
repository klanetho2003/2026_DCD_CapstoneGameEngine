using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Click류와 Drag류를 동시에 사용해야 하는 경우가 있기에 Handler를 분리
/// Clickable한 대상이면서 Draggable한 대상이기도 한 경우
/// ex) click하면 Detail View가 열리고, Drag하면 위치 이동이 되는 UI 요소
/// </summary>

public class UI_PointerEventHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler
{
    public event Action<PointerEventData> OnPointerEnterHandler;
    public event Action<PointerEventData> OnPointerExitHandler;
    public event Action<PointerEventData> OnClickHandler;
    public event Action<PointerEventData> OnPointerDownHandler;
    public event Action<PointerEventData> OnPointerUpHandler;

    public void OnPointerEnter(PointerEventData eventData) { OnPointerEnterHandler?.Invoke(eventData); }
    public void OnPointerExit(PointerEventData eventData) { OnPointerExitHandler?.Invoke(eventData); }
    public void OnPointerClick(PointerEventData eventData) { OnClickHandler?.Invoke(eventData); }
    public void OnPointerDown(PointerEventData eventData) { OnPointerDownHandler?.Invoke(eventData); }
    public void OnPointerUp(PointerEventData eventData) { OnPointerUpHandler?.Invoke(eventData); }
}

public class UI_DragEventHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public event Action<PointerEventData> OnBeginDragHandler;
    public event Action<PointerEventData> OnDragHandler;
    public event Action<PointerEventData> OnEndDragHandler;

    public void OnBeginDrag(PointerEventData eventData) { OnBeginDragHandler?.Invoke(eventData); }
    public void OnDrag(PointerEventData eventData) { OnDragHandler?.Invoke(eventData); }
    public void OnEndDrag(PointerEventData eventData) { OnEndDragHandler?.Invoke(eventData); }
}
