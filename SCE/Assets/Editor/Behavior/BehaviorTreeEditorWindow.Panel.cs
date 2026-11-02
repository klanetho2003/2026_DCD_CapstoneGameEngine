using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 오른쪽 패널 — [세부 설정] (고른 노드 · ROOT 카드면 트리 설정, BehaviorNodeInspector) · [Blackboard] (키 목록, BehaviorBlackboardView) · [변형] (BehaviorVariantView — .Variants.cs).
/// 패널은 다음 갱신에 한 번 다시 만든다 (SchedulePanel) — 고르기 · 구조 · Undo · 탭이 바뀔 때. 클릭으로 시작된 바꾸기도 입력 중인 칸이 먼저 확정된 뒤에 지운다.
/// 다시 만든 패널이 배치된 뒤 (DeferMs) 스크롤을 맞춘다 — 보일 칸이 있으면 그 칸으로 (검사 줄 · 키 편집), 같은 대상을 다시 만들었으면 보던 자리로, 다른 대상이면 맨 위.
/// 칸 값이 바뀌면 (값 추적) 패널은 그대로 두고 카드 글 · 검사만 맞춘다 (AfterPanelEdit — 노드 칸의 잠금 · enum 목록이 바뀐 때만 다시 만든다). 이름표 · 메모 · 키 · 트리 Id 편집은 Undo를 남기고 사본을 직접 바꾼다 —
/// 패널을 만든 트리가 아니면 (그 사이 다른 트리를 고름) 하지 않는다.
/// </summary>
public sealed partial class BehaviorTreeEditorWindow
{
    private enum EPanelTab
    {
        Details,
        Blackboard,
        Variants,
    }

    private const long DeferMs = 50; // 다시 만든 패널이 배치될 때까지 기다리는 시간 — 그 뒤에 스크롤 (다른 편집기 창과 같은 값)

    [SerializeField] private EPanelTab _panelTab = EPanelTab.Details; // 고른 탭 — 창을 다시 만들어도 그대로

    private ToolbarToggle _detailsTab;
    private ToolbarToggle _blackboardTab;
    private ToolbarToggle _variantsTab;
    private ScrollView _panelScroll;
    private VisualElement _panelContent;
    private IVisualElementScheduledItem _panelJob;
    private IVisualElementScheduledItem _panelScrollJob;
    private string _panelTarget = "";   // 지금 패널이 보이는 대상 (PanelTarget) — 같은 대상을 다시 만들면 보던 자리를 지킨다
    private Vector2? _keepScrollOffset;  // 다시 만든 뒤 맞출 보던 자리 (같은 대상일 때)
    private bool _revealPending;         // 다시 만든 뒤 보일 칸이 있다 — 검사 줄 · 키 편집
    private string _revealField = "";    // 세부 설정의 칸 (JSON 키) — 없는 칸이면 맨 위
    private int _revealKey = -1;         // Blackboard 키 차례 — 없는 키면 맨 위
    private string _panelFieldStates;    // 세부 설정이 노드 칸일 때 만든 때의 칸 잠금 · enum 목록 (BehaviorNodeInspector.FieldStateKey) — 노드 칸이 아니면 null

    private VisualElement BuildPanePanel()
    {
        var pane = new VisualElement();
        pane.style.minWidth = 260;
        var bar = new Toolbar();
        _detailsTab = new ToolbarToggle { text = "세부 설정", tooltip = "고른 카드 · Decorator · Service 줄의 칸 (ROOT 카드면 트리 설정)" };
        _blackboardTab = new ToolbarToggle { text = "Blackboard", tooltip = "트리의 키 목록 — 노드가 값을 읽고 쓰는 칸" };
        _variantsTab = new ToolbarToggle { text = "변형", tooltip = "이 트리의 노드 · 설정을 그대로 쓰고 Blackboard 키 기본값만 바꾼 변형 — 몬스터 데이터의 트리 Id '트리 Id/변형 이름'" };
        _detailsTab.RegisterValueChangedCallback(evt => OnTabToggled(EPanelTab.Details, evt.newValue));
        _blackboardTab.RegisterValueChangedCallback(evt => OnTabToggled(EPanelTab.Blackboard, evt.newValue));
        _variantsTab.RegisterValueChangedCallback(evt => OnTabToggled(EPanelTab.Variants, evt.newValue));
        bar.Add(_detailsTab);
        bar.Add(_blackboardTab);
        bar.Add(_variantsTab);
        pane.Add(bar);

        _panelScroll = new ScrollView(ScrollViewMode.Vertical);
        _panelScroll.style.flexGrow = 1;
        pane.Add(_panelScroll);
        ShowTabState();
        return pane;
    }

    private void CreateJobs()
    {
        _panelJob = rootVisualElement.schedule.Execute(RebuildPanelNow);
        _panelJob.Pause();
        _panelScrollJob = rootVisualElement.schedule.Execute(ApplyPanelScroll);
        _panelScrollJob.Pause();
        _checkJob = rootVisualElement.schedule.Execute(RunChecksNow);
        _checkJob.Pause();
    }

    private void OnTabToggled(EPanelTab tab, bool on)
    {
        if (on)
            _panelTab = tab;
        ShowTabState(); // 누른 탭은 켠 채로 (끄기로는 바꾸지 않는다)
        SchedulePanel();
    }

    private void ShowTabState()
    {
        _detailsTab?.SetValueWithoutNotify(_panelTab == EPanelTab.Details);
        _blackboardTab?.SetValueWithoutNotify(_panelTab == EPanelTab.Blackboard);
        _variantsTab?.SetValueWithoutNotify(_panelTab == EPanelTab.Variants);
    }

    /// <summary>그래프의 고르기가 바뀌었다 (다음 갱신에 한 번 알림) — 세부 설정을 다시 만든다</summary>
    private void OnGraphSelectionChanged()
    {
        if (_panelTab == EPanelTab.Details)
            SchedulePanel();
    }

    /// <summary>패널을 다음 갱신에 한 번 다시 만든다 — 같은 갱신 안의 여러 요청은 하나로</summary>
    private void SchedulePanel()
    {
        _panelJob?.ExecuteLater(0);
    }

    /// <summary>다시 만든 패널에서 그 칸 (세부 설정 — JSON 키, 변형 — 이름 · 키 이름) 또는 그 키 (Blackboard — 차례)를 보인다. 없는 칸 · 키면 맨 위</summary>
    private void RequestReveal(string field, int keyIndex)
    {
        _revealPending = true;
        _revealField = field ?? "";
        _revealKey = keyIndex;
    }

    private void ClearReveal()
    {
        _revealPending = false;
        _revealField = "";
        _revealKey = -1;
    }

    /// <summary>
    /// 패널을 지금 다시 만든다 — 사본 · 고른 탭 · 그래프의 고르기로. 칸 값 추적 (TrackSerializedObjectValue)은 새 내용 상자에 (등록이 쌓이지 않게).
    /// 스크롤은 배치된 뒤에 맞춘다 (ApplyPanelScroll) — 여러 번 다시 만들어도 마지막 것 뒤에 한 번
    /// </summary>
    private void RebuildPanelNow()
    {
        _panelJob?.Pause();
        if (_panelScroll == null)
            return;
        string target = PanelTarget();
        if (target != _panelTarget)
            _keepScrollOffset = null;
        else if (_keepScrollOffset.HasValue == false)
            _keepScrollOffset = _panelScroll.scrollOffset; // 앞의 다시 만들기가 맞추기 전이면 그때의 보던 자리 그대로
        _panelTarget = target;

        _panelContent?.Unbind();
        _panelScroll.Clear();
        _panelContent = null;
        _panelFieldStates = null;
        if (_selected == null || _selectedObject == null)
        {
            _panelScroll.Add(EditorPaneUI.Hint("왼쪽에서 트리를 고르세요"));
            return;
        }

        _selectedObject.Update();
        VisualElement content = _panelTab == EPanelTab.Blackboard ? BuildBlackboardContent() : _panelTab == EPanelTab.Variants ? BuildVariantsContent() : BuildDetailsContent();
        content.TrackSerializedObjectValue(_selectedObject, _ => AfterPanelEdit());
        _panelContent = content;
        _panelScroll.Add(content);
        ShowPanelIssues();
        if (_keepScrollOffset.HasValue == false)
            _panelScroll.scrollOffset = Vector2.zero; // 다른 대상 — 맨 위에서
        _panelScrollJob?.ExecuteLater(DeferMs);
    }

    /// <summary>패널이 보이는 대상 — 트리 파일 · 탭 · (세부 설정이면) 고른 노드의 이름표 또는 ROOT · (변형이면) 고른 변형 이름</summary>
    private string PanelTarget()
    {
        if (_selected == null)
            return "";
        string target = _selected.FilePath + "|" + _panelTab;
        if (_panelTab == EPanelTab.Details && _graph != null && _graph.TryGetSelected(out BehaviorNode node, out bool isRoot))
            target += isRoot ? "|ROOT" : "|" + node.NodeId;
        else if (_panelTab == EPanelTab.Variants)
            target += "|" + _variantName;
        return target;
    }

    /// <summary>다시 만든 패널이 배치된 뒤 — 보일 칸이 있으면 그 칸으로 (없는 칸이면 맨 위), 아니면 같은 대상의 보던 자리로</summary>
    private void ApplyPanelScroll()
    {
        if (_panelContent != null && _panelScroll != null)
        {
            if (_revealPending)
            {
                bool shown = _panelTab == EPanelTab.Blackboard
                    ? BehaviorBlackboardView.Reveal(_panelContent, _revealKey)
                    : _panelTab == EPanelTab.Variants
                        ? BehaviorVariantView.Reveal(_panelContent, _revealField)
                        : BehaviorNodeInspector.Reveal(_panelContent, _revealField);
                if (shown == false)
                    _panelScroll.scrollOffset = Vector2.zero;
            }
            else if (_keepScrollOffset.HasValue)
            {
                _panelScroll.scrollOffset = _keepScrollOffset.Value;
            }
        }
        ClearReveal();
        _keepScrollOffset = null;
    }

    private VisualElement BuildDetailsContent()
    {
        if (_graph.TryGetSelected(out BehaviorNode node, out bool isRoot) == false)
        {
            int count = _graph.SelectedCount;
            return EditorPaneUI.Hint(count > 1
                ? $"{count}개를 골랐습니다 — 카드 하나 · Decorator · Service 줄 하나를 고르면 칸이 보입니다"
                : "카드 · Decorator · Service 줄을 고르면 칸이 보입니다 (ROOT 카드 = 트리 설정)");
        }
        BehaviorTreeDocumentAsset document = _selected;
        if (isRoot)
        {
            string users = UsersText(document, out string usersTooltip);
            return BehaviorNodeInspector.BuildTree(_selectedObject, document, new BehaviorNodeInspector.TreeInfo
            {
                RenameTree = id => RenameTree(document, id),
                FileText = FileText(document),
                UsersText = users,
                UsersTooltip = usersTooltip,
            });
        }
        _panelFieldStates = BehaviorNodeInspector.FieldStateKey(node, document.Tree.Keys);
        return BehaviorNodeInspector.Build(_selectedObject, document, node, new BehaviorNodeInspector.Callbacks
        {
            RenameNode = (target, nodeId) => RenameNode(document, target, nodeId),
            SetMemo = (target, memo) => SetMemo(document, target, memo),
            PromoteToKey = (target, field) => PromoteToKey(document, target, field),
        });
    }

    /// <summary>
    /// 세부 설정이 보이는 노드의 칸 잠금 · enum 목록 (BehaviorNodeInspector.FieldStateKey — 노드가 밝힌다)이 패널을 만든 때와 달라졌으면 다음 갱신에 다시 만든다.
    /// 잠금 · 목록을 바꾸는 칸 (Check · 키 · Abort Mode 등)은 목록 · 체크라 입력 중인 글자 칸을 지우지 않는다. O(칸 수)
    /// </summary>
    private void RebuildPanelIfFieldStatesChanged()
    {
        if (_panelFieldStates == null || _panelTab != EPanelTab.Details || _selected == null || _graph == null)
            return;
        if (_graph.TryGetSelected(out BehaviorNode node, out bool isRoot) == false || isRoot)
            return;
        if (BehaviorNodeInspector.FieldStateKey(node, _selected.Tree.Keys) != _panelFieldStates)
            SchedulePanel();
    }

    private VisualElement BuildBlackboardContent()
    {
        BehaviorTreeDocumentAsset document = _selected;
        VisualElement content = BehaviorBlackboardView.Build(_selectedObject, document, new BehaviorBlackboardView.Callbacks
        {
            AddKey = type => AddKey(document, type),
            RemoveKey = index => RemoveKey(document, index),
            MoveKey = (index, delta) => MoveKey(document, index, delta),
            RenameKey = (index, name) => RenameKey(document, index, name),
            TypeChanged = SchedulePanel,
        });
        content.Insert(0, BuildDebugKeysView()); // Play 중 디버거가 보이는 모습의 키 값 (.Debug.cs — 없으면 감춤)
        return content;
    }

    /// <summary>패널의 검사 줄 — 세부 설정은 고른 노드 (트리 설정이면 트리 · 설정 자리)의 것, Blackboard는 키의 것, 변형은 고른 변형의 것 (목록에는 변형마다 표시)</summary>
    private void ShowPanelIssues()
    {
        if (_panelContent == null || _selected == null)
            return;
        if (_panelTab == EPanelTab.Blackboard)
        {
            BehaviorBlackboardView.ShowIssues(_panelContent, _checkItems);
            return;
        }
        if (_panelTab == EPanelTab.Variants)
        {
            BehaviorVariantView.ShowIssues(_panelContent, _checkItems, SelectedVariantIndex());
            return;
        }
        if (_graph.TryGetSelected(out BehaviorNode node, out bool isRoot) == false)
            return;
        if (isRoot)
            BehaviorNodeInspector.ShowIssues(_panelContent, _checkItems, item => item.Place == EBehaviorCheckPlace.Tree || item.Place == EBehaviorCheckPlace.Settings);
        else
            BehaviorNodeInspector.ShowIssues(_panelContent, _checkItems, item => item.Place == EBehaviorCheckPlace.Node && item.Node == node);
    }

    #region 패널의 바꾸기 (사본을 직접 — Undo를 먼저 남긴다)
    private const string OtherTreeError = "다른 트리를 골라 적용하지 않았습니다";

    /// <summary>패널을 만든 트리가 지금 고른 트리인가 — 아니면 (그 사이 다른 트리를 고름) 바꾸지 않는다</summary>
    private bool IsSelectedDocument(BehaviorTreeDocumentAsset document)
    {
        return document != null && document == _selected;
    }

    /// <summary>이름표 바꾸기 — 쓸 수 없으면 바꾸지 않고 까닭 (Undo도 남기지 않는다). 카드 자리 · 메모가 따라간다</summary>
    private string RenameNode(BehaviorTreeDocumentAsset document, BehaviorNode node, string nodeId)
    {
        if (IsSelectedDocument(document) == false)
            return OtherTreeError;
        string error = BehaviorTreeEditing.NodeIdError(document, node, nodeId);
        if (error != null || string.Equals(node.NodeId, nodeId, StringComparison.Ordinal))
            return error;
        Record("Behavior Tree 이름표 바꾸기");
        BehaviorTreeEditing.RenameNode(document, node, nodeId, out _);
        _selectedObject?.Update();
        AfterPanelEdit();
        return null;
    }

    /// <summary>
    /// 트리 Id 바꾸기 — 쓸 수 없으면 (BehaviorTreeEditing.TreeIdError — 빈 · 앞뒤 공백 · 파일 이름 불가 · 다른 트리와 겹침) 바꾸지 않고 까닭 (Undo도 남기지 않는다).
    /// 다른 트리의 검사 (Id 겹침)도 바뀌므로 모든 트리를 다시 검사한다. 파일 이름 · Addressable 주소는 저장할 때 따라간다
    /// </summary>
    private string RenameTree(BehaviorTreeDocumentAsset document, string id)
    {
        if (IsSelectedDocument(document) == false)
            return OtherTreeError;
        if (string.Equals(document.Tree.Id, id, StringComparison.Ordinal))
            return null;
        string error = BehaviorTreeEditing.TreeIdError(id, TakenTreeNames(document));
        if (error != null)
            return error;
        Record("Behavior Tree Id 바꾸기");
        document.Tree.Id = id;
        _selectedObject?.Update();
        AfterPanelEdit();
        ScheduleChecks(all: true);
        SchedulePanel(); // 파일 · 쓰는 몬스터 글
        return null;
    }

    /// <summary>트리 설정의 파일 글 — 지금 파일, 저장하면 바뀔 이름 (새 트리 · 이름 바꿈 · 대소문자만)</summary>
    private static string FileText(BehaviorTreeDocumentAsset document)
    {
        JsonSavePlan plan = JsonSavePlan.Decide(Folder, document.FilePath, document.Tree.Id);
        if (plan.IsNew)
            return $"파일: 저장 전 — 저장하면 {Path.GetFileName(plan.TargetPath)} (+ 배치 파일 {BehaviorTreeLayoutFile.FolderName}/{Path.GetFileName(BehaviorTreeLayoutFile.PathFor(plan.TargetPath))})";
        if (plan.Rename || plan.CaseOnlyRename)
            return $"파일: {Path.GetFileName(document.FilePath)} — 저장하면 {Path.GetFileName(plan.TargetPath)}으로 이름을 바꿈 (배치 파일도)";
        return $"파일: {Path.GetFileName(document.FilePath)}";
    }

    /// <summary>메모 바꾸기 (빈 글이면 지움) — 같으면 아무것도 하지 않는다</summary>
    private void SetMemo(BehaviorTreeDocumentAsset document, BehaviorNode node, string memo)
    {
        if (IsSelectedDocument(document) == false)
            return;
        string current = document.TryGetMemo(node.NodeId, out string text) ? text : "";
        if (string.Equals(current, memo ?? "", StringComparison.Ordinal))
            return;
        Record("Behavior Tree 메모");
        document.SetMemo(node.NodeId, memo);
        _selectedObject?.Update();
        AfterPanelEdit();
    }

    private void AddKey(BehaviorTreeDocumentAsset document, EBehaviorKeyType type)
    {
        if (IsSelectedDocument(document) == false)
            return;
        Record("Behavior Tree 키 더하기");
        int index = BehaviorBlackboardEditing.AddKey(document.Tree, type);
        RequestReveal(null, index);
        AfterKeyEdit();
    }

    /// <summary>키 지우기 — 변형이 바꾼 그 키의 값도 지운다 (BehaviorBlackboardEditing.RemoveKey — 지웠으면 상태 줄에 수)</summary>
    private void RemoveKey(BehaviorTreeDocumentAsset document, int index)
    {
        if (IsSelectedDocument(document) == false || document.Tree.Keys == null || index < 0 || index >= document.Tree.Keys.Length)
            return;
        string name = document.Tree.Keys[index]?.Name;
        Record("Behavior Tree 키 지우기");
        BehaviorBlackboardEditing.RemoveKey(document.Tree, index, out int variantValues);
        AfterKeyEdit();
        if (variantValues > 0)
            SetStatus($"키 '{name}'를 지움 — 변형이 바꾼 그 키의 값 {variantValues}개도 지움");
    }

    private void MoveKey(BehaviorTreeDocumentAsset document, int index, int delta)
    {
        BehaviorBlackboardKey[] keys = IsSelectedDocument(document) ? document.Tree.Keys : null;
        if (keys == null || delta == 0 || index < 0 || index >= keys.Length || index + delta < 0 || index + delta >= keys.Length)
            return;
        Record("Behavior Tree 키 차례 바꾸기");
        BehaviorBlackboardEditing.MoveKey(document.Tree, index, delta);
        RequestReveal(null, index + delta);
        AfterKeyEdit();
    }

    /// <summary>키 이름 바꾸기 — 쓸 수 없으면 바꾸지 않고 까닭 (Undo도 남기지 않는다). 노드가 쓰는 그 이름 · 변형이 바꾼 그 키의 값도 함께 (BehaviorBlackboardEditing.RenameKey)</summary>
    private string RenameKey(BehaviorTreeDocumentAsset document, int index, string name)
    {
        if (IsSelectedDocument(document) == false)
            return OtherTreeError;
        BehaviorBlackboardKey[] keys = document.Tree.Keys;
        if (keys == null || index < 0 || index >= keys.Length || keys[index] == null)
            return "키를 찾을 수 없습니다";
        if (string.Equals(keys[index].Name, name, StringComparison.Ordinal))
            return null;
        string error = BehaviorBlackboardEditing.RenameError(document.Tree, index, name);
        if (error != null)
            return error;
        Record("Behavior Tree 키 이름 바꾸기");
        int renamed = BehaviorBlackboardEditing.RenameKey(document, index, name, out _, out int variantValues);
        RequestReveal(null, index);
        AfterKeyEdit();
        var parts = new List<string>();
        if (renamed > 0)
            parts.Add($"노드 칸 {renamed}개");
        if (variantValues > 0)
            parts.Add($"변형 값 {variantValues}개");
        SetStatus(parts.Count > 0 ? $"키 이름을 '{name}'으로 — {string.Join(" · ", parts)}도 함께 바꿈" : $"키 이름을 '{name}'으로");
        return null;
    }

    /// <summary>
    /// 값 또는 키 칸을 키로 빼기 — 그 값을 기본값으로 하는 새 키를 만들어 칸이 쓰게 한다 (BehaviorBlackboardEditing.PromoteToKey). 막히면 바꾸지 않고 까닭 (Undo도 남기지 않는다).
    /// 검사가 그 노드의 값을 본 때만 뺀다 — 키 값에는 노드의 값 검사 (0 이상 · 다른 칸과의 관계 등)를 하지 않아 빼면 에러가 가려진다:
    /// 트리에 이어지지 않은 노드 (컴파일 검사 밖) · 노드의 검사 에러 · 트리 자리의 에러 (저장할 수 없는 값 등 — 컴파일 검사를 건너뜀)면 막는다. 키 목록이 바뀌어 패널을 다시 만든다
    /// </summary>
    private string PromoteToKey(BehaviorTreeDocumentAsset document, BehaviorNode node, string field)
    {
        if (IsSelectedDocument(document) == false)
            return OtherTreeError;
        if (node == null || BehaviorNodeFields.TryGetField(node.GetType(), field, out BehaviorNodeFields.Field info) == false
            || BehaviorNodeFields.ValueKeyType(info.Info.FieldType) == 0 || string.IsNullOrEmpty(BehaviorNodeFields.GetKeyName(node, info.Info)) == false)
            return "값을 쓰는 값 또는 키 칸이 아닙니다";
        BehaviorMainNode main = node as BehaviorMainNode;
        if (main == null && node is BehaviorAuxiliaryNode auxiliary)
            BehaviorTreeEditing.TryGetOwner(document, auxiliary, out main);
        if (main == null || BehaviorTreeEditing.IsInTree(document, main) == false)
            return "트리에 이어지지 않은 노드는 값 검사를 하지 않아 키로 뺄 수 없습니다 — 먼저 이을 것";
        RunChecksNow(); // 지금 값의 검사 — 미룬 검사가 남았을 수 있다
        if (_checkItems.Exists(item => item.IsError && ((item.Place == EBehaviorCheckPlace.Node && ReferenceEquals(item.Node, node)) || item.Place == EBehaviorCheckPlace.Tree)))
            return "노드 · 트리에 검사 에러가 있어 키로 뺄 수 없습니다 — 먼저 고칠 것 (키 값에는 노드의 값 검사를 하지 않아 에러가 가려진다)";
        Record("Behavior Tree 키로 빼기");
        int index = BehaviorBlackboardEditing.PromoteToKey(document.Tree, node, field, out string keyName, out string error);
        if (index < 0)
            return error;
        RequestReveal(field, -1);
        AfterKeyEdit();
        BehaviorBlackboardKey key = document.Tree.Keys[index];
        SetStatus($"{node.NodeId}.{field}의 값 {BehaviorVariantView.ValueText(key)}을 키 '{keyName}' ({key.Type})로 뺌 — 동작은 같고, [변형] 탭에서 몬스터마다 바꿀 수 있음");
        return null;
    }

    /// <summary>키 목록을 바꾼 뒤 — 칸의 SerializedObject를 맞추고, 패널 (키 줄 · 쓰는 노드)을 다시 만든다</summary>
    private void AfterKeyEdit()
    {
        _selectedObject?.Update();
        AfterPanelEdit();
        SchedulePanel();
    }
    #endregion
}