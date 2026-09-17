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
/// </summary>
public sealed class InteractionEditorWindow : EditorWindow
{
    private const string FolderPrefKey = "InteractionEditor.Folder";
    private const string DefaultFolder = "Assets/@Resources/Data/Interactions";
    private const string SetInteractionsPath = "Set.Interactions";

    private string _folder;
    private readonly List<InteractionSetAsset> _assets = new List<InteractionSetAsset>();
    private readonly List<InteractionSetAsset> _visible = new List<InteractionSetAsset>();
    private string _search = "";

    private InteractionSetAsset _selected;
    private SerializedObject _selectedObject;

    // UI 참조
    private ListView _setList;
    private VisualElement _setHeader;
    private ScrollView _cardScroll;
    private VisualElement _inspectorPane;   // 5b
    private VisualElement _validationPane;  // 5b
    private Label _folderLabel;
    private Label _status;

    private static readonly Color ErrorColor = new Color(1f, 0.45f, 0.45f);
    private static readonly Color WarningColor = new Color(1f, 0.82f, 0.4f);

    private struct ValidationEntry
    {
        public string Message;
        public bool IsError;
        public int CardIndex;
        public InteractionSetAsset Asset;
    }
    private readonly List<ValidationEntry> _validationEntries = new List<ValidationEntry>();
    private readonly List<string> _errorScratch = new List<string>();
    private readonly List<string> _warningScratch = new List<string>();
    private readonly List<VisualElement> _cardViews = new List<VisualElement>();
    private ListView _validationList;
    private string _selectedNodePath;


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
        DestroyAssets();
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
        _inspectorPane.Add(MakeHeader("노드 파라미터"));
        _inspectorPane.Add(new Label("노드를 선택하면 파라미터가 표시됩니다") { style = { paddingLeft = 8, paddingTop = 4, opacity = 0.6f } });
        horizontalInner.Add(_inspectorPane);

        var vertical = new TwoPaneSplitView(1, 120, TwoPaneSplitViewOrientation.Vertical);
        vertical.Add(horizontalOuter);
        _validationPane = BuildValidationPane();
        vertical.Add(_validationPane);
        vertical.style.flexGrow = 1;
        root.Add(vertical);
        root.RegisterCallback<PointerDownEvent>(OnRootPointerDown, TrickleDown.TrickleDown);

        RefreshSetList();
    }
    #endregion

    #region 툴바
    private VisualElement BuildToolbar()
    {
        var bar = new Toolbar();

        bar.Add(new ToolbarButton(PickFolder) { text = "폴더" });
        _folderLabel = new Label(_folder);
        _folderLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
        _folderLabel.style.marginLeft = 4;
        _folderLabel.style.marginRight = 12;
        _folderLabel.style.opacity = 0.7f;
        bar.Add(_folderLabel);

        bar.Add(new ToolbarButton(() => { LoadFolder(); RefreshSetList(); }) { text = "새로고침" });
        bar.Add(new ToolbarButton(CreateNewSet) { text = "+ 새 Set" });
        bar.Add(new ToolbarButton(SaveSelected) { text = "저장" });
        bar.Add(new ToolbarButton(SaveAll) { text = "모두 저장" });
        bar.Add(new ToolbarSpacer());

        var search = new ToolbarSearchField();
        search.RegisterValueChangedCallback(e => { _search = e.newValue ?? ""; RefreshSetList(); });
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
        if (string.IsNullOrEmpty(absolute)) return;

        string dataPath = Application.dataPath.Replace('\\', '/');
        absolute = absolute.Replace('\\', '/');
        if (absolute.StartsWith(dataPath) == false)
        {
            SetStatus("프로젝트 Assets 아래 폴더만 지정할 수 있습니다", isError: true);
            return;
        }

        _folder = "Assets" + absolute.Substring(dataPath.Length);
        EditorPrefs.SetString(FolderPrefKey, _folder);
        _folderLabel.text = _folder;
        LoadFolder();
        RefreshSetList();
    }

    private void SaveSelected()
    {
        if (_selected == null) { SetStatus("선택된 Set이 없습니다", true); return; }
        if (SaveAsset(_selected)) SetStatus($"{_selected.Set.Id} 저장 완료");
    }
    private void SaveAll()
    {
        int saved = 0, failed = 0;
        for (int i = 0; i < _assets.Count; i++)
        {
            if (_assets[i].IsDirty == false) continue;
            if (SaveAsset(_assets[i])) saved++; else failed++;
        }
        SetStatus(failed > 0 ? $"{saved}건 저장, {failed}건 실패 (검증 패널 확인)" : $"{saved}건 저장 완료", failed > 0);
    }

    /// <summary>에러가 있으면 저장하지 않는다 — 런타임 Parse가 거부할 파일을 만들지 않기 위함.</summary>
    private bool SaveAsset(InteractionSetAsset asset)
    {
        _validationEntries.Clear();
        bool ok = Collect(asset);
        _validationList?.RefreshItems();

        if (ok == false)
        {
            SetStatus($"{asset.Set.Id}: 에러가 있어 저장하지 않았습니다", true);
            return false;
        }

        for (int i = 0; i < _assets.Count; i++)
        {
            if (_assets[i] == asset || _assets[i].Set.Id != asset.Set.Id) continue;
            SetStatus($"Set Id 중복 '{asset.Set.Id}' — 다른 Set과 같습니다", true);
            return false;
        }

        string targetPath = Path.Combine(_folder, asset.Set.Id + ".json").Replace('\\', '/');
        bool createdOrRemoved = File.Exists(targetPath) == false;

        try
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(targetPath, InteractionLoader.Serialize(asset.Set));

            // Id를 바꿔 저장 경로가 달라졌으면 예전 파일과 meta를 정리한다
            if (string.IsNullOrEmpty(asset.FilePath) == false && asset.FilePath != targetPath && File.Exists(asset.FilePath))
            {
                File.Delete(asset.FilePath);
                if (File.Exists(asset.FilePath + ".meta")) File.Delete(asset.FilePath + ".meta");
                createdOrRemoved = true;
            }

            asset.FilePath = targetPath;
            asset.IsDirty = false;
        }
        catch (Exception e)
        {
            SetStatus($"저장 실패: {e.Message}", true);
            return false;
        }

        // 파일이 새로 생기거나 사라졌을 때만 Refresh.
        // 내용만 바뀐 경우까지 Refresh하면 임포트 → 도메인 리로드로 창 상태가 초기화될 수 있다.
        if (createdOrRemoved)
            AssetDatabase.Refresh();

        _setList?.RefreshItems();
        UpdateUnsavedFlag();
        return true;
    }

    private void OnFocus()
    {
        UpdateUnsavedFlag();
    }

    private void UpdateUnsavedFlag()
    {
        bool any = false;
        for (int i = 0; i < _assets.Count; i++)
        {
            if (_assets[i] != null && _assets[i].IsDirty) { any = true; break; }
        }

        // 미러가 리로드로 사라졌으면 저장할 것도 없다 — 플래그만 남아 다이얼로그가 뜨는 상황을 막는다
        if (_assets.Count == 0) any = false;

        hasUnsavedChanges = any;
        saveChangesMessage = "저장하지 않은 Interaction Set 변경사항이 있습니다.";
    }

    public override void SaveChanges()
    {
        SaveAll();
        if (hasUnsavedChanges == false)
            base.SaveChanges();
    }

    public override void DiscardChanges()
    {
        LoadFolder();
        RefreshSetList();
        hasUnsavedChanges = false;
        base.DiscardChanges();
    }

    private void SetStatus(string text, bool isError = false)
    {
        _status.text = text;
        _status.style.color = isError ? new Color(1f, 0.4f, 0.4f) : new Color(0.7f, 0.7f, 0.7f);
    }
    #endregion

    #region 로드
    private void LoadFolder()
    {
        DestroyAssets();
        SelectSet(null);

        if (Directory.Exists(_folder) == false)
            return;

        string[] files = Directory.GetFiles(_folder, "*.json", SearchOption.TopDirectoryOnly);
        for (int i = 0; i < files.Length; i++)
        {
            string path = files[i].Replace('\\', '/');
            string json = File.ReadAllText(path);
            InteractionSetDefinition set = InteractionLoader.ParseRaw(json, out string error);
            if (set == null)
            {
                Debug.LogError($"[InteractionEditor] 파싱 실패 >> {path}\n{error}");
                continue; // 구조 자체가 깨진 파일은 텍스트 편집기로 고쳐야 한다
            }
            if (string.IsNullOrEmpty(set.Id))
                set.Id = Path.GetFileNameWithoutExtension(path);

            _assets.Add(InteractionSetAsset.Create(set, path));
        }
        _assets.Sort((a, b) => string.CompareOrdinal(a.Set.Id, b.Set.Id));
    }

    private void DestroyAssets()
    {
        for (int i = 0; i < _assets.Count; i++)
            if (_assets[i] != null) DestroyImmediate(_assets[i]);
        _assets.Clear();
    }
    #endregion

    #region Set 목록
    private VisualElement BuildSetPane()
    {
        var pane = new VisualElement();
        pane.Add(MakeHeader("Interaction Sets"));

        _setList = new ListView
        {
            fixedItemHeight = 22,
            selectionType = SelectionType.Single,
            itemsSource = _visible,
        };
        _setList.makeItem = () =>
        {
            var label = new Label();
            label.style.paddingLeft = 8;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            return label;
        };
        _setList.bindItem = (element, i) =>
        {
            InteractionSetAsset asset = _visible[i];
            ((Label)element).text = (asset.IsDirty ? "* " : "") + asset.Set.Id;
        };
        _setList.selectionChanged += selection =>
        {
            foreach (object item in selection) { SelectSet(item as InteractionSetAsset); return; }
        };
        _setList.style.flexGrow = 1;
        pane.Add(_setList);
        return pane;
    }

    private void RefreshSetList()
    {
        if (_setList == null) return;

        _visible.Clear();
        for (int i = 0; i < _assets.Count; i++)
        {
            InteractionSetAsset asset = _assets[i];
            if (_search.Length == 0 || asset.Set.Id.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                _visible.Add(asset);
        }
        _setList.RefreshItems();

        int selectedIndex = _selected != null ? _visible.IndexOf(_selected) : -1;
        _setList.SetSelectionWithoutNotify(selectedIndex >= 0 ? new[] { selectedIndex } : Array.Empty<int>());
    }

    private void CreateNewSet()
    {
        string id = "new_set";
        int suffix = 1;
        while (_assets.Exists(a => a.Set.Id == id)) id = $"new_set_{suffix++}";

        var set = new InteractionSetDefinition { Id = id, Interactions = Array.Empty<InteractionDefinition>() };
        InteractionSetAsset asset = InteractionSetAsset.Create(set, Path.Combine(_folder, id + ".json").Replace('\\', '/'));
        asset.IsDirty = true;
        _assets.Add(asset);

        RefreshSetList();
        SelectSet(asset);
        _setList.SetSelectionWithoutNotify(new[] { _visible.IndexOf(asset) });
    }

    /// <summary>
    /// 노드 행과 파라미터 패널 밖을 누르면 노드 선택을 해제한다.
    /// 선택은 UI 상태일 뿐이라 값·Undo에는 영향이 없다.
    /// </summary>
    private void OnRootPointerDown(PointerDownEvent evt)
    {
        if (_selectedNodePath == null) return;

        var target = evt.target as VisualElement;
        for (VisualElement e = target; e != null; e = e.parent)
        {
            if (e == _inspectorPane) return;                                 // 파라미터 편집 중
            if (e.ClassListContains(InteractionCardView.NodeRowClass)) return; // 다른 노드 선택 중
        }

        ClearNodeSelection();
    }

    private void ClearNodeSelection()
    {
        for (int i = 0; i < _cardViews.Count; i++)
            (_cardViews[i] as InteractionCardView)?.ClearNodeSelection();

        _selectedNodePath = null;
        ShowNodeInspector(null);
    }
    #endregion

    #region 카드 영역
    private VisualElement BuildCenterPane()
    {
        var pane = new VisualElement();
        _setHeader = new VisualElement();
        _setHeader.style.flexDirection = FlexDirection.Row;
        _setHeader.style.paddingLeft = 6;
        _setHeader.style.paddingTop = 4;
        _setHeader.style.paddingBottom = 4;
        pane.Add(_setHeader);

        _cardScroll = new ScrollView(ScrollViewMode.Vertical);
        _cardScroll.style.flexGrow = 1;
        pane.Add(_cardScroll);
        return pane;
    }

    private void SelectSet(InteractionSetAsset asset)
    {
        _selected = asset;
        _selectedObject = asset != null ? new SerializedObject(asset) : null;
        RebuildSetView();

        _selectedNodePath = null;
        ShowNodeInspector(null);
        RunValidation(false);
    }

    private void RebuildSetView()
    {
        if (_setHeader == null) return;
        _setHeader.Clear();
        _cardScroll.Clear();

        if (_selected == null)
        {
            _cardScroll.Add(new Label("왼쪽에서 Set을 선택하세요") { style = { paddingLeft = 8, paddingTop = 8, opacity = 0.6f } });
            return;
        }

        _selectedObject.Update();

        var idField = new TextField("Set Id") { bindingPath = "Set.Id" };
        idField.style.flexGrow = 1;
        idField.RegisterValueChangedCallback(e =>
        {
            if (_selected == null) return;
            _selected.name = e.newValue; // 목록 라벨은 Set.Id를 읽으므로 표시만 갱신하면 된다
            _setList.RefreshItems();
        });
        _setHeader.Add(idField);
        _setHeader.Add(new Button(AddInteraction) { text = "+ Interaction" });
        _setHeader.Bind(_selectedObject);

        // 카드 컨테이너를 매번 새로 만들어 TrackSerializedObjectValue 등록이 누적되지 않게 한다
        var container = new VisualElement();
        _cardViews.Clear();
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
        container.TrackSerializedObjectValue(_selectedObject, _ => MarkDirty());
        _cardScroll.Add(container);
    }

    /// <summary>구조 변경 공통 경로: Undo 기록 → C# 배열 교체 → SerializedObject 갱신 → 뷰 재구성</summary>
    private void ModifyInteractions(string undoName, Func<List<InteractionDefinition>, bool> edit)
    {
        var list = new List<InteractionDefinition>(_selected.Set.Interactions);
        if (edit(list) == false) return;

        Undo.RegisterCompleteObjectUndo(_selected, undoName);
        _selected.Set.Interactions = list.ToArray();
        MarkDirty();
        _selectedObject.Update();
        RebuildSetView();
    }

    private void AddInteraction()
    {
        ModifyInteractions("Add Interaction", list =>
        {
            string id = "new_interaction";
            int suffix = 1;
            while (list.Exists(d => d.Id == id)) id = $"new_interaction_{suffix++}";

            list.Add(new InteractionDefinition
            {
                Id = id,
                Trigger = Define.ETriggerType.Tick,
                Mode = Define.EActivationMode.Fire,
                Conditions = Array.Empty<InteractionCondition>(),
                EffectPrototypes = Array.Empty<InteractionEffect>(),
            });
            return true;
        });
    }

    private void MoveInteraction(int index, int delta)
    {
        ModifyInteractions("Move Interaction", list =>
        {
            int target = index + delta;
            if (target < 0 || target >= list.Count) return false;
            (list[index], list[target]) = (list[target], list[index]);
            return true;
        });
    }

    private void DuplicateInteraction(int index)
    {
        ModifyInteractions("Duplicate Interaction", list =>
        {
            InteractionDefinition clone = InteractionLoader.CloneDefinition(list[index]);
            clone.Id = list[index].Id + "_copy";
            list.Insert(index + 1, clone);
            return true;
        });
    }

    private void DeleteInteraction(int index)
    {
        ModifyInteractions("Delete Interaction", list => { list.RemoveAt(index); return true; });
    }

    private void OnNodeSelected(SerializedObject so, string propertyPath)
    {
        _selectedNodePath = propertyPath;
        ShowNodeInspector(propertyPath);
    }

    private void ShowNodeInspector(string propertyPath)
    {
        if (_inspectorPane == null) return;

        _inspectorPane.Clear();
        _inspectorPane.Add(MakeHeader("노드 파라미터"));

        if (propertyPath == null || _selectedObject == null)
        {
            _inspectorPane.Add(new Label("노드를 선택하면 파라미터가 표시됩니다") { style = { paddingLeft = 8, paddingTop = 4, opacity = 0.6f } });
            return;
        }

        _inspectorPane.Add(InteractionNodeInspector.Build(_selectedObject, propertyPath, () =>
        {
            MarkDirty();
            ShowNodeInspector(_selectedNodePath); // 중첩 노드 타입이 바뀌면 뷰를 다시 만든다
            RunValidation(false);
        }));
    }

    private void MarkDirty()
    {
        if (_selected == null || _selected.IsDirty) return;
        _selected.IsDirty = true;
        _setList.RefreshItems();
        UpdateUnsavedFlag();
    }

    private void OnUndoRedo()
    {
        if (_selected != null)
        {
            _selectedObject.Update();
            RebuildSetView();
        }
        RefreshSetList();
    }
    #endregion

    #region 공통 UI 조각
    private VisualElement BuildValidationPane()
    {
        var pane = new VisualElement();

        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.borderBottomWidth = 1;
        header.style.borderBottomColor = new Color(0, 0, 0, 0.35f);

        var title = new Label("검증");
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.paddingLeft = 6;
        title.style.flexGrow = 1;
        header.Add(title);
        header.Add(new Button(() => RunValidation(false)) { text = "현재 Set" });
        header.Add(new Button(() => RunValidation(true)) { text = "전체" });
        pane.Add(header);

        _validationList = new ListView
        {
            fixedItemHeight = 20,
            itemsSource = _validationEntries,
            selectionType = SelectionType.Single,
        };
        _validationList.makeItem = () =>
        {
            var label = new Label();
            label.style.paddingLeft = 8;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            return label;
        };
        _validationList.bindItem = (element, i) =>
        {
            ValidationEntry entry = _validationEntries[i];
            var label = (Label)element;
            label.text = (entry.IsError ? "✕  " : "⚠  ") + entry.Message;
            label.style.color = entry.IsError ? ErrorColor : WarningColor;
        };
        _validationList.selectionChanged += _ => JumpToEntry(_validationList.selectedIndex);
        _validationList.style.flexGrow = 1;
        pane.Add(_validationList);
        return pane;
    }

    private void RunValidation(bool all)
    {
        if (_validationList == null) return;

        _validationEntries.Clear();
        if (all)
        {
            for (int i = 0; i < _assets.Count; i++) Collect(_assets[i]);
        }
        else if (_selected != null)
        {
            Collect(_selected);
        }
        _validationList.RefreshItems();

        int errors = 0;
        for (int i = 0; i < _validationEntries.Count; i++) if (_validationEntries[i].IsError) errors++;

        if (_validationEntries.Count == 0) SetStatus("검증 통과");
        else SetStatus($"에러 {errors}건 / 경고 {_validationEntries.Count - errors}건", errors > 0);
    }

    private bool Collect(InteractionSetAsset asset)
    {
        _errorScratch.Clear();
        _warningScratch.Clear();
        bool ok = InteractionValidator.Validate(asset.Set, _errorScratch, _warningScratch);

        for (int i = 0; i < _errorScratch.Count; i++)
            _validationEntries.Add(new ValidationEntry { Message = _errorScratch[i], IsError = true, CardIndex = ParseCardIndex(_errorScratch[i]), Asset = asset });
        for (int i = 0; i < _warningScratch.Count; i++)
            _validationEntries.Add(new ValidationEntry { Message = _warningScratch[i], IsError = false, CardIndex = ParseCardIndex(_warningScratch[i]), Asset = asset });

        return ok;
    }

    /// <summary>검증 메시지의 "setId[3]: ..." 에서 카드 인덱스를 뽑는다. 실패하면 -1 (점프 없음).</summary>
    private static int ParseCardIndex(string message)
    {
        int open = message.IndexOf('[');
        if (open < 0) return -1;
        int close = message.IndexOf(']', open + 1);
        if (close < 0) return -1;
        return int.TryParse(message.Substring(open + 1, close - open - 1), out int index) ? index : -1;
    }

    private void JumpToEntry(int entryIndex)
    {
        if (entryIndex < 0 || entryIndex >= _validationEntries.Count) return;
        ValidationEntry entry = _validationEntries[entryIndex];

        if (entry.Asset != _selected)
        {
            SelectSet(entry.Asset);
            RefreshSetList();
        }
        if (entry.CardIndex >= 0 && entry.CardIndex < _cardViews.Count)
            _cardScroll.ScrollTo(_cardViews[entry.CardIndex]);
    }

    public static Label MakeHeader(string text)
    {
        var label = new Label(text);
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.paddingLeft = 6;
        label.style.paddingTop = 4;
        label.style.paddingBottom = 4;
        label.style.borderBottomWidth = 1;
        label.style.borderBottomColor = new Color(0, 0, 0, 0.35f);
        return label;
    }
    #endregion
}