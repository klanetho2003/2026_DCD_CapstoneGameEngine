using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 실시간 검사 · 검증 목록.
/// 런타임 검사(StateDefinitionValidator — 게임 로드와 같은 규칙)
/// + 툴 경고(StateDefinitionLint · StateInteractionCrossCheck — 에디터 전용, "[툴]"로 시작).
/// 모든 파일을 함께 검사한다 — 파일 간 키 참조와 중복은 파일 하나만 봐서는 판정할 수 없기 때문.
/// </summary>
public sealed partial class StateDefinitionEditorWindow
{
    private const long ValidationDelayMs = 150; // 연속 편집 중에는 마지막 변경 후 한 번만 검사
    private const string ShowLintPrefKey = "StateDefinitionEditor.ShowLint";

    private struct IssueRow
    {
        public StateDefinitionIssue Issue;             // 상태 파일 안에 위치가 있는 문제
        public bool IsLint;
        public bool IsInteractionSide;                 // [6] true면 Issue 대신 InteractionIssue를 쓴다
        public InteractionCrossIssue InteractionIssue; // [6] 상호작용 쪽에 위치가 있는 툴 경고
    }

    private readonly List<StateDefinitionSet> _setScratch = new List<StateDefinitionSet>();
    private readonly List<StateDefinitionIssue> _issues = new List<StateDefinitionIssue>(); // 런타임 검사 + 툴 경고 (SetIndex = _assets 순번)
    private readonly List<InteractionCrossIssue> _interactionIssues = new List<InteractionCrossIssue>(); // [6] 어느 상태 파일에도 속하지 않는 툴 경고
    private readonly List<IssueRow> _issueRows = new List<IssueRow>();                     // 화면용 (파일 순서로 묶음)
    private int _lintStart;                                                                  // _issues에서 툴 경고가 시작하는 순번
    private int _errorCount;
    private int _warningCount;
    private int _lintCount;

    private IVisualElementScheduledItem _validationJob;
    private ListView _issueList;
    private Label _validationTitle;

    [SerializeField] private bool _issuesCurrentFileOnly; // 창에 직렬화 — 스크립트 컴파일 뒤에도 유지
    private bool _showLint = true;                        // EditorPrefs — 사람마다 다른 선호

    private void ScheduleValidation()
    {
        _validationJob?.ExecuteLater(ValidationDelayMs);
    }

    private void RunValidationNow()
    {
        _validationJob?.Pause(); // 예약된 검사가 있으면 취소하고 지금 바로 한다

        _setScratch.Clear();
        for (int i = 0; i < _assets.Count; i++)
            _setScratch.Add(_assets[i].Set);

        // [6] 게임 로드와 같은 순서: 시스템 키를 먼저 등록한 등록소에, 검사기가 통과시킨 키가 등록된다.
        // 교차 확인은 "게임이 실제로 등록할 키"를 이 등록소로 판정한다 — 키 목록을 따로 만들지 않는다
        var registry = new StateKeyRegistry();
        GameStateManager.RegisterSystemKeys(registry);

        _issues.Clear();
        _interactionIssues.Clear();
        StateDefinitionValidator.Validate(_setScratch, registry, _issues, null);
        _lintStart = _issues.Count;
        if (_showLint)
        {
            StateDefinitionLint.Check(_setScratch, _issues);
            CrossCheckInteractions(_setScratch, registry); // [6]
        }

        // 파일별 집계 (목록 배지·저장 거부용 — 저장을 막는 것은 런타임 검사의 에러뿐, 툴 경고는 모두 경고)
        for (int i = 0; i < _assets.Count; i++)
        {
            _assets[i].ErrorCount = 0;
            _assets[i].WarningCount = 0;
        }
        _errorCount = 0;
        _warningCount = 0;
        for (int i = 0; i < _issues.Count; i++)
        {
            StateDefinitionIssue issue = _issues[i];
            if (issue.IsError)
            {
                _assets[issue.SetIndex].ErrorCount++;
                _errorCount++;
            }
            else
            {
                _assets[issue.SetIndex].WarningCount++;
                _warningCount++;
            }
        }
        _warningCount += _interactionIssues.Count;                          // [6] 파일 배지에는 넣지 않는다 (상태 파일의 문제가 아니므로)
        _lintCount = _issues.Count - _lintStart + _interactionIssues.Count; // [6]

        _fileList?.RefreshItems();
        ApplyIssuesToViews();
        RefreshInspector(); // 모든 변경 뒤에 검사가 돌므로 사용처 패널도 여기서 최신으로
    }

    /// <summary>
    /// 검사 결과를 화면에 반영한다: 검증 목록 + 선택 파일의 키 표·규칙 카드.
    /// 선택·탭 전환 직후에도 불린다 — 다시 검사하지 않고 마지막 결과를 쓴다.
    /// </summary>
    private void ApplyIssuesToViews()
    {
        RebuildIssueRows(); // "현재 파일만" 보기는 선택에 따라 달라진다

        if (_selected == null)
            return;

        int setIndex = _assets.IndexOf(_selected);
        _keyTable?.SetIssues(_issues, setIndex);
        _ruleList?.SetIssues(_issues, setIndex);
    }

    /// <summary>화면용 목록: 파일 순서 → 파일 안에서는 검사 순서 (런타임 검사 → 툴 경고) → 맨 아래에 상호작용 쪽 경고.</summary>
    private void RebuildIssueRows()
    {
        _issueRows.Clear();
        int selectedIndex = _selected != null ? _assets.IndexOf(_selected) : -1;

        for (int s = 0; s < _assets.Count; s++)
        {
            if (_issuesCurrentFileOnly && s != selectedIndex)
                continue;

            for (int i = 0; i < _issues.Count; i++)
            {
                if (_issues[i].SetIndex == s)
                    _issueRows.Add(new IssueRow { Issue = _issues[i], IsLint = i >= _lintStart });
            }
        }

        // [6] 상호작용 쪽 경고는 어느 상태 파일에도 속하지 않는다 — "현재 파일만"에서는 뺀다
        if (_issuesCurrentFileOnly == false)
        {
            for (int i = 0; i < _interactionIssues.Count; i++)
                _issueRows.Add(new IssueRow { IsLint = true, IsInteractionSide = true, InteractionIssue = _interactionIssues[i] });
        }

        _issueList?.RefreshItems();
        UpdateValidationTitle();
    }

    #region 검증 목록
    private VisualElement BuildValidationPane()
    {
        _showLint = EditorPrefs.GetBool(ShowLintPrefKey, true);

        var pane = new VisualElement();

        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.borderBottomWidth = 1;
        header.style.borderBottomColor = new Color(0f, 0f, 0f, 0.35f);

        _validationTitle = new Label("검증");
        _validationTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
        _validationTitle.style.paddingLeft = 6;
        _validationTitle.style.flexGrow = 1;
        header.Add(_validationTitle);

        var currentOnly = new Toggle
        {
            text = "현재 파일만",
            value = _issuesCurrentFileOnly,
            tooltip = "선택한 파일의 문제만 보기 (상호작용 쪽 경고는 숨김). 검사는 항상 모든 파일을 함께 한다 (파일 간 키 참조 때문)",
        };
        currentOnly.RegisterValueChangedCallback(e =>
        {
            _issuesCurrentFileOnly = e.newValue;
            RebuildIssueRows();
        });
        header.Add(currentOnly);

        var lint = new Toggle
        {
            text = "툴 경고",
            value = _showLint,
            tooltip = "[툴]로 시작하는 에디터 전용 경고 — ruleKey 0, 오지 않는 일차, 효과 없는 op, 실행 순서, 파일 id 중복, "
                    + "상호작용 교차 확인(없는 키 읽기 · 짝 없는 신호). 이 문장들은 게임 로그에 나오지 않는다",
        };
        lint.style.marginLeft = 8;
        lint.RegisterValueChangedCallback(e =>
        {
            _showLint = e.newValue;
            EditorPrefs.SetBool(ShowLintPrefKey, _showLint);
            RunValidationNow();
        });
        header.Add(lint);

        header.Add(new Button(RunValidationNow) { text = "다시 검사" });
        pane.Add(header);

        _issueList = new ListView
        {
            itemsSource = _issueRows,
            fixedItemHeight = 20,
            selectionType = SelectionType.Single,
            makeItem = MakeIssueRow,
            bindItem = BindIssueRow,
        };
        _issueList.style.flexGrow = 1;
        pane.Add(_issueList);
        return pane;
    }

    private VisualElement MakeIssueRow()
    {
        var label = new Label();
        label.style.paddingLeft = 8;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;

        // 누를 때마다 이동 — selectionChanged는 같은 항목을 다시 누르면 오지 않아서 클릭 이벤트를 쓴다
        label.RegisterCallback<ClickEvent>(_ =>
        {
            if (label.userData is int row)
                JumpToIssue(row);
        });
        return label;
    }

    private void BindIssueRow(VisualElement element, int index)
    {
        var label = (Label)element;
        IssueRow row = _issueRows[index];
        label.userData = index;

        if (row.IsInteractionSide) // [6]
        {
            InteractionCrossIssue cross = row.InteractionIssue;
            label.text = "⚠  " + cross.Text;
            label.style.color = WarningColor;
            label.style.opacity = 0.85f;

            string detail = string.IsNullOrEmpty(cross.Detail) ? "" : "\n" + cross.Detail;
            string howTo = string.IsNullOrEmpty(cross.FilePath)
                ? ""
                : "\n\n누르면 Project 창에서 그 상호작용 파일을 가리킵니다. 고치는 곳은 Interaction Editor입니다";
            label.tooltip = cross.Text + detail + howTo;
            return;
        }

        label.text = (row.Issue.IsError ? "✕  " : "⚠  ") + DescribeIssue(row.Issue);
        label.style.color = row.Issue.IsError ? ErrorColor : WarningColor;
        label.style.opacity = row.IsLint ? 0.85f : 1f; // 툴 경고는 조금 옅게
        label.tooltip = row.IsLint ? "툴 경고 — 게임 로그에는 나오지 않는 기획 실수 탐지. 의도한 것이면 무시해도 된다" : "";
    }

    /// <summary>문장은 런타임 로그와 같다. 파일 전체 문제(id 비어 있음·중복)만 문장에 파일 이름이 없어 앞에 붙인다.</summary>
    private string DescribeIssue(StateDefinitionIssue issue)
    {
        if (issue.Section != EStateDefinitionSection.File || issue.SetIndex < 0 || issue.SetIndex >= _assets.Count)
            return issue.Text;
        return $"{FileLabel(_assets[issue.SetIndex])}: {issue.Text}";
    }

    private void UpdateValidationTitle()
    {
        if (_validationTitle == null)
            return;

        string lint = _showLint && _lintCount > 0 ? $" (툴 경고 {_lintCount} 포함)" : "";
        string broken = _brokenFiles.Count > 0 ? $" · 파싱 실패 파일 {_brokenFiles.Count}개" : "";
        string scope = _issuesCurrentFileOnly ? "   — 이 파일만 표시 중" : "";
        _validationTitle.text = _errorCount + _warningCount == 0
            ? $"검증 — 문제 없음{broken}{scope}"
            : $"검증 — 에러 {_errorCount} · 경고 {_warningCount}{lint}{broken}{scope}   (항목을 누르면 위치로 이동)";
    }

    private void JumpToIssue(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _issueRows.Count)
            return;

        IssueRow row = _issueRows[rowIndex];
        if (row.IsInteractionSide) // [6] 상태 파일 안에 위치가 없다 — 상호작용 파일을 가리킨다
        {
            JumpToInteractionIssue(row.InteractionIssue);
            return;
        }

        JumpToLocation(row.Issue.SetIndex, row.Issue.Section, row.Issue.ItemIndex, row.Issue.SubIndex);
    }

    /// <summary>파일 선택 → 탭 전환 → 키 행 또는 규칙 카드(행)로 이동. 검증 목록과 키 사용처 패널이 같이 쓴다.</summary>
    private void JumpToLocation(int setIndex, EStateDefinitionSection section, int itemIndex, int subIndex)
    {
        if (setIndex < 0 || setIndex >= _assets.Count)
            return;

        StateDefinitionAsset asset = _assets[setIndex];
        if (asset != _selected)
        {
            SelectAsset(asset);
            RefreshFileList();
        }

        bool keySide = section == EStateDefinitionSection.Key || section == EStateDefinitionSection.File;
        SwitchTab(keySide ? ETab.Keys : ETab.Rules);

        if (section == EStateDefinitionSection.Key)
            _keyTable?.ScrollToKey(itemIndex);
        else if (keySide == false)
            _ruleList?.ScrollToRule(itemIndex, section, subIndex);
    }
    #endregion
}