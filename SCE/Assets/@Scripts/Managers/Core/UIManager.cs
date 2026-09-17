using System;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI 관리 Manager Class
/// Scene마다 고유한 UI는 SceneUI로 관리,
/// 쌓이는 형태의 UI는 Popup으로 관리,
/// Popup하위에 배치되는 UI는 UI_Base로 관리,
/// World Space에 배치하는 UI는 UI_WorldSpace로 관리,
/// </summary>

public class UIManager
{
    private readonly Vector2 DEFAULT_SCREEN_RESOLUTION = new Vector2(1920, 1080);

    // 모든 UI의 Root
    private Transform _uiRoot = null;
    public Transform UIRoot
    {
        get
        {
            if (_uiRoot == null)
            {
                _uiRoot = GameObject.Find("@UI_Root")?.transform;

                if (_uiRoot == null)
                    _uiRoot = new GameObject { name = "@UI_Root" }.transform;
            }

            return _uiRoot;
        }
    }

    // Cache CurrentInputHandler Scene UI // Scene마다 고유한 UI
    public UI_Scene CurrentSceneUI { get; private set; }

    int _order = 10;
    // Cache All Popup Prefabs
    private Dictionary<string, UI_Popup> _popups = new Dictionary<string, UI_Popup>();
    // UI Popup Order 관리용
    private Stack<UI_Popup> _popupStack = new Stack<UI_Popup>();
    public int GetPopupCount() { return _popupStack.Count; }

    #region UI Base (공용)
    public T ShowBaseUI<T>(string name = null, int sortOrder = 0, Transform parent = null, bool pooling = false, RenderMode renderMode = RenderMode.ScreenSpaceCamera) where T : UI_Base
    {
        if (string.IsNullOrEmpty(name))
            name = typeof(T).Name;

        if (parent == null)
            parent = UIRoot;

        GameObject go = Managers.Resource.Instantiate(name, parent, pooling);

        T baseUI = Util.GetOrAddComponent<T>(go);

        SetCanvas(go, renderMode, false, sortOrder);

        return baseUI;
    }

    public void CloseBaseUI<T>(T uiBase) where T : UI_Base
    {
        Managers.Resource.Destroy(uiBase.gameObject);
    }

    public void SetCanvas(GameObject go, RenderMode renderMode, bool sort = true, int sortOrder = 0)
    {
        Canvas canvas = go.GetOrAddComponent<Canvas>();

        canvas.renderMode = renderMode;
        canvas.overrideSorting = true;
        canvas.planeDistance = 1f; // Screen Space - Camera Canvas를 카메라 가까이에 배치

        // Add. 해상도에 따라 크기 조절을 위함
        CanvasScaler canvasScaler = go.GetOrAddComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvas.worldCamera = Camera.main;
        canvasScaler.referenceResolution = DEFAULT_SCREEN_RESOLUTION;

        // Add. Raycast를 위함
        go.GetOrAddComponent<GraphicRaycaster>();

        if (sort)
        {
            canvas.sortingOrder = _order;
            _order++;
        }
        else
        {
            canvas.sortingOrder = sortOrder;
        }
    }
    #endregion

    #region SceneUI
    public T GetSceneUI<T>() where T : UI_Base
    {
        return CurrentSceneUI as T;
    }

    public T ShowSceneUI<T>(string name = null) where T : UI_Scene
    {
        if (string.IsNullOrEmpty(name))
            name = typeof(T).Name;

        GameObject go = Managers.Resource.Instantiate(name, parent: UIRoot);
        T sceneUI = go.GetOrAddComponent<T>();
        CurrentSceneUI = sceneUI;

        SetCanvas(go, RenderMode.ScreenSpaceCamera, sort: false);


        return sceneUI;
    }
    #endregion

    #region PopupUI
    public T ShowPopupUI<T>(string name = null) where T : UI_Popup
    {
        if (string.IsNullOrEmpty(name))
            name = typeof(T).Name;

        T popup = InstanceAndSetPopUpUI<T>(name, out bool isAlreadyOn);

        // 켜져 있는 Popup이면 Setting을 하지 않음
        if (isAlreadyOn == false)
        {
            SetCanvas(popup.gameObject, RenderMode.ScreenSpaceCamera, sort: true);
            popup.gameObject.SetActive(true);
            _popupStack.Push(popup);
        }

        return popup;
    }

    public void CacheAllPopups() // UI_Popup class를 상속 받고 있는 모든 class를 가져와 캐싱해두겠다
    {
        var popups = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsSubclassOf(typeof(UI_Popup)));

        foreach (Type type in popups)
        {
            string name = type.Name;

            UI_Popup popup = InstanceAndSetPopUpUI<UI_Popup>(name, out bool isAlreadyOn);

            popup.gameObject.SetActive(false);
        }
    }

    private T InstanceAndSetPopUpUI<T>(string name, out bool isAlreadyOn) where T : UI_Popup
    {
        isAlreadyOn = false;

        // 이미 캐싱된 Popup이면서 켜진 Popup의 경우 맞바로 반환
        if (_popups.TryGetValue(name, out UI_Popup existing) && existing != null)
        {
            isAlreadyOn = existing.IsValid();
            return (T)existing;
        }

        // 캐싱되지 않은 Popup의 경우 생성하여 캐싱
        GameObject go = Managers.Resource.Instantiate(name, parent: UIRoot);
        T popup = Util.GetOrAddComponent<T>(go);

        _popups[name] = popup;
        return popup;
    }

    public void ClosePopupUI()
    {
        if (_popupStack.Count == 0) return;

        UI_Popup popup = _popupStack.Pop();

        popup.gameObject.SetActive(false);

        _order--;

        // RefreshTimeScale();
    }

    public void CloseAllPopupUI()
    {
        while (_popupStack.Count > 0)
            ClosePopupUI();
    }
    #endregion

    #region SubUI
    public T MakeSubItem<T>(Transform parent = null, string key = null, bool pooling = true) where T : UI_Base
    {
        if (string.IsNullOrEmpty(key))
            key = typeof(T).Name;

        if (parent == null)
            parent = UIRoot;

        GameObject go = Managers.Resource.Instantiate(key, parent);

        return Util.GetOrAddComponent<T>(go);
    }
    #endregion

    #region WorldUI
    public T MakeWorldSpaceUI<T>(Transform parent = null, string key = null) where T : UI_Base
    {
        if (string.IsNullOrEmpty(key))
            key = typeof(T).Name;

        GameObject go = Managers.Resource.Instantiate(key, parent);

        Canvas canvas = go.GetOrAddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;

        return Util.GetOrAddComponent<T>(go);
    }
    #endregion

    public void Clear()
    {
        CloseAllPopupUI();
        CurrentSceneUI = null;
        _popups.Clear();
        _popupStack.Clear();
        _uiRoot = null;
        _order = 10; // 오더 초기화
    }
}
