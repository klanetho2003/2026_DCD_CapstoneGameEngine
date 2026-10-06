using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>오른쪽 패널(키 사용처) · 키 이름 변경.</summary>
public sealed partial class StateDefinitionEditorWindow
{
    private ScrollView _inspectorBody;
    private string _inspectedKey;
    private readonly StateKeyUsageResult _usage = new StateKeyUsageResult();
    private readonly List<StateDefinitionSet> _usageSets = new List<StateDefinitionSet>();

    #region 키 사용처 패널
    private VisualElement BuildInspectorPane()
    {
        var pane = new VisualElement();
        pane.Add(MakeHeader("키 사용처"));

        _inspectorBody = new ScrollView(ScrollViewMode.Vertical);
        _inspectorBody.style.flexGrow = 1;
        pane.Add(_inspectorBody);

        RescanInteractions(true); // [5-B] 패널도 함께 그린다
        return pane;
    }

    /// <summary>키 표의 행 또는 규칙 카드의 require·ops 행이 선택될 때 불린다.</summary>
    private void InspectKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            return;
        _inspectedKey = key;
        RefreshInspector();
    }

    /// <summary>지금 편집 중인 내용 기준의 파일 목록 (순번 = _assets 순번).</summary>
    private List<StateDefinitionSet> CurrentSets()
    {
        _usageSets.Clear();
        for (int i = 0; i < _assets.Count; i++)
            _usageSets.Add(_assets[i].Set);
        return _usageSets;
    }

    /// <summary>선택이 바뀌거나 검사가 끝날 때마다 다시 그린다 (검사는 모든 변경 뒤에 돌므로 내용이 항상 최신).</summary>
    private void RefreshInspector()
    {
        if (_inspectorBody == null)
            return;

        _inspectorBody.Clear();
        if (string.IsNullOrEmpty(_inspectedKey))
        {
            _inspectorBody.Add(Hint("키 표의 행이나 규칙의 선행 조건·값 변경 행을 선택하면, 그 키를 쓰는 곳이 여기에 표시됩니다"));
            return;
        }

        List<StateDefinitionSet> sets = CurrentSets();
        StateKeyUsage.Collect(sets, _inspectedKey, _usage);

        var title = new Label(_inspectedKey);
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.fontSize = 13;
        title.style.paddingLeft = 6;
        title.style.paddingTop = 6;
        title.style.paddingBottom = 2;
        title.style.whiteSpace = WhiteSpace.Normal;
        _inspectorBody.Add(title);

        bool isSystem = StateKeyUsage.IsSystemKey(_inspectedKey, out StateKeyDefinition systemKey);
        if (isSystem)
        {
            _inspectorBody.Add(InfoLine($"시스템 키 — 코드가 등록하고 관리합니다. 규칙은 읽기만 할 수 있습니다.\n{systemKey.Description} · 기본값 {systemKey.Default}"));
        }
        else if (_usage.Declarations.Count == 0)
        {
            Label missing = InfoLine("선언되지 않은 키 — 어느 파일의 키 표에도 없습니다");
            missing.style.color = ErrorColor;
            _inspectorBody.Add(missing);
        }
        else
        {
            for (int i = 0; i < _usage.Declarations.Count; i++)
                AddDeclaration(_usage.Declarations[i], sets);

            if (_usage.Declarations.Count > 1)
            {
                Label duplicate = InfoLine("여러 번 선언됨 — 게임은 먼저 로드된 하나만 등록합니다");
                duplicate.style.color = ErrorColor;
                _inspectorBody.Add(duplicate);
            }
        }

        AddReferences("바꾸는 규칙", _usage.Writers, sets);
        AddReferences("읽는 규칙", _usage.Readers, sets);
        int interactionReads = AddInteractionReads(); // [5-B]

        if (_usage.ReferenceCount == 0 && interactionReads == 0 && isSystem == false && _usage.Declarations.Count > 0)
            _inspectorBody.Add(Hint("이 키를 읽거나 바꾸는 규칙·상호작용이 없습니다"));
    }

    private void AddDeclaration(StateKeyDeclaration declaration, List<StateDefinitionSet> sets)
    {
        StateKeyDefinition definition = sets[declaration.SetIndex].Keys[declaration.KeyIndex];
        int setIndex = declaration.SetIndex;
        int keyIndex = declaration.KeyIndex;

        _inspectorBody.Add(MakeLink($"선언: {FileLabel(_assets[setIndex])} · keys[{keyIndex}]", "키 표의 이 행으로 이동",
            () => JumpToLocation(setIndex, EStateDefinitionSection.Key, keyIndex, -1)));

        string reset = EditorEnumLabels.Of(definition.Reset);
        if (definition.Reset == Define.EStateResetPolicy.EveryNDays)
            reset += $" ({definition.Interval}일)";
        string description = string.IsNullOrEmpty(definition.Description) ? "" : "\n" + definition.Description;
        _inspectorBody.Add(InfoLine($"기본값 {definition.Default} · {reset}{description}"));
    }

    private void AddReferences(string title, List<StateKeyRuleReference> references, List<StateDefinitionSet> sets)
    {
        _inspectorBody.Add(SectionHeader($"{title} ({references.Count})"));

        for (int i = 0; i < references.Count; i++)
        {
            StateKeyRuleReference reference = references[i]; // 람다가 잡을 수 있게 지역 변수로
            StateRuleDefinition rule = sets[reference.SetIndex].Rules[reference.RuleIndex];

            string detail;
            if (reference.IsWrite)
            {
                StateOpDefinition op = rule.Ops[reference.EntryIndex];
                detail = $"{EditorEnumLabels.Of(op.Op)} {op.Value}";
            }
            else
            {
                StateRequireDefinition require = rule.Require[reference.EntryIndex];
                detail = $"{EditorEnumLabels.Of(require.Comparison)} {require.Value}";
            }

            string text = $"{FileLabel(_assets[reference.SetIndex])}.rules[{reference.RuleIndex}]  ·  {EditorEnumLabels.Of(rule.Event)}  ·  {detail}";
            string tooltip = string.IsNullOrEmpty(rule.Description) ? "규칙 카드의 이 행으로 이동" : rule.Description;
            EStateDefinitionSection section = reference.IsWrite ? EStateDefinitionSection.Op : EStateDefinitionSection.Require;

            _inspectorBody.Add(MakeLink(text, tooltip,
                () => JumpToLocation(reference.SetIndex, section, reference.RuleIndex, reference.EntryIndex)));
        }
    }

    private static Label SectionHeader(string text)
    {
        var header = new Label(text);
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.paddingLeft = 6;
        header.style.marginTop = 8;
        return header;
    }

    private static Button MakeLink(string text, string tooltip, System.Action onClick)
    {
        var button = new Button(onClick) { text = text, tooltip = tooltip };
        button.style.unityTextAlign = TextAnchor.MiddleLeft;
        button.style.whiteSpace = WhiteSpace.Normal;
        button.style.marginLeft = 6;
        button.style.marginRight = 6;
        return button;
    }

    private static Label InfoLine(string text)
    {
        var label = new Label(text);
        label.style.paddingLeft = 8;
        label.style.paddingRight = 6;
        label.style.paddingTop = 2;
        label.style.opacity = 0.8f;
        label.style.whiteSpace = WhiteSpace.Normal;
        return label;
    }
    #endregion

    #region 상호작용 참조 [5-B] — 읽기 전용

    private int CountInteractionReads(string key)
    {
        int count = 0;
        for (int i = 0; i < _interactionScan.KeyReads.Count; i++)
        {
            if (_interactionScan.KeyReads[i].Key == key)
                count++;
        }
        return count;
    }

    /// <summary>선택한 키를 읽는 상호작용 목록. 누르면 Project 창에서 그 JSON 파일을 가리킨다. 개수를 돌려준다.</summary>
    private int AddInteractionReads()
    {
        int count = CountInteractionReads(_inspectedKey);
        _inspectorBody.Add(SectionHeader($"읽는 상호작용 ({count})"));

        if (_interactionScan.FolderExists == false)
        {
            _inspectorBody.Add(InfoLine($"상호작용 폴더가 없습니다: {_interactionScan.Folder}"));
            return 0;
        }

        for (int i = 0; i < _interactionScan.KeyReads.Count; i++)
        {
            InteractionKeyRead read = _interactionScan.KeyReads[i];
            if (read.Key != _inspectedKey)
                continue;

            string nested = read.IsNested ? " 안쪽" : "";
            string text = $"{read.SetId} › {read.InteractionId}  ·  조건 {read.ConditionIndex + 1}{nested}  ·  {EditorEnumLabels.Of(read.Op)} {read.Value}";
            string path = read.FilePath;
            _inspectorBody.Add(MakeLink(text, $"{path}\n누르면 Project 창에서 이 파일을 가리킵니다. 수정은 Interaction Editor에서", () => PingFile(path)));
        }

        if (_interactionScan.UnreadableFiles.Count > 0)
        {
            Label unreadable = InfoLine($"읽지 못한 상호작용 파일 {_interactionScan.UnreadableFiles.Count}개 — 그 안의 참조는 여기에 보이지 않습니다");
            unreadable.style.color = WarningColor;
            unreadable.tooltip = string.Join("\n", _interactionScan.UnreadableFiles);
            _inspectorBody.Add(unreadable);
        }
        return count;
    }

    /// <summary>이름 변경 확인 창에 넣을 목록 — "  • merchant › talk (조건 2)" 형태로 최대 max줄.</summary>
    private string DescribeInteractionReads(string key, int max)
    {
        var builder = new StringBuilder();
        int shown = 0;
        int total = 0;
        for (int i = 0; i < _interactionScan.KeyReads.Count; i++)
        {
            InteractionKeyRead read = _interactionScan.KeyReads[i];
            if (read.Key != key)
                continue;

            total++;
            if (shown < max)
            {
                builder.Append("  • ").Append(read.SetId).Append(" › ").Append(read.InteractionId)
                       .Append(" (조건 ").Append(read.ConditionIndex + 1).Append(")\n");
                shown++;
            }
        }
        if (total > shown)
            builder.Append("  … 외 ").Append(total - shown).Append("곳\n");
        return builder.ToString();
    }

    private static void PingFile(string path)
    {
        var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        if (asset != null)
            EditorGUIUtility.PingObject(asset);
    }
    #endregion

    #region 키 이름 변경 (키 표가 호출)
    /// <summary>
    /// 선택 파일의 keys[keyIndex] 이름을 바꾼다. 그 키를 쓰는 규칙이 있으면 함께 바꿀지 묻는다.
    /// 선언과 모든 파일의 참조를 Undo 한 단계로 묶는다 — Ctrl+Z 한 번에 전부 돌아간다.
    /// 상호작용 파일은 바꾸지 않는다 — 그 키를 읽는 상호작용이 있으면 목록을 보여 주고 확인받는다.
    /// </summary>
    private void RenameKey(int keyIndex, string oldKey, string newKey)
    {
        if (_selected == null || oldKey == newKey)
            return;

        StateKeyDefinition[] keys = _selected.Set.Keys;
        if (keyIndex < 0 || keyIndex >= keys.Length || keys[keyIndex] == null)
            return;

        List<StateDefinitionSet> sets = CurrentSets();
        StateKeyUsage.Collect(sets, oldKey, _usage);

        // [5-B] 이 키를 읽는 상호작용 — 이름이 바뀌면 그 Set은 "선언되지 않은 상태 키"로 게임에서 로드되지 않는다
        int interactionReads = CountInteractionReads(oldKey);
        string interactionNote = interactionReads > 0
            ? $"\n\n상호작용 {interactionReads}곳도 이 키를 읽습니다:\n{DescribeInteractionReads(oldKey, 6)}"
              + "상호작용 파일은 이 툴이 바꾸지 않습니다. Interaction Editor에서 키를 직접 고치지 않으면 그 Set은 게임에서 로드되지 않습니다."
            : "";

        bool withReferences = false;
        bool asked = false;
        string note = "";
        if (_usage.ReferenceCount > 0)
        {
            if (_usage.Declarations.Count != 1)
            {
                note = $" — '{oldKey}'가 여러 번 선언되어 있어 규칙의 참조는 바꾸지 않았습니다";
            }
            else if (StateKeyUsage.IsFreeName(sets, newKey, out string reason) == false)
            {
                note = $" — 규칙의 참조는 바꾸지 않았습니다 ({reason})";
            }
            else
            {
                asked = true;
                int choice = EditorUtility.DisplayDialogComplex("키 이름 변경",
                    $"'{oldKey}'  →  '{newKey}'\n\n이 키를 쓰는 규칙 {_usage.ReferenceCount}곳(파일 {_usage.ReferencedFileCount()}개)도 함께 바꿀까요?\n\n"
                    + "'이름만 바꾸기'를 고르면 그 규칙들은 선언되지 않은 키를 가리키게 됩니다."
                    + interactionNote,
                    "함께 바꾸기", "취소", "이름만 바꾸기");

                if (choice == 1) // 취소 — 표의 이름 칸을 원래 글자로 되돌린다
                {
                    _keyTable?.RefreshRowStates();
                    return;
                }
                withReferences = choice == 0;
            }
        }

        // [5-B] 규칙 쪽 질문이 없었더라도, 상호작용이 읽는 키라면 한 번 확인받는다
        if (asked == false && interactionReads > 0)
        {
            bool proceed = EditorUtility.DisplayDialog("키 이름 변경",
                $"'{oldKey}'  →  '{newKey}'{interactionNote}", "이름 바꾸기", "취소");
            if (proceed == false)
            {
                _keyTable?.RefreshRowStates();
                return;
            }
        }

        // 참조가 다른 파일에 있을 수 있으므로 모든 사본을 한 번에 Undo 대상으로 등록한다
        var targets = new Object[_assets.Count];
        for (int i = 0; i < _assets.Count; i++)
            targets[i] = _assets[i];
        Undo.RegisterCompleteObjectUndo(targets, "상태 키 이름 변경");

        keys[keyIndex].Key = newKey;
        int changed = withReferences ? StateKeyUsage.RenameReferences(sets, oldKey, newKey) : 0;

        // C# 객체를 직접 고쳤으므로 화면 쪽(SerializedObject)을 맞추고, 미저장 판정·검사·사용처를 갱신한다
        _selectedObject?.Update();
        if (_inspectedKey == oldKey)
            _inspectedKey = newKey;

        for (int i = 0; i < _assets.Count; i++)
            _assets[i].RefreshDirty();
        UpdateUnsavedFlag();
        RunValidationNow();

        string interactionStatus = interactionReads > 0 ? $" · 상호작용 {interactionReads}곳은 Interaction Editor에서 수정 필요" : "";
        SetStatus(withReferences
            ? $"'{oldKey}' → '{newKey}': 규칙 {changed}곳도 함께 변경 (Ctrl+Z 한 번으로 되돌리기){interactionStatus}"
            : $"'{oldKey}' → '{newKey}'{note}{interactionStatus}", interactionReads > 0);
    }
    #endregion
}