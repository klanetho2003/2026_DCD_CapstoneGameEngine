using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 기획자용 상태 정의 편집 툴. Tools > State Definition Editor. 도킹 가능.
/// 레이아웃: [파일 목록] | [선택 파일의 키·규칙] | [키 사용처]  /  하단 [검증]
/// 값 편집은 SerializedObject 바인딩(Undo 자동)에 맡기고, 미저장 판정은 "불러온 때의 JSON과 지금 JSON 비교"로 한다.
///
/// 파일 구성 (partial)
///   StateDefinitionEditorWindow.cs             창 골격 · 파일 목록 · 선택 · Undo
///   StateDefinitionEditorWindow.Save.cs        불러오기 · 새 파일 · 저장
///   StateDefinitionEditorWindow.Validation.cs  실시간 검사 · 검증 목록
/// </summary>
public sealed partial class StateDefinitionEditorWindow : EditorWindow
{
    public const string FolderPrefKey = "StateDefinitionEditor.Folder";
    public const string DefaultFolder = "Assets/@Resources/Data/JsonData";
    private const string SetIdPath = "Set.Id";

    private enum ETab { Keys = 0, Rules = 1 }

    /// <summary>파싱에 실패한 상태 정의 파일 — 목록에 보여 주기만 하고 편집할 수 없다.</summary>
    private sealed class BrokenFile
    {
        public string FilePath;
        public string Error;
    }

    private static readonly Color ErrorColor = new Color(1f, 0.45f, 0.45f);
    private static readonly Color WarningColor = new Color(1f, 0.82f, 0.4f);
    private static readonly Color StatusColor = new Color(0.7f, 0.7f, 0.7f);

    private string _folder;
    private readonly List<StateDefinitionAsset> _assets = new List<StateDefinitionAsset>();
    private readonly List<BrokenFile> _brokenFiles = new List<BrokenFile>();
    private readonly List<object> _visible = new List<object>(); // 검색을 거친 목록 항목 (StateDefinitionAsset 또는 BrokenFile)
    private string _search = "";
    private int _skippedOtherJson;

    private StateDefinitionAsset _selected;
    private BrokenFile _selectedBroken;
    private SerializedObject _selectedObject;

    // 도메인 리로드(스크립트 컴파일) 뒤에도 선택과 탭을 복원하기 위해 창에 직렬화한다
    [SerializeField] private string _selectedPath;
    [SerializeField] private ETab _tab = ETab.Keys;

    // UI
    private Label _folderLabel;
    private Label _status;
    private ListView _fileList;
    private VisualElement _header;
    private ToolbarToggle _keysTab;
    private ToolbarToggle _rulesTab;
    private ToolbarButton _addKeyButton;
    private VisualElement _content;
    private StateKeyTableView _keyTable;

    [MenuItem("Tools/State Definition Editor")]
    public static void Open()
    {
        StateDefinitionEditorWindow window = GetWindow<StateDefinitionEditorWindow>();
        window.titleContent = new GUIContent("State Definition Editor");
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
        RescanInteractions(false);
    }

    private void CreateGUI()
    {
        VisualElement root = rootVisualElement;
        root.Add(BuildToolbar());

        // 가로 3분할 (목록 | 키·규칙 | 키 사용처) 을 세로 분할 (본문 / 검증) 위에 올린다
        var horizontalOuter = new TwoPaneSplitView(0, 240, TwoPaneSplitViewOrientation.Horizontal);
        var horizontalInner = new TwoPaneSplitView(1, 280, TwoPaneSplitViewOrientation.Horizontal);
        horizontalOuter.Add(BuildFilePane());
        horizontalOuter.Add(horizontalInner);
        horizontalInner.Add(BuildCenterPane());
        horizontalInner.Add(BuildInspectorPane());

        var vertical = new TwoPaneSplitView(1, 150, TwoPaneSplitViewOrientation.Vertical);
        vertical.Add(horizontalOuter);
        vertical.Add(BuildValidationPane());
        vertical.style.flexGrow = 1;
        root.Add(vertical);

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

        bar.Add(new ToolbarButton(PickFolder) { text = "폴더", tooltip = "상태 정의 JSON이 있는 폴더 지정 (Assets 아래)" });
        _folderLabel = new Label(_folder);
        _folderLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
        _folderLabel.style.marginLeft = 4;
        _folderLabel.style.marginRight = 12;
        _folderLabel.style.opacity = 0.7f;
        bar.Add(_folderLabel);

        bar.Add(new ToolbarButton(Reload) { text = "새로고침", tooltip = "디스크에서 다시 읽기" });
        bar.Add(new ToolbarButton(CreateNewFile) { text = "+ 새 파일" });
        bar.Add(new ToolbarButton(SaveSelected) { text = "저장" });
        bar.Add(new ToolbarButton(SaveAll) { text = "모두 저장" });
        bar.Add(new ToolbarSpacer());

        var search = new ToolbarSearchField { tooltip = "파일 id·파일 이름으로 찾기" };
        search.RegisterValueChangedCallback(e =>
        {
            _search = e.newValue ?? "";
            RefreshFileList();
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
        string absolute = EditorUtility.OpenFolderPanel("상태 정의 JSON 폴더", _folder, "");
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
        RescanInteractions(true);
        RunValidationNow();
        SetStatus(LoadSummary());
    }

    private bool ConfirmDiscard(string action)
    {
        int dirty = CountDirty();
        if (dirty == 0)
            return true;

        return EditorUtility.DisplayDialog("저장하지 않은 변경",
            $"{action} 저장하지 않은 파일 {dirty}개의 변경이 사라집니다.", "버리고 진행", "취소");
    }

    private void SetStatus(string text, bool isError = false)
    {
        if (_status == null)
            return;
        _status.text = text;
        _status.style.color = isError ? ErrorColor : StatusColor;
    }
    #endregion

    #region 파일 목록
    private VisualElement BuildFilePane()
    {
        var pane = new VisualElement();
        pane.Add(MakeHeader("상태 정의 파일"));

        _fileList = new ListView
        {
            itemsSource = _visible,
            fixedItemHeight = 22,
            selectionType = SelectionType.Single,
            makeItem = MakeFileRow,
            bindItem = BindFileRow,
        };
        _fileList.selectionChanged += OnFileSelectionChanged;
        _fileList.style.flexGrow = 1;
        pane.Add(_fileList);
        return pane;
    }

    private static VisualElement MakeFileRow()
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

    private void BindFileRow(VisualElement element, int index)
    {
        Label main = element.Q<Label>("main");
        Label file = element.Q<Label>("file");
        object item = _visible[index];

        if (item is StateDefinitionAsset asset)
        {
            string badge = asset.ErrorCount > 0 ? $"   ✕{asset.ErrorCount}" : asset.WarningCount > 0 ? $"   ⚠{asset.WarningCount}" : "";
            main.text = (asset.IsDirty ? "* " : "") + FileLabel(asset) + badge;
            file.text = asset.FilePath != null ? Path.GetFileName(asset.FilePath) : "새 파일";
            element.tooltip = asset.FilePath ?? "저장하지 않은 새 파일";
            if (asset.ErrorCount > 0)
                main.style.color = ErrorColor;
            else
                main.style.color = StyleKeyword.Null;
        }
        else if (item is BrokenFile broken)
        {
            main.text = "✕ 파싱 실패";
            file.text = Path.GetFileName(broken.FilePath);
            element.tooltip = broken.Error;
            main.style.color = ErrorColor;
        }
    }

    private void RefreshFileList()
    {
        if (_fileList == null)
            return;

        _visible.Clear();
        for (int i = 0; i < _assets.Count; i++)
        {
            StateDefinitionAsset asset = _assets[i];
            if (Matches(asset.Set.Id) || Matches(asset.FilePath != null ? Path.GetFileName(asset.FilePath) : null))
                _visible.Add(asset);
        }
        for (int i = 0; i < _brokenFiles.Count; i++)
        {
            if (Matches(Path.GetFileName(_brokenFiles[i].FilePath)))
                _visible.Add(_brokenFiles[i]);
        }
        _fileList.RefreshItems();

        object current = _selected != null ? _selected : (object)_selectedBroken;
        int selectedIndex = current != null ? _visible.IndexOf(current) : -1;
        _fileList.SetSelectionWithoutNotify(selectedIndex >= 0 ? new[] { selectedIndex } : Array.Empty<int>());
    }

    private bool Matches(string text)
    {
        return _search.Length == 0 || (text != null && text.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private void OnFileSelectionChanged(IEnumerable<object> selection)
    {
        foreach (object item in selection)
        {
            if (item is StateDefinitionAsset asset)
                SelectAsset(asset);
            else if (item is BrokenFile broken)
                SelectBroken(broken);
            return;
        }
    }

    private void SelectAsset(StateDefinitionAsset asset)
    {
        DisposeSelection();
        _selected = asset;
        _selectedBroken = null;
        _selectedPath = asset != null ? asset.FilePath : null;
        _selectedObject = asset != null ? new SerializedObject(asset) : null;
        RebuildCenter();
        ApplyIssuesToViews();
    }

    private void SelectBroken(BrokenFile broken)
    {
        DisposeSelection();
        _selected = null;
        _selectedBroken = broken;
        _selectedPath = broken?.FilePath;
        RebuildCenter();
    }

    /// <summary>경로로 다시 선택한다 (새로고침·도메인 리로드 뒤). 못 찾으면 선택 없음.</summary>
    private void SelectByPath(string path)
    {
        StateDefinitionAsset asset = null;
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
            SelectAsset(asset);
        RefreshFileList();
    }

    /// <summary>바인딩된 UI를 먼저 풀고 비운 뒤 SerializedObject를 정리한다 (반대 순서면 정리된 객체를 UI가 읽으려 한다).</summary>
    private void DisposeSelection()
    {
        ClearCenter();
        _selectedObject?.Dispose();
        _selectedObject = null;
    }
    #endregion

    #region 가운데 (키·규칙)
    private VisualElement BuildCenterPane()
    {
        var pane = new VisualElement();

        _header = new VisualElement();
        _header.style.flexDirection = FlexDirection.Row;
        _header.style.alignItems = Align.Center;
        _header.style.paddingLeft = 6;
        _header.style.paddingRight = 6;
        _header.style.paddingTop = 4;
        _header.style.paddingBottom = 4;
        pane.Add(_header);

        var tabBar = new Toolbar();
        _keysTab = new ToolbarToggle { text = "키" };
        _keysTab.RegisterValueChangedCallback(_ => SwitchTab(ETab.Keys));
        tabBar.Add(_keysTab);
        _rulesTab = new ToolbarToggle { text = "규칙" };
        _rulesTab.RegisterValueChangedCallback(_ => SwitchTab(ETab.Rules));
        tabBar.Add(_rulesTab);

        var spacer = new ToolbarSpacer();
        spacer.style.flexGrow = 1;
        tabBar.Add(spacer);

        _addKeyButton = new ToolbarButton(() => _keyTable?.AddKey()) { text = "+ 키" };
        tabBar.Add(_addKeyButton);
        pane.Add(tabBar);

        _content = new VisualElement();
        _content.style.flexGrow = 1;
        pane.Add(_content);
        return pane;
    }

    private void ClearCenter()
    {
        _keyTable = null;
        _ruleList = null;
        if (_content == null)
            return;

        _header.Unbind();
        _header.Clear();
        _content.Unbind();
        _content.Clear();
    }

    private void RebuildCenter()
    {
        if (_content == null)
            return;

        ClearCenter();
        if (_selectedBroken != null)
            BuildBrokenView();
        else if (_selected != null)
            BuildAssetView();
        else
            _content.Add(Hint("왼쪽에서 파일을 선택하세요"));

        UpdateTabBar();
    }

    private void BuildAssetView()
    {
        _selectedObject.Update();

        var idField = new TextField("파일 id")
        {
            bindingPath = SetIdPath,
            isDelayed = true,
            tooltip = "검증·로그 문장에 쓰이는 이름 (예: core.rules[0]). 바꿔도 파일 이름은 그대로다",
        };
        idField.style.flexGrow = 1;
        _header.Add(idField);

        var fileName = new Label(_selected.FilePath != null ? Path.GetFileName(_selected.FilePath) : "새 파일 — 저장하면 state_{id}.json");
        fileName.style.marginLeft = 8;
        fileName.style.opacity = 0.6f;
        _header.Add(fileName);
        _header.Bind(_selectedObject);

        // 탭 내용. 컨테이너를 매번 새로 만들어 TrackSerializedObjectValue 등록이 누적되지 않게 한다
        var body = new VisualElement();
        body.style.flexGrow = 1;
        if (_tab == ETab.Keys)
        {
            _keyTable = new StateKeyTableView(_selectedObject, MakeUniqueKey, RenameKey, InspectKey);
            body.Add(_keyTable);
        }
        else
        {
            body.Add(BuildRulesTab());
        }
        body.TrackSerializedObjectValue(_selectedObject, _ => OnSelectedValueChanged());
        _content.Add(body);
    }

    private void BuildBrokenView()
    {
        var box = new VisualElement();
        box.style.paddingLeft = 8;
        box.style.paddingRight = 8;
        box.style.paddingTop = 8;

        var title = new Label("JSON 파싱에 실패해 툴에서 편집할 수 없는 파일입니다.");
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = ErrorColor;
        box.Add(title);
        box.Add(new Label(_selectedBroken.FilePath) { style = { opacity = 0.6f, marginBottom = 6 } });

        var error = new TextField { value = _selectedBroken.Error, multiline = true, isReadOnly = true };
        error.style.whiteSpace = WhiteSpace.Normal;
        box.Add(error);

        box.Add(Hint("텍스트 편집기로 고친 뒤 [새로고침]을 누르세요. 흔한 원인: 쉼표 누락, 필드 이름 오타(예: defualt), 없는 enum 이름(예: EveryWeek)."));
        string path = _selectedBroken.FilePath;
        box.Add(new Button(() => OpenInTextEditor(path)) { text = "텍스트 편집기로 열기" });
        _content.Add(box);
    }

    private static void OpenInTextEditor(string path)
    {
        var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        if (textAsset != null)
            AssetDatabase.OpenAsset(textAsset);
        else
            EditorUtility.OpenWithDefaultApp(path);
    }

    private void UpdateTabBar()
    {
        if (_keysTab == null)
            return;

        bool hasAsset = _selected != null;
        _keysTab.SetEnabled(hasAsset);
        _rulesTab.SetEnabled(hasAsset);
        _keysTab.text = hasAsset ? $"키 ({_selected.Set.Keys.Length})" : "키";
        _rulesTab.text = hasAsset ? $"규칙 ({_selected.Set.Rules.Length})" : "규칙";
        _keysTab.SetValueWithoutNotify(_tab == ETab.Keys);
        _rulesTab.SetValueWithoutNotify(_tab == ETab.Rules);
        _addKeyButton.style.display = hasAsset && _tab == ETab.Keys ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void SwitchTab(ETab tab)
    {
        if (tab == _tab)
        {
            UpdateTabBar(); // 열린 탭을 다시 눌러 꺼진 토글을 되돌린다
            return;
        }
        _tab = tab;
        RebuildCenter();
        ApplyIssuesToViews();
    }
    #endregion

    #region 변경 추적 · Undo
    /// <summary>선택 파일의 값이 바뀔 때마다 (편집·추가·삭제·드래그·Undo 모두): 미저장 표시, 개수, 검사 예약.</summary>
    private void OnSelectedValueChanged()
    {
        if (_selected == null)
            return;

        if (_selected.RefreshDirty())
            UpdateUnsavedFlag();
        _fileList?.RefreshItems();
        UpdateTabBar();
        ScheduleValidation();
    }

    /// <summary>Undo는 선택하지 않은 파일을 되돌릴 수도 있어 모든 파일의 미저장 판정을 다시 한다.</summary>
    private void OnUndoRedo()
    {
        _selectedObject?.Update();

        bool changed = false;
        for (int i = 0; i < _assets.Count; i++)
        {
            if (_assets[i] != null)
                changed |= _assets[i].RefreshDirty();
        }
        if (changed)
            UpdateUnsavedFlag();

        _fileList?.RefreshItems();
        UpdateTabBar();
        ScheduleValidation();
    }

    private void UpdateUnsavedFlag()
    {
        hasUnsavedChanges = CountDirty() > 0;
        saveChangesMessage = "저장하지 않은 상태 정의 변경이 있습니다.";
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
    /// <summary>새 키 이름 — 모든 파일과 시스템 키를 통틀어 겹치지 않게 뒤에 숫자를 붙인다.</summary>
    private string MakeUniqueKey(string baseName)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);

        var systemKeys = new StateKeyRegistry();
        GameStateManager.RegisterSystemKeys(systemKeys);
        for (int i = 0; i < systemKeys.Count; i++)
            used.Add(systemKeys.GetDefinition(i).Key);

        for (int a = 0; a < _assets.Count; a++)
        {
            StateKeyDefinition[] keys = _assets[a].Set.Keys;
            for (int k = 0; k < keys.Length; k++)
            {
                if (keys[k] != null && keys[k].Key != null)
                    used.Add(keys[k].Key);
            }
        }

        if (used.Contains(baseName) == false)
            return baseName;
        for (int n = 2; ; n++)
        {
            string candidate = baseName + n;
            if (used.Contains(candidate) == false)
                return candidate;
        }
    }

    private static string FileLabel(StateDefinitionAsset asset)
    {
        if (string.IsNullOrEmpty(asset.Set.Id) == false)
            return asset.Set.Id;
        return asset.FilePath != null ? Path.GetFileName(asset.FilePath) : "(새 파일)";
    }

    private static Label MakeHeader(string text)
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