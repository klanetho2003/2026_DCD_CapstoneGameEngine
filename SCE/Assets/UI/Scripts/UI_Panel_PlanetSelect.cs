using System;
using System.Collections.Generic;
using Data;
using UnityEngine;
using UnityEngine.UI;
using static Define;

/// <summary>
/// 행성 선택 = Stage 선택 화면. UI_StageSelect 와 같은 사용법:
///   var ui = Managers.UI.ShowBaseUI&lt;UI_Panel_PlanetSelect&gt;(sortOrder: UI_Panel_PlanetSelect.SORT_ORDER);
///   ui.SetInfo(onStageSelected);  ui.Show() / ui.Hide()
/// 행성 순서(수성, 금성, 지구, 화성) = StageCatalog 목록 순서.
/// 목록에 없는 행성과 목성·토성은 잠김(클릭 안 됨).
/// 스스로 닫히지 않는다 — 열고 닫기는 StageFlowPresenter 가 한다.
/// </summary>
public class UI_Panel_PlanetSelect : UI_Scene
{
    /// <summary>다른 Scene UI(0)보다 위, Popup(10 이상)보다 아래.</summary>
    public const int SORT_ORDER = 5;

    private enum GameObjects
    {
        Btn_Back,
        Btn_Land,
        Btn_Planet_Mercury,
        Btn_Planet_Venus,
        Btn_Planet_Earth,
        Btn_Planet_Mars,
    }

    private static readonly GameObjects[] Planets =
    {
        GameObjects.Btn_Planet_Mercury,
        GameObjects.Btn_Planet_Venus,
        GameObjects.Btn_Planet_Earth,
        GameObjects.Btn_Planet_Mars,
    };

    private readonly StageCatalog _catalog = new StageCatalog();
    private Action<StageInfoData> _onSelected;
    private Action _onBack;
    private int _selected = -1;

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        BindObjects(typeof(GameObjects));

        GameObject land = GetObject(GameObjects.Btn_Land);
        if (land != null) BindEvent(land, (evt) => OnClickLand());

        GameObject back = GetObject(GameObjects.Btn_Back);
        if (back != null) BindEvent(back, (evt) => _onBack?.Invoke());

        for (int i = 0; i < Planets.Length; i++)
        {
            int index = i;
            GameObject planet = GetObject(Planets[i]);
            if (planet != null)
                BindEvent(planet, (evt) => Select(index));
        }

        return true;
    }

    /// <param name="onSelected">착륙을 눌렀을 때. 고른 Stage 정보를 넘긴다.</param>
    /// <param name="onBack">Back 을 눌렀을 때 (없으면 아무 일 없음).</param>
    public void SetInfo(Action<StageInfoData> onSelected, Action onBack = null)
    {
        Init();
        _onSelected = onSelected;
        _onBack = onBack;

        if (_catalog.Load() == false)
            LogPrinter.LogError("[UI_Panel_PlanetSelect] Stage 목록을 불러오지 못함");

        Select(-1);
    }

    public void Show()
    {
        gameObject.SetActive(true);
        Select(-1);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private int StageCount
    {
        get { return _catalog.Stages == null ? 0 : _catalog.Stages.Count; }
    }

    private void Select(int index)
    {
        if (index >= StageCount)
            return; // 목록에 없는 행성 = 잠김

        _selected = index;

        // 선택된 행성의 링(Img_Ring)만 켜기
        for (int i = 0; i < Planets.Length; i++)
        {
            GameObject ring = UIPanelInput.FindDeep(GetObject(Planets[i]), "Img_Ring");
            if (ring != null)
                ring.SetActive(i == _selected);
        }

        // 행성을 안 골랐으면 착륙 버튼 비활성
        GameObject land = GetObject(GameObjects.Btn_Land);
        if (land != null && land.TryGetComponent(out Button button))
            button.interactable = _selected >= 0;
    }

    private void OnClickLand()
    {
        if (_selected < 0 || _selected >= StageCount)
            return;

        _onSelected?.Invoke(_catalog.Stages[_selected]);
    }
}
