using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 기획자용 상호작용 조립 툴. Tools > Interaction Editor. 도킹 가능.
/// 레이아웃: [Set 목록] | [선택 Set의 Interaction 카드] | [선택 노드 파라미터]  /  하단 [검증]
/// 구조 변경(카드 추가·삭제·복제·이동)은 C# 객체를 Undo와 함께 고치고 뷰를 재구성,
/// 값 편집(필드·노드 목록)은 SerializedObject 바인딩에 맡긴다.
/// 미저장 판정은 "불러온(저장한) 때의 JSON과 지금 저장될 JSON 비교"로 한다 — 켜기만 하는 플래그는 Undo를 따라가지 못한다.
///
/// 파일 구성 (partial)
///   InteractionEditorWindow.cs             창 골격 · Set 목록 · 선택 · 카드 영역 · Undo
///   InteractionEditorWindow.Save.cs        불러오기 · 새 Set · 저장
///   InteractionEditorWindow.Validation.cs  실시간 검사 · 검증 목록
/// </summary>
public sealed partial class InteractionEditorWindow : EditorWindow
{
    // public: State Definition Editor가 같은 폴더를 읽는다 (상호작용 참조 스캔)
    public const string FolderPrefKey = "InteractionEditor.Folder";
    public const string DefaultFolder = "Assets/@Resources/Data/JsonData/Interactions";

    private const string SetIdPath = "Set.Id";
    private const string SetInteractionsPath = "Set.Interactions";
    private const long DeferMs = 50; // 다시 만든 카드가 배치될 때까지 기다리는 시간

    /// <summary>파싱에 실패한 파일 — 목록에 보여 주기만 하고 편집할 수 없다.</summary>
    private sealed class BrokenFile
    {
        public string FilePath;
        public string Error;
    }

    private static readonly Color ErrorColor = new Color(1f, 0.45f, 0.45f);
    private static readonly Color WarningColor = new Color(1f, 0.82f, 0.4f);
    private static readonly Color StatusColor = new Color(0.7f, 0.7f, 0.7f);

    private string _folder;
    private readonly List<InteractionSetAsset> _assets = new List<InteractionSetAsset>();
    private readonly List<BrokenFile> _brokenFiles = new List<BrokenFile>();
    private readonly List<object> _visible = new List<object>(); // 검색을 거친 목록 항목 (InteractionSetAsset 또는 BrokenFile)
    private string _search = "";

    private InteractionSetAsset _selected;
    private BrokenFile _selectedBroken;
    private SerializedObject _selectedObject;
    private string _selectedNodePath;

    // 도메인 리로드(스크립트 컴파일) 뒤에도 선택을 복원하기 위해 창에 직렬화한다
    [SerializeField] private string _selectedPath;

    // UI
    private Label _folderLabel;
    private Label _status;
    private ListView _setList;
    private VisualElement _setHeader;
    private Label _fileLabel;
    private ScrollView _cardScroll;
    private VisualElement _inspectorPane;
    private readonly List<InteractionCardView> _cardViews = new List<InteractionCardView>();

    [MenuItem("Tools/Interaction Editor")]
    public static void Open()
    {
        InteractionEditorWindow window = GetWindow<InteractionEditorWindow>();
        window.titleContent = new GUIContent("Interaction Editor");
        window.minSize = new Vector2(960, 540);
    }

    #region 생명주기
    private void OnEnable()
    {
        _folder = EditorPrefs.GetString(FolderPrefKey, DefaultFolder);
        Undo.undoRedoPerformed += OnUndoRedo;
        LoadFolder();
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
        DisposeSelection();
        DestroyAssets();
    }

    private void OnFocus()
    {
        UpdateUnsavedFlag();
        RescanStateDefinitions(false); // 상태 정의 파일이 바뀌었으면 다시 읽는다
    }

    private void CreateGUI()
    {
        VisualElement root = rootVisualElement;
        root.Add(BuildToolbar());

        // 가로 3분할 (목록 | 카드 | 파라미터) 을 세로 분할 (본문 / 검증) 위에 올린다
        var horizontalOuter = new TwoPaneSplitView(0, 220, TwoPaneSplitViewOrientation.Horizontal);
        var horizontalInner = new TwoPaneSplitView(1, 320, TwoPaneSplitViewOrientation.Horizontal);
        horizontalOuter.Add(BuildSetPane());
        horizontalOuter.Add(horizontalInner);
        horizontalInner.Add(BuildCenterPane());
        _inspectorPane = new VisualElement();
        horizontalInner.Add(_inspectorPane);

        var vertical = new TwoPaneSplitView(1, 120, TwoPaneSplitViewOrientation.Vertical);
        vertical.Add(horizontalOuter);
        vertical.Add(BuildValidationPane());
        vertical.style.flexGrow = 1;
        root.Add(vertical);
        root.RegisterCallback<PointerDownEvent>(OnRootPointerDown, TrickleDown.TrickleDown);

        _validationJob = root.schedule.Execute(RunValidationNow);
        _validationJob.Pause();

        SelectByPath(_selectedPath);
        RunValidationNow();
        SetStatus(LoadSummary());
    }
    #endregion

    #region 툴바
    private VisualElement BuildToolbar()
    {
        var bar = new Toolbar();

        bar.Add(new ToolbarButton(PickFolder) { text = "폴더", tooltip = "Interaction JSON이 있는 폴더 지정 (Assets 아래)" });
        _folderLabel = new Label(_folder);
        _folderLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
        _folderLabel.style.marginLeft = 4;
        _folderLabel.style.marginRight = 12;
        _folderLabel.style.opacity = 0.7f;
        bar.Add(_folderLabel);

        bar.Add(new ToolbarButton(Reload) { text = "새로고침", tooltip = "디스크에서 다시 읽기" });
        bar.Add(new ToolbarButton(CreateNewSet) { text = "+ 새 Set" });
        bar.Add(new ToolbarButton(SaveSelected) { text = "저장" });
        bar.Add(new ToolbarButton(SaveAll) { text = "모두 저장" });
        bar.Add(new ToolbarSpacer());

        var search = new ToolbarSearchField { tooltip = "Set Id·파일 이름으로 찾기" };
        search.RegisterValueChangedCallback(e =>
        {
            _search = e.newValue ?? "";
            RefreshSetList();
        });
        bar.Add(search);

        _status = new Label("");
        _status.style.unityTextAlign = TextAnchor.MiddleLeft;
        _status.style.marginLeft = 8;
        bar.Add(_status);
        return bar;
    }

    private void PickFolder()
    {
        string absolute = EditorUtility.OpenFolderPanel("Interaction JSON 폴더", _folder, "");
        if (string.IsNullOrEmpty(absolute))
            return;

        string dataPath = Application.dataPath.Replace('\\', '/');
        absolute = absolute.Replace('\\', '/');
        if (absolute.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase) == false)
        {
            SetStatus("프로젝트 Assets 아래 폴더만 지정할 수 있습니다", true);
            return;
        }
        if (ConfirmDiscard("폴더를 바꾸면") == false)
            return;

        _folder = "Assets" + absolute.Substring(dataPath.Length);
        EditorPrefs.SetString(FolderPrefKey, _folder);
        _folderLabel.text = _folder;
        ReloadAll();
    }

    private void Reload()
    {
        if (ConfirmDiscard("새로고침하면"))
            ReloadAll();
    }

    /// <summary>디스크에서 다시 읽고, 같은 파일이 있으면 선택을 유지한다.</summary>
    private void ReloadAll()
    {
        string keepPath = _selected != null ? _selected.FilePath : _selectedBroken?.FilePath;
        LoadFolder();
        SelectByPath(keepPath);
        RescanStateDefinitions(true);
        RunValidationNow();
        SetStatus(LoadSummary());
    }

    private bool ConfirmDiscard(string action)
    {
        int dirty = CountDirty();
        if (dirty == 0)
            return true;

        return EditorUtility.DisplayDialog("저장하지 않은 변경",
            $"{action} 저장하지 않은 Set {dirty}개의 변경이 사라집니다.", "버리고 진행", "취소");
    }

    private void SetStatus(string text, bool isError = false)
    {
        if (_status == null)
            return;
        _status.text = text;
        _status.style.color = isError ? ErrorColor : StatusColor;
    }
    #endregion

    #region Set 목록
    private VisualElement BuildSetPane()
    {
        var pane = new VisualElement();
        pane.Add(MakeHeader("Interaction Sets"));

        _setList = new ListView
        {
            itemsSource = _visible,
            fixedItemHeight = 22,
            selectionType = SelectionType.Single,
            makeItem = MakeSetRow,
            bindItem = BindSetRow,
        };
        _setList.selectionChanged += OnSetSelectionChanged;
        _setList.style.flexGrow = 1;
        pane.Add(_setList);
        return pane;
    }

    private static VisualElement MakeSetRow()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.paddingLeft = 8;
        row.style.paddingRight = 6;

        var main = new Label { name = "main" };
        main.style.flexGrow = 1;
        main.style.unityTextAlign = TextAnchor.MiddleLeft;
        row.Add(main);

        var file = new Label { name = "file" };
        file.style.opacity = 0.5f;
        file.style.unityTextAlign = TextAnchor.MiddleRight;
        row.Add(file);
        return row;
    }

    private void BindSetRow(VisualElement element, int index)
    {
        Label main = element.Q<Label>("main");
        Label file = element.Q<Label>("file");
        object item = _visible[index];

        if (item is InteractionSetAsset asset)
        {
            string badge = asset.ErrorCount > 0 ? $"   ✕{asset.ErrorCount}" : asset.WarningCount > 0 ? $"   ⚠{asset.WarningCount}" : "";
            main.text = (asset.IsDirty ? "* " : "") + SetLabel(asset) + badge;
            if (asset.ErrorCount > 0)
                main.style.color = ErrorColor;
            else
                main.style.color = StyleKeyword.Null;

            // 파일 이름은 Set Id와 다를 때만 보여 준다 — 저장하면 파일 이름이 Set Id로 바뀐다는 표시
            string fileName = asset.FilePath != null ? Path.GetFileName(asset.FilePath) : "새 Set";
            file.text = fileName == asset.Set.Id + ".json" ? "" : fileName;
            element.tooltip = asset.FilePath ?? "저장하지 않은 새 Set";
        }
        else if (item is BrokenFile broken)
        {
            main.text = "✕ 파싱 실패";
            main.style.color = ErrorColor;
            file.text = Path.GetFileName(broken.FilePath);
            element.tooltip = broken.Error;
        }
    }

    private void RefreshSetList()
    {
        if (_setList == null)
            return;

        _visible.Clear();
        for (int i = 0; i < _assets.Count; i++)
        {
            InteractionSetAsset asset = _assets[i];
            if (Matches(asset.Set.Id) || Matches(asset.FilePath != null ? Path.GetFileName(asset.FilePath) : null))
                _visible.Add(asset);
        }
        for (int i = 0; i < _brokenFiles.Count; i++)
        {
            if (Matches(Path.GetFileName(_brokenFiles[i].FilePath)))
                _visible.Add(_brokenFiles[i]);
        }
        _setList.RefreshItems();

        object current = _selected != null ? _selected : (object)_selectedBroken;
        int selectedIndex = current != null ? _visible.IndexOf(current) : -1;
        _setList.SetSelectionWithoutNotify(selectedIndex >= 0 ? new[] { selectedIndex } : Array.Empty<int>());
    }

    private bool Matches(string text)
    {
        return _search.Length == 0 || (text != null && text.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private void OnSetSelectionChanged(IEnumerable<object> selection)
    {
        foreach (object item in selection)
        {
            if (item is InteractionSetAsset asset)
                SelectSet(asset);
            else if (item is BrokenFile broken)
                SelectBroken(broken);
            return;
        }
    }

    private void SelectSet(InteractionSetAsset asset)
    {
        DisposeSelection();
        _selected = asset;
        _selectedBroken = null;
        _selectedPath = asset != null ? asset.FilePath : null;
        _selectedObject = asset != null ? new SerializedObject(asset) : null;

        RebuildSetView(false, -1);
        ShowNodeInspector(null);
        RebuildValidationRows(); // "현재 Set만" 보기는 선택에 따라 달라진다
    }

    private void SelectBroken(BrokenFile broken)
    {
        DisposeSelection();
        _selected = null;
        _selectedBroken = broken;
        _selectedPath = broken?.FilePath;

        RebuildSetView(false, -1);
        ShowNodeInspector(null);
        RebuildValidationRows();
    }

    /// <summary>경로로 다시 선택한다 (새로고침·도메인 리로드 뒤). 못 찾으면 선택 없음.</summary>
    private void SelectByPath(string path)
    {
        InteractionSetAsset asset = null;
        BrokenFile broken = null;
        if (string.IsNullOrEmpty(path) == false)
        {
            asset = _assets.Find(a => a.FilePath == path);
            if (asset == null)
                broken = _brokenFiles.Find(b => b.FilePath == path);
        }

        if (broken != null)
            SelectBroken(broken);
        else
            SelectSet(asset);
        RefreshSetList();
    }

    /// <summary>바인딩된 UI를 먼저 풀고 비운 뒤 SerializedObject를 정리한다 (반대 순서면 정리된 객체를 UI가 읽으려 한다).</summary>
    private void DisposeSelection()
    {
        ClearSetView();
        if (_inspectorPane != null)
        {
            _inspectorPane.Unbind();
            _inspectorPane.Clear();
        }
        _selectedObject?.Dispose();
        _selectedObject = null;
        _selectedNodePath = null;
    }
    #endregion

    #region 카드 영역
    private VisualElement BuildCenterPane()
    {
        var pane = new VisualElement();
        _setHeader = new VisualElement();
        _setHeader.style.flexDirection = FlexDirection.Row;
        _setHeader.style.alignItems = Align.Center;
        _setHeader.style.paddingLeft = 6;
        _setHeader.style.paddingRight = 6;
        _setHeader.style.paddingTop = 4;
        _setHeader.style.paddingBottom = 4;
        pane.Add(_setHeader);

        _cardScroll = new ScrollView(ScrollViewMode.Vertical);
        _cardScroll.style.flexGrow = 1;
        pane.Add(_cardScroll);
        return pane;
    }

    private void ClearSetView()
    {
        _cardViews.Clear();
        _fileLabel = null;
        if (_setHeader == null)
            return;

        _setHeader.Unbind();
        _setHeader.Clear();
        _cardScroll.Unbind();
        _cardScroll.Clear();
    }

    /// <summary>
    /// 머리글과 카드를 다시 만든다. 카드는 배열 순번 경로에 묶여 있어 구조가 바뀌면 다시 만들어야 한다.
    /// keepScroll: 같은 Set을 다시 그릴 때 스크롤 위치를 유지한다. scrollToCard: 그 카드가 보이게 한다 (없으면 -1).
    /// </summary>
    private void RebuildSetView(bool keepScroll, int scrollToCard)
    {
        if (_setHeader == null)
            return;

        Vector2 offset = _cardScroll.scrollOffset;
        ClearSetView();

        if (_selectedBroken != null)
        {
            BuildBrokenView();
            return;
        }
        if (_selected == null)
        {
            _cardScroll.Add(Hint("왼쪽에서 Set을 선택하세요"));
            return;
        }

        _selectedObject.Update();

        var idField = new TextField("Set Id")
        {
            bindingPath = SetIdPath,
            isDelayed = true, // Enter나 포커스 이동 때 한 번만 확정 — 글자마다 Undo 기록과 검사가 생기지 않게
            tooltip = "게임이 Set을 찾는 이름. 파일 이름도 이 값을 따른다 — 바꿔서 저장하면 파일 이름만 바뀐다 (.meta와 GUID는 유지)",
        };
        idField.style.flexGrow = 1;
        _setHeader.Add(idField);

        _fileLabel = new Label();
        _fileLabel.style.marginLeft = 8;
        _fileLabel.style.marginRight = 8;
        _fileLabel.style.opacity = 0.6f;
        _setHeader.Add(_fileLabel);
        UpdateFileLabel();

        _setHeader.Add(new Button(AddInteraction) { text = "+ Interaction" });
        _setHeader.Bind(_selectedObject);

        // 카드 컨테이너를 매번 새로 만들어 TrackSerializedObjectValue 등록이 누적되지 않게 한다
        var container = new VisualElement();
        SerializedProperty array = _selectedObject.FindProperty(SetInteractionsPath);
        for (int i = 0; i < array.arraySize; i++)
        {
            var card = new InteractionCardView(_selectedObject, i, new InteractionCardView.Callbacks
            {
                MoveUp = index => MoveInteraction(index, -1),
                MoveDown = index => MoveInteraction(index, +1),
                Duplicate = DuplicateInteraction,
                Delete = DeleteInteraction,
                NodeSelected = OnNodeSelected,
            });
            _cardViews.Add(card);
            container.Add(card);
        }
        container.TrackSerializedObjectValue(_selectedObject, _ => OnSelectedValueChanged());
        _cardScroll.Add(container);

        if (keepScroll || scrollToCard >= 0)
            RestoreScrollLater(offset, scrollToCard);
    }

    /// <summary>다시 만든 직후에는 스크롤이 맨 위로 간다. 배치가 끝난 뒤 원래 자리로 되돌리고, 지정한 카드가 있으면 그 카드가 보이게 한다.</summary>
    private void RestoreScrollLater(Vector2 offset, int scrollToCard)
    {
        _cardScroll.schedule.Execute(() =>
        {
            _cardScroll.scrollOffset = offset;
            if (scrollToCard >= 0 && scrollToCard < _cardViews.Count)
                _cardScroll.ScrollTo(_cardViews[scrollToCard]);
        }).ExecuteLater(DeferMs);
    }

    private void UpdateFileLabel()
    {
        if (_fileLabel == null || _selected == null)
            return;
        _fileLabel.text = _selected.FilePath != null
            ? Path.GetFileName(_selected.FilePath)
            : "새 Set — 저장하면 Set Id가 파일 이름이 됩니다";
    }

    private void BuildBrokenView()
    {
        var box = new VisualElement();
        box.style.paddingLeft = 8;
        box.style.paddingRight = 8;
        box.style.paddingTop = 8;

        var title = new Label("JSON 파싱에 실패해 툴에서 편집할 수 없는 파일입니다. 게임에서도 이 Set은 로드되지 않습니다.");
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = ErrorColor;
        title.style.whiteSpace = WhiteSpace.Normal;
        box.Add(title);
        box.Add(new Label(_selectedBroken.FilePath) { style = { opacity = 0.6f, marginBottom = 6 } });

        var error = new TextField { value = _selectedBroken.Error, multiline = true, isReadOnly = true };
        error.style.whiteSpace = WhiteSpace.Normal;
        box.Add(error);

        box.Add(Hint("텍스트 편집기로 고친 뒤 [새로고침]을 누르세요. 흔한 원인: 쉼표 누락, 필드 이름 오타, 등록되지 않은 노드 이름, 없는 enum 이름."));
        string path = _selectedBroken.FilePath;
        box.Add(new Button(() => OpenInTextEditor(path)) { text = "텍스트 편집기로 열기" });
        _cardScroll.Add(box);
    }

    private static void OpenInTextEditor(string path)
    {
        var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        if (textAsset != null)
            AssetDatabase.OpenAsset(textAsset);
        else
            EditorUtility.OpenWithDefaultApp(path);
    }
    #endregion

    #region 구조 변경 (카드 추가·이동·복제·삭제)
    private void AddInteraction()
    {
        if (_selected == null)
            return;

        var list = new List<InteractionDefinition>(_selected.Set.Interactions);
        string id = "new_interaction";
        int suffix = 1;
        while (list.Exists(d => d != null && d.Id == id))
            id = $"new_interaction_{suffix++}";

        list.Add(new InteractionDefinition
        {
            Id = id,
            Trigger = Define.ETriggerType.Tick,
            Mode = Define.EActivationMode.Fire,
            Conditions = Array.Empty<InteractionCondition>(),
            EffectPrototypes = Array.Empty<InteractionEffect>(),
        });
        ApplyInteractions("Add Interaction", list, list.Count - 1);
    }

    private void MoveInteraction(int index, int delta)
    {
        if (_selected == null)
            return;

        var list = new List<InteractionDefinition>(_selected.Set.Interactions);
        int target = index + delta;
        if (index < 0 || index >= list.Count || target < 0 || target >= list.Count)
            return;

        (list[index], list[target]) = (list[target], list[index]);
        ApplyInteractions("Move Interaction", list, target);
    }

    private void DuplicateInteraction(int index)
    {
        if (_selected == null)
            return;

        var list = new List<InteractionDefinition>(_selected.Set.Interactions);
        if (index < 0 || index >= list.Count || list[index] == null)
            return;

        InteractionDefinition clone = InteractionLoader.CloneDefinition(list[index]);
        clone.Id = list[index].Id + "_copy";
        list.Insert(index + 1, clone);
        ApplyInteractions("Duplicate Interaction", list, index + 1);
    }

    private void DeleteInteraction(int index)
    {
        if (_selected == null)
            return;

        var list = new List<InteractionDefinition>(_selected.Set.Interactions);
        if (index < 0 || index >= list.Count)
            return;

        list.RemoveAt(index);
        ApplyInteractions("Delete Interaction", list, -1);
    }

    /// <summary>
    /// 구조 변경 공통 경로: Undo 기록 → C# 배열 교체 → SerializedObject 갱신 → 뷰 재구성 (스크롤 유지).
    /// scrollToCard: 바뀐 뒤 보여 줄 카드 순번 (없으면 -1).
    /// </summary>
    private void ApplyInteractions(string undoName, List<InteractionDefinition> list, int scrollToCard)
    {
        Undo.RegisterCompleteObjectUndo(_selected, undoName);
        _selected.Set.Interactions = list.ToArray();
        _selectedObject.Update();

        // 카드 순번이 바뀌면 선택해 둔 노드 경로(…data[i]…)가 다른 노드를 가리키게 된다 — 선택을 풀어 엉뚱한 노드를 고치지 않게 한다
        _selectedNodePath = null;
        RebuildSetView(true, scrollToCard);
        ShowNodeInspector(null);
        OnSelectedValueChanged();
    }
    #endregion

    #region 노드 파라미터
    private void OnNodeSelected(SerializedObject so, string propertyPath)
    {
        _selectedNodePath = propertyPath;
        ShowNodeInspector(propertyPath);
    }

    private void ShowNodeInspector(string propertyPath)
    {
        if (_inspectorPane == null)
            return;

        _inspectorPane.Unbind();
        _inspectorPane.Clear();
        _inspectorPane.Add(MakeHeader("노드 파라미터"));

        if (propertyPath == null || _selectedObject == null)
        {
            _inspectorPane.Add(Hint("노드를 선택하면 파라미터가 표시됩니다"));
            return;
        }

        _inspectorPane.Add(InteractionNodeInspector.Build(_selectedObject, propertyPath, OnNodeStructureChanged, BuildStateLinkedField));
    }

    /// <summary>파라미터 패널에서 중첩 노드의 타입을 바꾸거나 비웠을 때.</summary>
    private void OnNodeStructureChanged()
    {
        OnSelectedValueChanged();
        ShowNodeInspector(_selectedNodePath); // 중첩 노드 타입이 바뀌면 칸 구성이 달라지므로 다시 만든다
    }

    /// <summary>
    /// 노드 행과 파라미터 패널 밖을 누르면 노드 선택을 해제한다.
    /// 선택은 UI 상태일 뿐이라 값·Undo에는 영향이 없다.
    /// </summary>
    private void OnRootPointerDown(PointerDownEvent evt)
    {
        if (_selectedNodePath == null)
            return;

        var target = evt.target as VisualElement;
        for (VisualElement e = target; e != null; e = e.parent)
        {
            if (e == _inspectorPane)
                return; // 파라미터 편집 중
            if (e.ClassListContains(InteractionCardView.NodeRowClass))
                return; // 다른 노드 선택 중
        }

        ClearNodeSelection();
    }

    private void ClearNodeSelection()
    {
        for (int i = 0; i < _cardViews.Count; i++)
            _cardViews[i].ClearNodeSelection();

        _selectedNodePath = null;
        ShowNodeInspector(null);
    }
    #endregion

    #region 변경 추적 · Undo
    /// <summary>선택 Set의 값이 바뀔 때마다 (필드 편집·노드 추가·삭제·드래그·구조 변경): 미저장 판정, 목록 표시, 검사 예약.</summary>
    private void OnSelectedValueChanged()
    {
        if (_selected == null)
            return;

        if (_selected.RefreshDirty())
            UpdateUnsavedFlag();
        _setList?.RefreshItems();
        ScheduleValidation();
    }

    /// <summary>
    /// Undo는 선택하지 않은 Set을 되돌릴 수도 있어 모든 Set의 미저장 판정을 다시 한다.
    /// 카드 수나 순서가 되돌려졌을 수 있으므로 카드도 다시 만든다 (스크롤 위치 유지).
    /// </summary>
    private void OnUndoRedo()
    {
        bool changed = false;
        for (int i = 0; i < _assets.Count; i++)
        {
            if (_assets[i] != null)
                changed |= _assets[i].RefreshDirty();
        }
        if (changed)
            UpdateUnsavedFlag();

        if (_selected != null && _selectedObject != null)
        {
            _selectedObject.Update();
            RebuildSetView(true, -1);
            ShowNodeInspector(_selectedNodePath); // 선택했던 노드가 사라졌으면 패널이 그 사실을 알려 준다
        }
        RefreshSetList();
        ScheduleValidation();
    }

    private void UpdateUnsavedFlag()
    {
        hasUnsavedChanges = CountDirty() > 0;
        saveChangesMessage = "저장하지 않은 Interaction Set 변경사항이 있습니다.";
    }

    private int CountDirty()
    {
        int count = 0;
        for (int i = 0; i < _assets.Count; i++)
        {
            if (_assets[i] != null && _assets[i].IsDirty)
                count++;
        }
        return count;
    }
    #endregion

    #region 도우미
    private static string SetLabel(InteractionSetAsset asset)
    {
        if (string.IsNullOrEmpty(asset.Set.Id) == false)
            return asset.Set.Id;
        return asset.FilePath != null ? Path.GetFileName(asset.FilePath) : "(새 Set)";
    }

    public static Label MakeHeader(string text)
    {
        var label = new Label(text);
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.paddingLeft = 6;
        label.style.paddingTop = 4;
        label.style.paddingBottom = 4;
        label.style.borderBottomWidth = 1;
        label.style.borderBottomColor = new Color(0f, 0f, 0f, 0.35f);
        return label;
    }

    private static Label Hint(string text)
    {
        var label = new Label(text);
        label.style.paddingLeft = 8;
        label.style.paddingTop = 8;
        label.style.opacity = 0.6f;
        label.style.whiteSpace = WhiteSpace.Normal;
        return label;
    }
    #endregion
}