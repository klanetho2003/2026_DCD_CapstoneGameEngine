using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 상태 정의와의 연결 — 상태 키 고르기 · 없는 키 검사 · 신호를 받는 규칙 표시.
/// 상태 파일은 읽기만 한다 (디스크에 저장된 내용 기준). 고치는 곳은 State Definition Editor 하나다.
/// </summary>
public sealed partial class InteractionEditorWindow
{
    private const int MaxReceiversShown = 3; // 이름으로 적는 규칙 수 (넘으면 "외 n곳")

    private readonly StateDefinitionCatalog _stateCatalog = new StateDefinitionCatalog();
    private readonly InteractionReferenceScan _referenceScratch = new InteractionReferenceScan();
    private readonly List<InteractionStateLinkIssue> _linkIssues = new List<InteractionStateLinkIssue>();
    private long _stateSignature;
    private bool _stateScanned; // 한 번이라도 읽었는가

    #region 상태 정의 읽기
    /// <summary>상태 정의 폴더 — State Definition Editor가 고른 폴더를 그대로 따른다 (EditorPrefs 키와 기본값의 원본은 그 창).</summary>
    private static string StateFolder()
    {
        return EditorPrefs.GetString(StateDefinitionEditorWindow.FolderPrefKey, StateDefinitionEditorWindow.DefaultFolder);
    }

    /// <summary>
    /// 상태 정의를 다시 읽고, 결과를 쓰는 화면(파라미터 패널 · 검증 목록)을 갱신한다.
    /// force가 아니면 폴더 내용이 바뀌었을 때만 읽는다 — 창이 포커스를 받을 때마다 불린다.
    /// </summary>
    private void RescanStateDefinitions(bool force)
    {
        string folder = StateFolder();
        long signature = EditorJsonFolder.Signature(folder);
        if (force == false && _stateScanned && folder == _stateCatalog.Folder && signature == _stateSignature)
            return;

        ScanStateDefinitions(folder, signature);

        if (_selectedNodePath != null)
            ShowNodeInspector(_selectedNodePath); // 키 설명·받는 규칙 문구가 달라질 수 있다
        ScheduleValidation();
    }

    private void ScanStateDefinitions(string folder, long signature)
    {
        _stateScanned = true;
        _stateSignature = signature;
        _stateCatalog.ScanFolder(folder);
    }

    /// <summary>화면을 만드는 순서와 상관없이, 결과를 쓰기 전에 한 번은 읽혀 있도록 한다.</summary>
    private void EnsureStateScan()
    {
        if (_stateScanned)
            return;

        string folder = StateFolder();
        ScanStateDefinitions(folder, EditorJsonFolder.Signature(folder));
    }
    #endregion

    #region 검사 (RunValidationNow가 호출)
    /// <summary>이 Set의 상태 연결 문제를 검증 목록에 더한다. 에러면 그 Set의 저장도 막힌다.</summary>
    private void AddStateLinkEntries(InteractionSetAsset asset)
    {
        EnsureStateScan();

        _linkIssues.Clear();
        InteractionStateLinkCheck.Check(asset.Set, _stateCatalog, _referenceScratch, _linkIssues);
        for (int i = 0; i < _linkIssues.Count; i++)
            AddEntry(asset, _linkIssues[i].Message, _linkIssues[i].IsError, _linkIssues[i].InteractionIndex);
    }

    /// <summary>검증 제목에 붙이는 알림 — 상태 연결 확인을 건너뛰었으면 그 사실을 숨기지 않는다.</summary>
    private string StateLinkNotice()
    {
        if (_stateScanned == false)
            return "";
        if (_stateCatalog.FolderExists == false)
            return " · 상태 정의 폴더 없음 — 상태 키·신호 확인 건너뜀";
        if (_stateCatalog.UnreadableFiles.Count > 0)
            return $" · 읽지 못한 상태 파일 {_stateCatalog.UnreadableFiles.Count}개 — 상태 키·신호 확인 건너뜀";
        return "";
    }
    #endregion

    #region 파라미터 패널의 전용 칸 (InteractionNodeInspector가 호출)
    /// <summary>
    /// 상태와 이어진 필드는 전용 칸으로 그린다. 해당 없으면 null — 패널이 기본 칸(PropertyField)을 쓴다.
    /// 상태 정의 폴더를 읽지 못했을 때도 null이다 (고를 목록이 없으므로 직접 입력하는 기본 칸으로 둔다).
    /// </summary>
    private VisualElement BuildStateLinkedField(object node, SerializedProperty field)
    {
        EnsureStateScan();
        if (_stateCatalog.FolderExists == false)
            return null;

        if (node is StateValueCondition && field.name == nameof(StateValueCondition.Key))
            return BuildStateKeyField(field);
        if (node is RaiseSignalEffect && field.name == nameof(RaiseSignalEffect.SignalId))
            return BuildSignalIdField(field);
        return null;
    }

    /// <summary>StateValue 조건의 Key — 직접 입력하지 않고 선언된 키 목록에서 고른다 (오타 방지). 아래에 그 키의 정보를 보여 준다.</summary>
    private VisualElement BuildStateKeyField(SerializedProperty field)
    {
        string path = field.propertyPath;
        string current = field.stringValue;

        var box = new VisualElement();
        box.style.marginTop = 2;
        box.style.marginBottom = 2;

        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;

        var label = new Label(ObjectNames.NicifyVariableName(field.name));
        label.style.minWidth = 60;
        row.Add(label);

        Button pick = null;
        pick = new Button(() => PickStateKey(pick, path))
        {
            bindingPath = path, // 버튼 글자 = 키 값. 패널의 Bind가 묶어 준다
            tooltip = "눌러서 선언된 상태 키 중에서 고르기",
        };
        pick.style.flexGrow = 1;
        pick.style.minHeight = 18;
        pick.style.unityTextAlign = TextAnchor.MiddleLeft;
        row.Add(pick);
        box.Add(row);

        var status = new Label();
        status.style.paddingLeft = 4;
        status.style.whiteSpace = WhiteSpace.Normal;
        if (string.IsNullOrEmpty(current))
        {
            status.text = "키를 고르세요 — 버튼을 누르면 선언된 상태 키 목록이 열립니다";
            status.style.color = WarningColor;
        }
        else if (_stateCatalog.TryGetKey(current, out StateKeyDefinition definition))
        {
            status.text = DescribeStateKey(definition);
            status.style.opacity = 0.7f;
        }
        else if (_stateCatalog.IsComplete)
        {
            status.text = "선언되지 않은 상태 키 — 게임에서 이 Set 전체가 로드되지 않습니다. 다른 키를 고르거나, State Definition Editor에서 이 키를 선언하고 저장하세요";
            status.style.color = ErrorColor;
        }
        else
        {
            status.text = "읽지 못한 상태 파일이 있어 이 키의 선언 여부를 확인하지 못했습니다";
            status.style.color = WarningColor;
        }
        box.Add(status);
        return box;
    }

    private void PickStateKey(VisualElement activator, string propertyPath)
    {
        if (_selectedObject == null)
            return;

        _selectedObject.Update();
        SerializedProperty property = _selectedObject.FindProperty(propertyPath);
        if (property == null)
            return;

        Rect rect = GUIUtility.GUIToScreenRect(activator.worldBound);
        EditorSearchPicker.Show(rect, "조건으로 읽을 상태 키 — 저장된 상태 정의 기준 (새 키는 State Definition Editor에서 선언하고 저장)",
            BuildStateKeyItems(), property.stringValue, item =>
            {
                if (activator.panel == null || _selectedObject == null) // 팝업이 떠 있는 사이 패널이 다시 그려졌으면 무시
                    return;

                _selectedObject.Update();
                SerializedProperty target = _selectedObject.FindProperty(propertyPath);
                if (target == null)
                    return;

                target.stringValue = (string)item.Payload;
                _selectedObject.ApplyModifiedProperties();
                Undo.SetCurrentGroupName("상태 키 변경");
                OnNodeStructureChanged(); // 미저장 판정·검사 예약 + 키 설명을 새 키에 맞게 다시 그린다
            });
    }

    /// <summary>팝업 항목: 시스템 키가 맨 위, 나머지는 이름순. 게임이 등록할 키만 나온다 (이름 규칙에 어긋나거나 중복된 선언은 빠진다).</summary>
    private List<EditorSearchPicker.Item> BuildStateKeyItems()
    {
        var items = new List<EditorSearchPicker.Item>();
        var declared = new List<EditorSearchPicker.Item>();

        for (int i = 0; i < _stateCatalog.KeyCount; i++)
        {
            StateKeyDefinition key = _stateCatalog.GetKey(i);
            if (key.Key.StartsWith(StateKeyRegistry.ReservedPrefix, StringComparison.Ordinal))
                items.Add(new EditorSearchPicker.Item(key.Key, DescribeStateKey(key), key.Key));
            else
                declared.Add(new EditorSearchPicker.Item(key.Key, DescribeStateKey(key), key.Key));
        }

        declared.Sort((x, y) => string.CompareOrdinal(x.Title, y.Title));
        items.AddRange(declared);
        return items;
    }

    private string DescribeStateKey(StateKeyDefinition key)
    {
        string description = string.IsNullOrEmpty(key.Description) ? "" : key.Description + " · ";
        if (key.Key.StartsWith(StateKeyRegistry.ReservedPrefix, StringComparison.Ordinal))
            return $"{description}시스템 키 (코드가 관리)";
        return $"{description}기본값 {key.Default} · {EditorEnumLabels.Of(key.Reset)} · {_stateCatalog.OwnerOf(key.Key)}";
    }

    /// <summary>신호 발생 효과의 SignalId — 기본 숫자 칸 아래에 "이 신호를 받는 규칙"을 보여 준다.</summary>
    private VisualElement BuildSignalIdField(SerializedProperty field)
    {
        var box = new VisualElement();
        box.Add(new PropertyField(field));

        var info = new Label();
        info.style.paddingLeft = 4;
        info.style.whiteSpace = WhiteSpace.Normal;
        box.Add(info);
        UpdateSignalInfo(info, field.intValue);

        // 숫자 칸의 변경 이벤트가 이 상자까지 올라온다 — 입력하는 대로 문구를 맞춘다. Undo 때는 창이 패널을 다시 그린다
        box.RegisterCallback<ChangeEvent<int>>(evt => UpdateSignalInfo(info, evt.newValue));
        return box;
    }

    private void UpdateSignalInfo(Label info, int signalId)
    {
        string receivers = _stateCatalog.DescribeSignalReceivers(signalId, MaxReceiversShown);
        if (receivers != null)
        {
            info.text = $"이 신호를 받는 규칙: {receivers}";
            info.style.color = StyleKeyword.Null;
            info.style.opacity = 0.7f;
        }
        else
        {
            info.text = _stateCatalog.IsComplete
                ? "이 신호를 받는 규칙이 없음 — 코드가 직접 받는 신호가 아니라면 상태 값이 바뀌지 않습니다"
                : "읽지 못한 상태 파일이 있어 받는 규칙을 확인하지 못했습니다";
            info.style.color = WarningColor;
            info.style.opacity = 1f;
        }
    }
    #endregion
}