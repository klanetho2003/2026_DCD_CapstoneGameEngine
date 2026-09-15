using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static Define;
using static LogPrinter;

/// <summary>
/// 모든 UI의 부모 class
/// UI 내부 요소들을 Bind하고 Get할 수 있는 Helper Method와 
/// UI Event를 바인딩하는 Helper Method들을 제공
/// </summary>

public class UI_Base : InitBase
{
    protected Dictionary<Type, Object[]> _objectDic = new Dictionary<Type, Object[]>();

    #region UI Bind

    /// <summary>
    /// Index 기반으로 UI 요소들을 바인딩하는 방법 사용.
    /// Index는 enum의 순서에 따라 결정되며, enum의 이름과 동일한 GameObject 또는 Component를 찾아서 바인딩
    /// Get<T>(int idx) 메서드를 통해 해당 UI 요소에 접근
    /// </summary>

    protected void Bind<T>(Type enumType) where T : Object
    {
        string[] names = Enum.GetNames(enumType); // (리플렉션) enum에 들어가 있는 값들을 string으로 변환해서 array에 push
        T[] arr = new T[names.Length];

        for (int i = 0; i < names.Length; i++)
        {
            T found;

            if (typeof(T) == typeof(GameObject))
                found = Util.FindChild(gameObject, names[i], true) as T;
            else
                found = Util.FindChild<T>(gameObject, names[i], true);

            // Failed to Bind
            if (found == null) Log($"<color=red> Failed to Bind -> {names[i]} </color>");

            arr[i] = found;
        }

        // Binding된 요소들을 Object로 Upcast해서 Dictionary에 저장
        _objectDic[enumType] = arr;
    }

    /// Helper Bind Methods
    public void BindObjects(Type enumType) { Bind<GameObject>(enumType); }
    public void BindImages(Type enumType) { Bind<Image>(enumType); }
    public void BindTexts(Type enumType, bool isBindAtTextManager = true) { Bind<TMP_Text>(enumType); if (isBindAtTextManager == false) return; /*Managers.Text.BindTexts(GetAllTexts());*/ }
    public void BindButtons(Type enumType) { Bind<Button>(enumType); }
    public void BindToggles(Type enumType) { Bind<Toggle>(enumType); }
    public void BindSliders(Type enumType) { Bind<Slider>(enumType); }
    #endregion

    #region UI Get
    /// <summary>
    /// /// Index 기반으로 UI 요소들을 바인딩하는 방법 사용.
    /// Index는 enum의 순서에 따라 결정되며, enum의 이름과 동일한 GameObject 또는 Component를 찾아서 바인딩
    /// Get<T>(int idx) 메서드를 통해 해당 UI 요소에 접근
    /// </summary>
    /// 
    protected T Get<T>(Enum enumKey) where T : Object
    {
        if (_objectDic.TryGetValue(enumKey.GetType(), out Object[] objects) == false)
            return null;

        // index 접근 방법 사용 후, Object 배열에서 T 타입으로 Downcast해서 반환
        int idx = Convert.ToInt32(enumKey);
        return objects[idx] as T;
    }

    protected bool TryGetAll<T>(out T[] arr) where T : Object
    {
        List<T> list = new List<T>();

        foreach (var baseArr in _objectDic.Values)
        {
            if (baseArr is T[] typed)
            {
                list.AddRange(typed);
            }
        }

        arr = list.ToArray();
        return arr.Length > 0;
    }

    /// Helper Get Methods
    public GameObject[] GetAllObjects() { TryGetAll<GameObject>(out var arr); return arr; }
    public TMP_Text[] GetAllTexts()     { TryGetAll<TMP_Text>(out var arr); return arr; }
    public Button[] GetAllButtons()     { TryGetAll<Button>(out var arr); return arr; }
    public Image[] GetAllImages()       { TryGetAll<Image>(out var arr); return arr; }
    public Toggle[] GetAllToggles()     { TryGetAll<Toggle>(out var arr); return arr; }
    public Slider[] GetAllSliders()     { TryGetAll<Slider>(out var arr); return arr; }

    protected GameObject GetObject(Enum enumKey)    { return Get<GameObject>(enumKey); }
    protected TMP_Text GetText(Enum enumKey)        { return Get<TMP_Text>(enumKey); }
    protected Button GetButton(Enum enumKey)        { return Get<Button>(enumKey); }
    protected Image GetImage(Enum enumKey)          { return Get<Image>(enumKey); }
    protected Toggle GetToggle(Enum enumKey)        { return Get<Toggle>(enumKey); }
    protected Slider GetSliders(Enum enumKey)       { return Get<Slider>(enumKey); }
    #endregion

    #region Bind UI Event Helper
    /// GameObject 버전들
    public void BindEvent(GameObject go, Action<PointerEventData> action, UIEvent type = UIEvent.Click)
    {
        UI_EventBinder.BindEvent(go, type, action);
    }

    public void BindEvent(GameObject go, Action<PointerEventData> action1, UIEvent type1, Action<PointerEventData> action2, UIEvent type2)
    {
        UI_EventBinder.BindEvent(go, type1, action1, type2, action2);
    }

    public void BindEvent(GameObject go, Action<PointerEventData> action1, UIEvent type1, Action<PointerEventData> action2, UIEvent type2, Action<PointerEventData> action3, UIEvent type3)
    {
        UI_EventBinder.BindEvent(go, type1, action1, type2, action2, type3, action3);
    }

    /// Component 버전들
    public void BindEvent(Component component, Action<PointerEventData> action, UIEvent type = UIEvent.Click)
    {
        UI_EventBinder.BindEvent(component.gameObject, type, action);
    }

    public void BindEvent(Component component, Action<PointerEventData> action1, UIEvent type1, Action<PointerEventData> action2, UIEvent type2)
    {
        UI_EventBinder.BindEvent(component.gameObject, type1, action1, type2, action2);
    }

    public void BindEvent(Component component, Action<PointerEventData> action1, UIEvent type1, Action<PointerEventData> action2, UIEvent type2, Action<PointerEventData> action3, UIEvent type3)
    {
        UI_EventBinder.BindEvent(component.gameObject, type1, action1, type2, action2, type3, action3);
    }
    #endregion
}
