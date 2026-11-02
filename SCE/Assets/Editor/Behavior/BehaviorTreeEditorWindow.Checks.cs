using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 검사 목록 (그래프 아래) — 고른 트리의 검사 줄 (BehaviorTreeChecks — 게임과 같은 컴파일 · 변형 검사 + 편집기 검사, 창이 더하는 Addressable 등록 · 쓰는 몬스터 경고).
/// 줄을 누르면 그 카드 · 줄 · 트리 설정 · 키 · 변형으로 가서 그 칸을 보인다.
/// 불러온 뒤 · Undo 뒤 · 트리 Id를 바꾼 뒤 · 저장 뒤에는 모든 사본을, 그 밖의 편집 뒤에는 고른 사본만 검사한다 (다른 사본의 결과가 기대는 것은 트리 Id · 파일 · 등록이다).
/// 편집 뒤의 검사는 마지막 바꾸기 뒤 잠깐 (CheckDelayMs) 기다려 한 번 — 미저장 판정 · 목록 · 머리도 그때 맞춘다 (ScheduleChecks).
/// 결과는 사본의 에러 · 경고 수 (목록 ✕ · ⚠), 카드 · 줄 표시, 패널 검사 줄.
/// </summary>
public sealed partial class BehaviorTreeEditorWindow
{
    private const long CheckDelayMs = 150; // 연속 편집 중에는 마지막 바꾸기 뒤 한 번만 검사 (Interaction Editor와 같은 값)

    private readonly List<BehaviorTreeCheckItem> _checkItems = new List<BehaviorTreeCheckItem>(); // 고른 트리의 검사 줄
    private readonly List<BehaviorTreeCheckItem> _scratchItems = new List<BehaviorTreeCheckItem>();
    private ListView _checkList;
    private Label _checkSummary;
    private IVisualElementScheduledItem _checkJob;
    private bool _checkAllPending; // 다음 검사에 모든 사본 — Undo 뒤

    private VisualElement BuildCheckPane()
    {
        var pane = new VisualElement();
        pane.style.minHeight = 60;
        VisualElement header = EditorPaneUI.HeaderRow("검사", out _checkSummary);
        header.Add(BuildFixRegistrationsButton());
        pane.Add(header);
        _checkList = new ListView
        {
            itemsSource = _checkItems,
            fixedItemHeight = 20,
            selectionType = SelectionType.Single,
            makeItem = MakeCheckRow,
            bindItem = BindCheckRow,
        };
        _checkList.selectionChanged += OnCheckSelectionChanged;
        _checkList.style.flexGrow = 1;
        pane.Add(_checkList);
        return pane;
    }

    /// <summary>검사를 잠깐 뒤 한 번 — 그 사이의 바꾸기는 하나로 (마지막 바꾸기 뒤 CheckDelayMs). all이면 모든 사본 (Undo는 고르지 않은 사본도 바꾼다), 아니면 고른 사본만</summary>
    private void ScheduleChecks(bool all = false)
    {
        _checkAllPending |= all;
        _checkJob?.ExecuteLater(CheckDelayMs);
    }

    /// <summary>모든 사본을 지금 검사한다 — 불러온 뒤</summary>
    private void RunAllChecksNow()
    {
        _checkAllPending = true;
        RunChecksNow();
    }

    /// <summary>
    /// 검사한다 — 고른 사본, 모든 사본을 기다리면 (ScheduleChecks(true)) 나머지도. 고른 사본의 미저장 판정도 다시 하고, 목록 · 머리 · 검사 줄 (목록 · 그래프 · 패널) ·
    /// [Addressable 맞추기 (n)] · 파일 없는 트리 수 · 닫을 때 묻기를 맞춘다. O(검사한 트리의 노드 수 + 트리 수²)
    /// </summary>
    private void RunChecksNow()
    {
        _checkJob?.Pause();
        bool all = _checkAllPending;
        _checkAllPending = false;
        _selected?.RefreshDirty();
        UpdateUnsavedFlag(); // 판정이 그대로여도 — 불러올 때 채운 이름표뿐이던 트리에 편집이 더해지면 IsDirty는 그대로 참이다
        _checkItems.Clear();
        for (int i = 0; i < _documents.Count; i++)
        {
            BehaviorTreeDocumentAsset document = _documents[i];
            if (document == _selected)
                CheckInto(document, _checkItems);
            else if (all)
                CheckInto(document, _scratchItems);
        }
        _treeList?.RefreshItems();
        UpdateTreeTitle();
        CountFixableRegistrations();
        UpdateMissingLabel();
        ShowCheckItems();
    }

    /// <summary>고른 사본만 지금 검사해 보인다 — 트리를 고른 뒤 (고른 사본이 없으면 비운다)</summary>
    private void ShowChecksOfSelected()
    {
        _checkItems.Clear();
        if (_selected != null)
            CheckInto(_selected, _checkItems);
        ShowCheckItems();
    }

    /// <summary>사본 하나를 검사해 줄을 items에 (편집기 검사 + Addressable 등록 · 쓰는 몬스터 경고), 에러 · 경고 수를 사본에 (목록 ✕ · ⚠ · 저장 거부)</summary>
    private void CheckInto(BehaviorTreeDocumentAsset document, List<BehaviorTreeCheckItem> items)
    {
        BehaviorTreeChecks.Run(document, _documents, items);
        AddRegistrationItems(document, items);
        AddUserItems(document, items);
        BehaviorTreeChecks.Count(items, out int errors, out int warnings);
        document.ErrorCount = errors;
        document.WarningCount = warnings;
    }

    /// <summary>고른 사본의 검사 줄 (_checkItems)을 목록 · 그래프 · 패널에 보인다</summary>
    private void ShowCheckItems()
    {
        BehaviorTreeChecks.Count(_checkItems, out int errors, out int warnings);
        if (_checkSummary != null)
        {
            _checkSummary.text = (_selected == null ? "검사" : errors + warnings == 0 ? "검사 — 문제 없음" : $"검사 — 에러 {errors} · 경고 {warnings}") + AddressNotice();
            _checkSummary.style.color = errors > 0 ? EditorStatusColors.Error : StyleKeyword.Null;
        }
        _checkList?.RefreshItems();
        _graph?.SetIssues(_checkItems);
        ShowPanelIssues();
    }

    private static VisualElement MakeCheckRow()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.paddingLeft = 4;
        var mark = new Label { name = "mark" };
        mark.style.width = 16;
        mark.style.unityFontStyleAndWeight = FontStyle.Bold;
        row.Add(mark);
        var place = new Label { name = "place" };
        place.style.minWidth = 160;
        place.style.maxWidth = 240;
        place.style.overflow = Overflow.Hidden;
        place.style.opacity = 0.75f;
        row.Add(place);
        var text = new Label { name = "text" };
        text.style.flexGrow = 1;
        text.style.overflow = Overflow.Hidden;
        row.Add(text);
        return row;
    }

    private void BindCheckRow(VisualElement row, int index)
    {
        BehaviorTreeCheckItem item = _checkItems[index];
        Label mark = row.Q<Label>("mark");
        mark.text = item.IsError ? "✕" : "⚠";
        mark.style.color = item.IsError ? BehaviorGraphStyle.ErrorMark : BehaviorGraphStyle.WarningMark;
        row.Q<Label>("place").text = PlaceText(item);
        string text = item.Field.Length > 0 ? item.Field + ": " + item.Text : item.Text;
        row.Q<Label>("text").text = text;
        row.tooltip = PlaceText(item) + "\n" + text;
    }

    /// <summary>검사 줄의 자리 글 — 노드 "표시 이름 (이름표)" · "Blackboard [i] 이름" · "변형 [i] 이름" · "트리 설정" · "트리"</summary>
    private string PlaceText(BehaviorTreeCheckItem item)
    {
        switch (item.Place)
        {
            case EBehaviorCheckPlace.Node:
                if (item.Node == null)
                    return "노드";
                string name = _selected != null && _selected.Registry.TryGetInfo(item.Node.GetType(), out BehaviorNodeRegistry.NodeInfo info) ? info.DisplayName : item.Node.GetType().Name;
                return $"{name} ({item.Node.NodeId})";
            case EBehaviorCheckPlace.Blackboard:
                BehaviorBlackboardKey[] keys = _selected?.Tree.Keys;
                string keyName = keys != null && item.KeyIndex >= 0 && item.KeyIndex < keys.Length && keys[item.KeyIndex] != null ? " " + keys[item.KeyIndex].Name : "";
                return item.KeyIndex >= 0 ? $"Blackboard [{item.KeyIndex}]{keyName}" : "Blackboard";
            case EBehaviorCheckPlace.Variant:
                BehaviorTreeVariant variant = _selected != null ? BehaviorVariantEditing.At(_selected.Tree, item.VariantIndex) : null;
                return item.VariantIndex >= 0 ? $"변형 [{item.VariantIndex}]{(variant != null ? " " + variant.Name : "")}" : "변형";
            case EBehaviorCheckPlace.Settings:
                return "트리 설정";
            default:
                return "트리";
        }
    }

    /// <summary>검사 줄을 골랐다 — 그 자리로 가서 칸을 보인다 (같은 줄을 다시 누를 수 있게 고르기는 바로 푼다)</summary>
    private void OnCheckSelectionChanged(IEnumerable<object> selection)
    {
        foreach (object chosen in selection)
        {
            if (chosen is BehaviorTreeCheckItem item)
                GoTo(item);
            break;
        }
        _checkList.SetSelectionWithoutNotify(Array.Empty<int>());
    }

    /// <summary>
    /// 검사 줄의 자리로 — 노드는 그 카드 · 줄을 고르고 세부 설정에서 그 칸, 트리 · 설정은 ROOT 카드 (트리 설정), Blackboard는 그 키, 변형은 그 변형을 골라 그 칸 (이름 · 키 줄)
    /// (칸 · 키가 없으면 패널 맨 위)
    /// </summary>
    private void GoTo(BehaviorTreeCheckItem item)
    {
        if (_selected == null)
            return;
        switch (item.Place)
        {
            case EBehaviorCheckPlace.Node:
                if (item.Node == null || _graph.SelectNode(item.Node) == false)
                    return;
                _panelTab = EPanelTab.Details;
                RequestReveal(item.Field, -1);
                break;
            case EBehaviorCheckPlace.Blackboard:
                _panelTab = EPanelTab.Blackboard;
                RequestReveal(null, item.KeyIndex);
                break;
            case EBehaviorCheckPlace.Variant:
                ChooseVariant(item.VariantIndex);
                _panelTab = EPanelTab.Variants;
                RequestReveal(item.Field, -1);
                break;
            default:
                _graph.SelectRoot();
                _panelTab = EPanelTab.Details;
                RequestReveal(item.Field, -1);
                break;
        }
        ShowTabState();
        SchedulePanel();
    }
}