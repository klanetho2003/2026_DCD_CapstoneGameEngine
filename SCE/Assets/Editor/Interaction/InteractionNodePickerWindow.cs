using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 검색 가능한 노드 선택 드롭다운. GenericMenu는 검색이 안 되고 설명을 못 보여준다.
/// </summary>
public sealed class InteractionNodePickerWindow : EditorWindow
{
    private static Action<InteractionNodeRegistry.NodeInfo> s_onPick;
    private static bool s_isCondition;

    private readonly List<InteractionNodeRegistry.NodeInfo> _filtered = new();
    private ListView _list;
    private string _search = "";

    public static void Open(Rect activatorScreenRect, bool isCondition, Action<InteractionNodeRegistry.NodeInfo> onPick)
    {
        s_onPick = onPick;
        s_isCondition = isCondition;

        var window = CreateInstance<InteractionNodePickerWindow>();
        window.ShowAsDropDown(activatorScreenRect, new Vector2(Mathf.Max(340f, activatorScreenRect.width), 300f));
    }

    private void CreateGUI()
    {
        var search = new ToolbarSearchField();
        search.RegisterValueChangedCallback(e => { _search = e.newValue ?? ""; Refresh(); });
        rootVisualElement.Add(search);

        _list = new ListView
        {
            fixedItemHeight = 38,
            itemsSource = _filtered,
            selectionType = SelectionType.Single,
        };
        _list.makeItem = () =>
        {
            var box = new VisualElement();
            box.style.paddingLeft = 6;
            box.style.paddingTop = 2;
            box.Add(new Label { name = "title" });
            var desc = new Label { name = "desc" };
            desc.style.opacity = 0.55f;
            desc.style.fontSize = 10;
            desc.style.whiteSpace = WhiteSpace.NoWrap;
            box.Add(desc);
            return box;
        };
        _list.bindItem = (element, i) =>
        {
            InteractionNodeRegistry.NodeInfo info = _filtered[i];
            element.Q<Label>("title").text = $"{info.DisplayName}  ({info.Key})";
            element.Q<Label>("desc").text = info.Description;
        };
        _list.selectionChanged += _ => Pick();
        _list.style.flexGrow = 1;
        rootVisualElement.Add(_list);

        Refresh();
        rootVisualElement.schedule.Execute(() => search.Q("unity-text-input")?.Focus());
    }

    private void Refresh()
    {
        _filtered.Clear();
        var source = s_isCondition ? InteractionNodeRegistry.ConditionInfos : InteractionNodeRegistry.EffectInfos;
        for (int i = 0; i < source.Count; i++)
        {
            InteractionNodeRegistry.NodeInfo info = source[i];
            if (_search.Length == 0
                || info.DisplayName.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                || info.Key.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                || info.Description.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _filtered.Add(info);
            }
        }
        _list?.RefreshItems();
    }

    private void Pick()
    {
        int index = _list.selectedIndex;
        if (index < 0 || index >= _filtered.Count) return;

        Action<InteractionNodeRegistry.NodeInfo> callback = s_onPick;
        InteractionNodeRegistry.NodeInfo picked = _filtered[index];
        s_onPick = null;
        Close();
        callback?.Invoke(picked); // Close 후 호출 — 콜백이 창을 다시 띄워도 안전
    }

    private void OnDisable()
    {
        s_onPick = null;
    }
}