using System;
using System.Collections.Generic;
using Data;
using UnityEngine;

/// <summary>
/// Stage 선택 UI — GameScene 전체를 덮는다.
/// 책임: Stage 목록을 불러와 행 수만큼 버튼을 만들고, 고른 Stage의 정보를 넘긴다 (트리거).
/// Stage 실행에는 관여하지 않으며, 스스로 닫히지도 않는다 — 열고 닫는 것은 StageFlowPresenter가 상태를 보고 한다.
/// 프리팹 규약: 하위에 Content(버튼이 놓일 부모).
/// </summary>
public class UI_StageSelect : UI_Scene
{
    /// <summary>다른 Scene UI(0)보다 위, Popup(10 이상)보다 아래.</summary>
    public const int SORT_ORDER = 5;

    private enum GameObjects
    {
        Content,
    }

    private readonly StageCatalog _catalog = new StageCatalog();
    private readonly List<UI_StageSelectItem> _items = new();
    private readonly UIFocusNavigator _navigator = new UIFocusNavigator();

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        BindObjects(typeof(GameObjects));
        return true;
    }

    /// <summary>목록을 불러와 버튼을 만든다. 버튼 수 = 목록의 행 수.</summary>
    /// <param name="onSelected">Stage를 골랐을 때. 고른 Stage의 정보를 넘긴다.</param>
    public void SetInfo(Action<StageInfoData> onSelected)
    {
        Init();

        if (_catalog.Load() == false)
            LogPrinter.LogError("[UI_StageSelect] Stage 목록을 불러오지 못함 — 버튼이 없음");

        ClearItems();

        Transform content = GetObject(GameObjects.Content).transform;
        IReadOnlyList<StageInfoData> stages = _catalog.Stages;
        for (int i = 0; i < stages.Count; i++)
        {
            UI_StageSelectItem item = Managers.UI.MakeSubItem<UI_StageSelectItem>(content);
            item.SetInfo(stages[i], onSelected);
            _items.Add(item);
        }
    }

    public void Show()
    {
        gameObject.SetActive(true);
        _navigator.Open(_items);
    }

    public void Hide()
    {
        _navigator.Close();
        gameObject.SetActive(false);
    }

    private void ClearItems()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i] != null)
                Managers.Resource.Destroy(_items[i].gameObject);
        }
        _items.Clear();
    }

    // 밖에서 비활성되거나 씬이 정리될 때도 입력 오버레이가 남지 않도록
    private void OnDisable()
    {
        _navigator.Close();
    }
}