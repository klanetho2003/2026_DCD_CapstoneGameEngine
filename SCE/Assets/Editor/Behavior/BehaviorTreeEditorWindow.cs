using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Behavior Tree 시각 편집기 — Tools > Behavior Tree Editor. 도킹 가능.
/// 레이아웃: [트리 목록] | [고른 트리의 그래프 — Unreal 모양: 위에서 아래로 · 카드 안 Decorator (파랑) · Service (초록) · 실행 순서 번호 · 자식 차례 = 가로 자리]
///                        [검사 목록 (그래프 아래)]                                                                                    | [세부 설정 · Blackboard (오른쪽 탭)]
/// 트리 폴더 (Folder) 바로 아래의 JSON을 모두 읽어 편집 사본 (BehaviorTreeDocumentAsset)을 만든다 — 읽지 못한 파일은 목록에 '파싱 실패'로 둔다 (게임도 그 트리를 쓰지 못한다).
/// 그래프 바꾸기는 그래프 (BehaviorTreeGraphView)가, 칸 값은 패널 (BehaviorNodeInspector · BehaviorBlackboardView — SerializedObject)이 사본에 옮긴다.
/// 저장은 [저장] · [모두 저장] — 트리 JSON + 배치 파일 (카드 자리 · 메모 · 이어지지 않은 노드), 이름 바꾸기 · Addressable 등록 (.Save.cs · .Addressables.cs). 창을 다시 만들어도 저장하지 않은 편집은 넘어간다.
/// 목록의 `*`는 저장할 것이 있는 트리 (BehaviorTreeDocumentAsset.NeedsSave — 트리 JSON이나 배치가 파일과 다름, 불러올 때 채운 노드 이름표도 — 디버거가 Play 중 노드를 이름표로 찾는다),
/// ✕ · ⚠는 그 트리의 검사 에러 · 경고 수 (BehaviorTreeChecks + 등록 · 쓰는 몬스터).
/// Play 중에는 디버거 — 대상 몬스터의 실행 모습 · breakpoint · 한 평가씩 · Scene 뷰, 저장한 트리를 몬스터에 입힘 (.Debug.cs).
/// </summary>
public sealed partial class BehaviorTreeEditorWindow : EditorWindow
{
    /// <summary>트리 JSON 폴더 — 바로 아래 파일만 읽는다</summary>
    public const string Folder = "Assets/@Resources/Data/JsonData/BehaviorTrees";
    private const string ToolName = "Behavior Tree Editor";

    private readonly List<BehaviorTreeDocumentAsset> _documents = new List<BehaviorTreeDocumentAsset>();
    private readonly List<JsonBrokenFile> _brokenFiles = new List<JsonBrokenFile>();
    private readonly List<object> _items = new List<object>(); // 목록 항목 — 사본 또는 파싱 실패 파일

    [SerializeField] private string _selectedPath; // 고른 파일 — 창을 다시 만들면 다시 고른다
    [NonSerialized] private BehaviorTreeDocumentAsset _selected;
    [NonSerialized] private JsonBrokenFile _selectedBroken;
    [NonSerialized] private SerializedObject _selectedObject; // 고른 사본 — 패널이 칸을 묶는다

    // UI
    private Label _status;
    private ListView _treeList;
    private VisualElement _treeHeader;
    private Label _treeTitle;
    private BehaviorTreeGraphView _graph;
    private VisualElement _messageView;

    [MenuItem("Tools/Behavior Tree Editor")]
    public static void Open()
    {
        BehaviorTreeEditorWindow window = GetWindow<BehaviorTreeEditorWindow>();
        window.titleContent = new GUIContent(ToolName);
        window.minSize = new Vector2(900, 480);
    }

    #region 생명주기
    private void OnEnable()
    {
        LoadStamp(); // 담은 때 표식과 견줄 지금 표식 (Save.cs)
        Undo.undoRedoPerformed += OnUndoRedo;
        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload; // 저장하지 않은 편집을 리로드 너머로 (Save.cs)
        LoadFolder();
        RestoreUnsavedEdits(); // 창을 다시 만들기 전 (도메인 리로드 · 다른 창 최대화)에 담아 둔 편집이 있으면 다시 읽은 사본에 입힌다 — 표식이 같을 때만
        EnableDebug();
    }

    private void OnDisable()
    {
        DisableDebug();
        AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
        Undo.undoRedoPerformed -= OnUndoRedo;
        _graph?.Show(null);
        _selected = null;
        _selectedBroken = null;
        DisposeSelectedObject();
        JsonDocumentList.DestroyAll(_documents);
    }

    /// <summary>창이 포커스를 받을 때 — 닫을 때 묻기를 맞추고, 몬스터 데이터 · Addressable 등록이 다른 곳에서 바뀌었으면 다시 읽어 검사한다</summary>
    private void OnFocus()
    {
        UpdateUnsavedFlag();
        RescanUsers(false);
        RescanAddressables();
    }

    /// <summary>창이 포커스를 잃을 때 — 입력 중인 글자 칸을 확정한다</summary>
    private void OnLostFocus()
    {
        EditorTextInput.CommitFocused(rootVisualElement);
    }

    private void CreateGUI()
    {
        VisualElement root = rootVisualElement;
        root.RegisterCallback<PointerDownEvent>(OnRootPointerDown, TrickleDown.TrickleDown);
        root.Add(BuildToolbar());

        var center = new TwoPaneSplitView(1, 150, TwoPaneSplitViewOrientation.Vertical);
        center.Add(BuildGraphPane());
        center.Add(BuildCheckPane());

        var right = new TwoPaneSplitView(1, 340, TwoPaneSplitViewOrientation.Horizontal);
        right.Add(center);
        right.Add(BuildPanePanel());

        var split = new TwoPaneSplitView(0, 220, TwoPaneSplitViewOrientation.Horizontal);
        split.Add(BuildTreePane());
        split.Add(right);
        split.style.flexGrow = 1;
        root.Add(split);

        CreateJobs();
        StartDebugJob();
        SelectByPath(_selectedPath);
        SetStatus(LoadSummary() + RestoreNotice());
        RunAllChecksNow();
    }

    /// <summary>
    /// 창 안의 모든 누름이 먼저 지나는 곳 (TrickleDown) — 입력 중인 글자 칸 밖을 누르면 그 칸을 확정한다. 누른 곳이 포커스를 받지 않는 여백 · 버튼이어도.
    /// 확정은 이 누름을 처리한 뒤에 일어난다 — 목록에서 트리 고르기는 한 틱 뒤에 하고 (AfterInputCommit), 그래프 고르기 · 탭 · 검사 줄이 바꾸는 패널은 다음 갱신에 다시 만든다 (SchedulePanel).
    /// </summary>
    private void OnRootPointerDown(PointerDownEvent evt)
    {
        EditorTextInput.CommitFocused(rootVisualElement, evt.target as VisualElement);
    }

    /// <summary>
    /// 칸을 지우는 전환 (목록에서 트리 고르기)은 한 틱 뒤에 한다 — 입력 중이던 글자 칸이 먼저 확정되게 (Interaction Editor · State Definition Editor와 같은 규칙, EditorTextInput).
    /// 클릭으로 시작된 전환은 그 클릭을 처리하는 중에 불리는데, 글자 칸의 확정 (포커스 해제)은 그 처리가 끝난 뒤에 일어난다. 바로 바꾸면 확정 전 글자가 다른 트리에 적용되거나 버려진다.
    /// </summary>
    private void AfterInputCommit(Action action)
    {
        rootVisualElement.schedule.Execute(action);
    }
    #endregion

    #region 툴바 · 상태
    private VisualElement BuildToolbar()
    {
        var bar = new Toolbar();
        bar.Add(new ToolbarButton(SaveSelected) { text = "저장", tooltip = "고른 트리 저장 — 트리 JSON + 배치 파일 (Editor/{Id}.layout.json), 파일 이름 = 트리 Id, Addressable 등록도 맞춘다. 에러가 있으면 저장하지 않는다" });
        bar.Add(new ToolbarButton(SaveAll) { text = "모두 저장", tooltip = "`*`인 트리 모두 저장 — 이름표만 채운 트리는 에러 · 파일 이름 ≠ 트리 Id · 없어진 파일 · 밖에서 바뀐 파일이면 건너뜀" });
        bar.Add(new ToolbarSpacer());
        bar.Add(BuildNewTreeButton());
        bar.Add(new ToolbarSpacer());
        bar.Add(new ToolbarButton(Reload) { text = "새로고침", tooltip = "디스크에서 다시 읽기 (몬스터 데이터 · Addressable 등록도) — 저장하지 않은 편집은 사라진다" });
        bar.Add(new ToolbarSpacer());
        bar.Add(BuildDebugBar());
        _status = new Label("");
        _status.style.unityTextAlign = TextAnchor.MiddleLeft;
        _status.style.marginLeft = 8;
        bar.Add(_status);
        return bar;
    }

    private void Reload()
    {
        if (ConfirmDiscard() == false)
            return;
        RenewStamp(); // 다시 읽기 — 그 전에 담아 둔 편집은 이제 지난 것
        string keepPath = _selectedPath;
        LoadFolder();
        ScanUsers(); // 주소 등록이 바뀐 것은 데이터 파일 서명에 잡히지 않는다
        SelectByPath(keepPath);
        SetStatus(LoadSummary());
        RunAllChecksNow();
    }

    /// <summary>다시 읽기 전에 묻는다 — 다시 읽으면 잃는 편집이 있는 트리만 센다 (HasLosableEdits). 없으면 묻지 않고 true</summary>
    private bool ConfirmDiscard()
    {
        JsonDocumentList.RefreshDirty(_documents);
        int count = 0;
        for (int i = 0; i < _documents.Count; i++)
        {
            if (_documents[i].HasLosableEdits())
                count++;
        }
        if (count == 0)
            return true;
        return EditorUtility.DisplayDialog("편집 버리기", $"다시 읽으면 트리 {count}개의 편집이 사라집니다.", "버리고 다시 읽기", "취소");
    }

    private void SetStatus(string text, bool isError = false)
    {
        if (_status == null)
            return;
        _status.text = text;
        _status.style.color = isError ? EditorStatusColors.Error : EditorStatusColors.Status;
    }

    private string LoadSummary()
    {
        if (Directory.Exists(Folder) == false)
            return $"폴더가 없습니다: {Folder}";
        string summary = $"트리 {_documents.Count}개";
        if (_brokenFiles.Count > 0)
            summary += $", 파싱 실패 {_brokenFiles.Count}개";
        int filled = _documents.FindAll(document => document.HasOnlyLoadFill()).Count;
        if (filled > 0)
            summary += $", 노드 이름표를 채운 트리 {filled}개 (`*` — 저장하면 파일에 들어감, 디버거 · breakpoint용)";
        return summary;
    }
    #endregion

    #region 불러오기
    /// <summary>
    /// 폴더 바로 아래의 JSON을 읽는다 (하위 폴더는 보지 않음 — 배치 파일은 Editor 아래). 트리마다 배치 파일도 읽는다 (BehaviorTreeFiles.LoadLayout — 없으면 처음 보일 때 정리 배치).
    /// 읽지 못하거나 파싱에 실패한 파일은 '파싱 실패' 항목으로. 사본은 트리 Id 순
    /// </summary>
    private void LoadFolder()
    {
        ForgetAddressBook(); // 다시 읽으면 Addressable 등록도 다시 본다 — 버전 관리로 그룹 파일이 바뀐 것처럼 수정 이벤트가 오지 않는 변경
        _graph?.Show(null);
        _selected = null;
        _selectedBroken = null;
        DisposeSelectedObject();
        JsonDocumentList.DestroyAll(_documents);
        _brokenFiles.Clear();

        BehaviorNodeRegistry registry = BehaviorNodeRegistry.Default;
        string[] files = EditorJsonFolder.ListJsonFiles(Folder);
        for (int i = 0; i < files.Length; i++)
        {
            BehaviorTreeAsset tree = Read(files[i], registry, out string text, out bool hasBom, out string error);
            if (tree == null)
            {
                _brokenFiles.Add(new JsonBrokenFile { FilePath = files[i], Error = error });
                continue;
            }
            BehaviorTreeDocumentAsset document = BehaviorTreeDocumentAsset.Create(tree, registry, files[i], text, hasBom);
            BehaviorTreeFiles.LoadLayout(document);
            _documents.Add(document);
        }
        SortDocuments();
        UpdateUnsavedFlag(); // 지운 사본의 표시도 여기서 맞춘다
    }

    /// <summary>사본을 이름 (트리 Id) 순으로</summary>
    private void SortDocuments()
    {
        _documents.Sort((a, b) => string.CompareOrdinal(LabelOf(a), LabelOf(b)));
    }

    /// <summary>편집용으로 읽는다 — UTF-8 · BOM 유무 (EditorJsonFile.ReadText), 파싱만 (BehaviorTreeLoader.ParseAsset — 구조 검사 없음, 깨진 트리도 연다). 실패하면 null과 까닭</summary>
    private static BehaviorTreeAsset Read(string path, BehaviorNodeRegistry registry, out string text, out bool hasBom, out string error)
    {
        try
        {
            text = EditorJsonFile.ReadText(path, out hasBom);
        }
        catch (Exception e) // IO · 권한 · 경로 형식 — 어느 것이든 읽기 실패
        {
            text = null;
            hasBom = false;
            error = e.Message;
            return null;
        }

        BehaviorTreeAsset tree = BehaviorTreeLoader.ParseAsset(text, registry, out error);
        if (tree == null && EditorJsonFile.StartsWithWideBom(path))
            error = EditorJsonFile.WideBomReason;
        return tree;
    }
    #endregion

    #region 트리 목록
    private VisualElement BuildTreePane()
    {
        var pane = new VisualElement();
        pane.Add(EditorPaneUI.Header("Behavior Trees"));
        pane.Add(BuildMissingLabel());
        _treeList = new ListView
        {
            itemsSource = _items,
            fixedItemHeight = 22,
            selectionType = SelectionType.Single,
            makeItem = EditorPaneUI.FileRow,
            bindItem = BindTreeRow,
        };
        _treeList.selectionChanged += OnTreeSelectionChanged;
        _treeList.style.flexGrow = 1;
        pane.Add(_treeList);
        return pane;
    }

    private void BindTreeRow(VisualElement element, int index)
    {
        Label main = element.Q<Label>(EditorPaneUI.RowMainName);
        Label file = element.Q<Label>(EditorPaneUI.RowFileName);
        object item = _items[index];
        if (item is BehaviorTreeDocumentAsset document)
        {
            main.text = (document.NeedsSave() ? "* " : "") + LabelOf(document);
            main.style.color = StyleKeyword.Null;
            // 파일 이름은 저장하면 바뀔 때만 보인다 — 저장 계획과 같은 판정 (대소문자만 다른 것도 바꾼다)
            JsonSavePlan plan = JsonSavePlan.Decide(Folder, document.FilePath, document.Tree.Id);
            string fileName = plan.IsNew ? "새 트리" : plan.Rename || plan.CaseOnlyRename ? Path.GetFileName(document.FilePath) : "";
            file.text = IssueCountText(document) + fileName;
            element.tooltip = (document.FilePath ?? "저장하지 않은 새 트리") + (document.ErrorCount + document.WarningCount > 0 ? $"\n검사: 에러 {document.ErrorCount} · 경고 {document.WarningCount}" : "")
                + (document.HasOnlyLoadFill() ? "\n불러올 때 노드 이름표를 채움 — 저장하면 파일에 들어감 (Play 디버거 · breakpoint가 이름표로 노드를 찾는다)" : "");
        }
        else if (item is JsonBrokenFile broken)
        {
            main.text = "✕ 파싱 실패";
            main.style.color = EditorStatusColors.Error;
            file.text = Path.GetFileName(broken.FilePath);
            element.tooltip = broken.Error;
        }
    }

    /// <summary>"✕2 ⚠1 " — 검사 에러 · 경고 수 (없으면 빈 글)</summary>
    private static string IssueCountText(BehaviorTreeDocumentAsset document)
    {
        string text = document.ErrorCount > 0 ? $"✕{document.ErrorCount} " : "";
        return document.WarningCount > 0 ? text + $"⚠{document.WarningCount} " : text;
    }

    /// <summary>목록을 다시 채운다 — 사본, 그다음 파싱 실패 파일. 고른 항목은 표시만 맞춘다 (알림 없이)</summary>
    private void RefreshTreeList()
    {
        if (_treeList == null)
            return;
        _items.Clear();
        _items.AddRange(_documents);
        _items.AddRange(_brokenFiles);
        _treeList.RefreshItems();

        object current = _selected != null ? _selected : (object)_selectedBroken;
        int index = current != null ? _items.IndexOf(current) : -1;
        _treeList.SetSelectionWithoutNotify(index >= 0 ? new[] { index } : Array.Empty<int>());
    }

    /// <summary>목록에서 트리를 골랐을 때 — 입력 중이던 칸 (세부 설정 · Blackboard)이 확정된 뒤에 바꾼다 (AfterInputCommit)</summary>
    private void OnTreeSelectionChanged(IEnumerable<object> selection)
    {
        foreach (object item in selection)
        {
            AfterInputCommit(() => SelectItem(item));
            return;
        }
    }

    /// <summary>목록 항목을 고른다. 미루는 사이 새로고침으로 목록이 바뀌었으면 무시한다</summary>
    private void SelectItem(object item)
    {
        if ((item is BehaviorTreeDocumentAsset document && _documents.Contains(document)) || (item is JsonBrokenFile broken && _brokenFiles.Contains(broken)))
            Select(item);
    }

    /// <summary>경로로 고른다 (처음 그릴 때 · 새로고침 · 창을 다시 만든 뒤). 없으면 고른 것 없음</summary>
    private void SelectByPath(string path)
    {
        object item = null;
        if (string.IsNullOrEmpty(path) == false)
        {
            item = _documents.Find(document => document.FilePath == path);
            if (item == null)
                item = _brokenFiles.Find(broken => broken.FilePath == path);
        }
        Select(item);
        RefreshTreeList();
    }

    /// <summary>항목을 고른다 — 놓는 트리의 마지막 편집 (미저장 판정 · 검사)을 먼저 반영한다: 값 추적은 패널과 함께 사라져, 방금 확정된 값의 알림이 오지 않을 수 있다</summary>
    private void Select(object item)
    {
        RenewStamp(); // 고른 트리 바꿈 (Save.cs)
        if (_selected != null)
            RunChecksNow();
        ClearReveal();
        _selected = item as BehaviorTreeDocumentAsset;
        _selectedBroken = item as JsonBrokenFile;
        _selectedPath = _selected != null ? _selected.FilePath : _selectedBroken?.FilePath;
        DisposeSelectedObject();
        if (_selected != null)
            _selectedObject = new SerializedObject(_selected);
        ShowSelected();
    }

    /// <summary>패널을 먼저 풀고 비운 뒤 SerializedObject를 정리한다 (반대 순서면 정리된 객체를 패널이 읽으려 한다 — Interaction Editor와 같은 순서)</summary>
    private void DisposeSelectedObject()
    {
        _panelContent?.Unbind();
        _panelContent = null;
        _panelScroll?.Clear();
        _selectedObject?.Dispose();
        _selectedObject = null;
    }
    #endregion

    #region 그래프 영역
    private VisualElement BuildGraphPane()
    {
        var pane = new VisualElement();
        pane.style.flexGrow = 1;

        _treeHeader = EditorPaneUI.HeaderRow("", out _treeTitle);
        var hint = new Label("노드 추가: 오른쪽 클릭 · Space · 칸에서 끈 선을 빈 곳에 놓기");
        hint.style.opacity = 0.6f;
        hint.style.marginRight = 8;
        _treeHeader.Add(hint);
        _treeHeader.Add(BuildClearBreakpointsButton());
        _treeHeader.Add(new Button(() => _graph.ArrangeAll()) { text = "정리 배치", tooltip = "위에서 아래로 다시 놓는다 — 자식 차례는 그대로 (Undo 가능)" });
        _treeHeader.Add(new Button(() => _graph.FrameAllCards()) { text = "화면 맞춤", tooltip = "모든 카드가 보이게" });
        pane.Add(_treeHeader);

        _graph = new BehaviorTreeGraphView(this, OnDocumentChanged, OnGraphSelectionChanged, SetStatus);
        _graph.SetBreakpointHandlers(HasBreakpoint, ToggleBreakpoint);
        pane.Add(_graph);

        _messageView = new VisualElement();
        _messageView.style.flexGrow = 1;
        pane.Add(_messageView);
        return pane;
    }

    /// <summary>고른 항목을 보인다 — 트리면 그래프 · 패널 · 검사, 파싱 실패 파일이면 까닭, 없으면 안내</summary>
    private void ShowSelected()
    {
        if (_graph == null)
            return;
        bool showsTree = _selected != null;
        _treeHeader.style.display = showsTree ? DisplayStyle.Flex : DisplayStyle.None;
        _graph.style.display = showsTree ? DisplayStyle.Flex : DisplayStyle.None;
        _messageView.style.display = showsTree ? DisplayStyle.None : DisplayStyle.Flex;
        _messageView.Clear();
        _graph.Show(_selected);
        ShowChecksOfSelected();
        SchedulePanel();
        DebugTick(); // 이 트리의 breakpoint 표시 · (Play 중이면) 대상의 모습

        if (showsTree)
        {
            UpdateTreeTitle();
            return;
        }
        if (_selectedBroken != null)
        {
            string path = _selectedBroken.FilePath;
            _messageView.Add(EditorPaneUI.BrokenFileView(
                "JSON 파싱에 실패해 편집기에서 열 수 없는 파일입니다. 게임에서도 이 트리는 로드되지 않습니다.",
                path,
                _selectedBroken.Error,
                "텍스트 편집기로 고친 뒤 [새로고침]을 누르세요. 흔한 원인: 쉼표 누락, 칸 이름 오타, 등록되지 않은 노드 이름, 없는 enum 이름.",
                () => OpenInTextEditor(path)));
            return;
        }
        _messageView.Add(EditorPaneUI.Hint(_documents.Count + _brokenFiles.Count > 0 ? "왼쪽에서 트리를 고르세요" : $"트리 파일이 없습니다 — {Folder}"));
    }

    /// <summary>그래프 위 머리 — 트리 이름 (`*` 저장할 것 있음) · 파일 이름 · 이어지지 않은 노드 수</summary>
    private void UpdateTreeTitle()
    {
        if (_treeTitle == null || _selected == null)
            return;
        string title = (_selected.NeedsSave() ? "* " : "") + LabelOf(_selected) + "   " + (_selected.FilePath != null ? Path.GetFileName(_selected.FilePath) : "(저장 전 새 트리)");
        if (_selected.LooseNodes.Count > 0)
            title += $"   — 이어지지 않은 노드 {_selected.LooseNodes.Count}개 (실행 순서에 들지 않음)";
        _treeTitle.text = title;
    }

    /// <summary>텍스트 편집기로 연다 — Unity의 열기 (외부 편집기 설정), 에셋이 없으면 OS 기본 앱</summary>
    private static void OpenInTextEditor(string path)
    {
        var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        if (textAsset != null)
            AssetDatabase.OpenAsset(textAsset);
        else
            EditorUtility.OpenWithDefaultApp(path);
    }
    #endregion

    #region 바꾸기 · Undo
    /// <summary>그래프가 사본의 구조 · 카드 자리를 바꾼 뒤 — 미저장 판정 · 목록 · 머리 · 패널 (칸의 SerializedObject를 사본에 맞춘다) · 검사</summary>
    private void OnDocumentChanged()
    {
        RenewStamp(); // 담아 둔 편집의 표식을 새로 (Save.cs)
        _selected?.RefreshDirty();
        UpdateUnsavedFlag(); // 판정이 그대로여도 — 불러올 때 채운 이름표뿐이던 트리에 편집이 더해지면 IsDirty는 그대로 참이다
        _selectedObject?.Update();
        _treeList?.RefreshItems();
        UpdateTreeTitle();
        SchedulePanel();
        ScheduleChecks();
    }

    /// <summary>
    /// 패널이 칸 값 · 메모 · 이름표 · 키를 바꾼 뒤 (패널이 지켜보는 사본 값이 바뀔 때마다 — 그래프 · Undo · 처음 배치가 바꿔도) — 카드 글은 바로,
    /// 미저장 판정 · 목록 · 머리 · 검사는 잠깐 뒤 한 번 (ScheduleChecks — 끌어서 바꾸는 칸은 매 프레임 바뀐다).
    /// 닫을 때 묻기가 꺼져 있으면 고른 트리를 바로 판정한다 — 바꾼 직후 창을 닫아도 묻게 (이미 켜져 있으면 끌기 중 매 프레임 판정하지 않는다). 패널은 그대로 둔다 — 입력 · 끌기 중인 칸을 지우지 않게
    /// (칸의 잠금 · 목록이 바뀐 때만 다음 갱신에 다시 만든다 — RebuildPanelIfFieldStatesChanged)
    /// </summary>
    private void AfterPanelEdit()
    {
        RenewStamp(); // 담아 둔 편집의 표식을 새로 (Save.cs)
        if (hasUnsavedChanges == false)
        {
            _selected?.RefreshDirty();
            UpdateUnsavedFlag();
        }
        _graph?.RefreshTexts();
        ScheduleChecks();
        RebuildPanelIfFieldStatesChanged();
    }

    /// <summary>Undo는 고르지 않은 트리도 바꿀 수 있어 모든 사본을 다시 판정 · 검사한다. 그래프는 사본에서 다시 그리고, 패널은 다시 만든다</summary>
    private void OnUndoRedo()
    {
        RenewStamp(); // 담아 둔 편집의 표식을 새로 (Save.cs)
        JsonDocumentList.RefreshDirty(_documents);
        UpdateUnsavedFlag();
        _selectedObject?.Update();
        _graph?.RebuildNow();
        _treeList?.RefreshItems();
        UpdateTreeTitle();
        SchedulePanel();
        ScheduleChecks(all: true);
    }

    /// <summary>사본을 바꾸기 전 — 사본 전체를 Undo에 남긴다</summary>
    private void Record(string undoName)
    {
        Undo.RegisterCompleteObjectUndo(_selected, undoName);
    }
    #endregion

    #region 도우미
    /// <summary>목록 · 머리 · 상태 줄의 트리 이름 — 트리 Id, 비었으면 파일 이름 (저장 전 새 트리면 "(새 트리)")</summary>
    private static string LabelOf(BehaviorTreeDocumentAsset document)
    {
        if (string.IsNullOrEmpty(document.Tree.Id) == false)
            return document.Tree.Id;
        return document.FilePath != null ? Path.GetFileName(document.FilePath) : "(새 트리)";
    }
    #endregion
}