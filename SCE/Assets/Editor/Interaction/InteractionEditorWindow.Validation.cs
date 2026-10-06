using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 실시간 검사 · 검증 목록.
/// 검사 규칙은 런타임과 같은 InteractionValidator다. 여기에 툴만 아는 두 가지를 더한다:
/// Set Id 중복 (게임은 두 번째 Set을 거부한다), 파일 이름으로 쓸 수 없는 Set Id (파일 이름 = Set Id 규칙 때문).
/// 모든 Set을 함께 검사한다 — Set Id 중복은 Set 하나만 봐서는 알 수 없다.
/// </summary>
public sealed partial class InteractionEditorWindow
{
    private const long ValidationDelayMs = 150; // 연속 편집 중에는 마지막 변경 후 한 번만 검사

    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    private struct ValidationEntry
    {
        public InteractionSetAsset Asset;
        public string Message;
        public bool IsError;
        public int CardIndex; // 가리키는 카드가 없으면 -1
    }

    private readonly List<ValidationEntry> _entries = new List<ValidationEntry>();    // 모든 Set의 검사 결과
    private readonly List<ValidationEntry> _entryRows = new List<ValidationEntry>();  // 화면용 ("현재 Set만" 적용)
    private readonly List<string> _errorScratch = new List<string>();
    private readonly List<string> _warningScratch = new List<string>();
    private readonly Dictionary<string, int> _idCounts = new Dictionary<string, int>(StringComparer.Ordinal);
    private int _errorCount;
    private int _warningCount;

    private IVisualElementScheduledItem _validationJob;
    private ListView _validationList;
    private Label _validationTitle;

    [SerializeField] private bool _entriesCurrentSetOnly; // 창에 직렬화 — 스크립트 컴파일 뒤에도 유지

    private void ScheduleValidation()
    {
        _validationJob?.ExecuteLater(ValidationDelayMs);
    }

    private void RunValidationNow()
    {
        _validationJob?.Pause(); // 예약된 검사가 있으면 취소하고 지금 바로 한다

        _entries.Clear();
        _errorCount = 0;
        _warningCount = 0;

        // Set Id별 개수 — 게임(InteractionManager.AddSet)은 같은 Id의 두 번째 Set을 거부한다
        _idCounts.Clear();
        for (int i = 0; i < _assets.Count; i++)
        {
            string id = _assets[i].Set.Id ?? "";
            _idCounts.TryGetValue(id, out int count);
            _idCounts[id] = count + 1;
        }

        for (int i = 0; i < _assets.Count; i++)
        {
            InteractionSetAsset asset = _assets[i];
            asset.ErrorCount = 0;
            asset.WarningCount = 0;
            string id = asset.Set.Id ?? "";

            _errorScratch.Clear();
            _warningScratch.Clear();
            InteractionValidator.Validate(asset.Set, _errorScratch, _warningScratch);

            for (int e = 0; e < _errorScratch.Count; e++)
                AddEntry(asset, _errorScratch[e], true, ParseCardIndex(_errorScratch[e], id));

            if (id.Length > 0 && _idCounts[id] > 1)
                AddEntry(asset, $"Set Id 중복 '{id}' — 게임은 먼저 로드한 하나만 등록하고 나머지는 거부", true, -1);
            if (id.IndexOfAny(InvalidFileNameChars) >= 0)
                AddEntry(asset, $"Set Id '{id}'에 파일 이름으로 쓸 수 없는 문자가 있음 — 파일 이름이 Set Id라서 저장할 수 없음", true, -1);

            for (int w = 0; w < _warningScratch.Count; w++)
                AddEntry(asset, _warningScratch[w], false, ParseCardIndex(_warningScratch[w], id));

            AddStateLinkEntries(asset); // 없는 상태 키(에러), 받는 규칙 없는 신호(경고)
        }

        _setList?.RefreshItems(); // Set별 배지
        RebuildValidationRows();
    }

    private void AddEntry(InteractionSetAsset asset, string message, bool isError, int cardIndex)
    {
        _entries.Add(new ValidationEntry { Asset = asset, Message = message, IsError = isError, CardIndex = cardIndex });
        if (isError)
        {
            asset.ErrorCount++;
            _errorCount++;
        }
        else
        {
            asset.WarningCount++;
            _warningCount++;
        }
    }

    /// <summary>
    /// 검증 문장은 "{Set Id}[{카드 순번}]: …"로 시작한다 (InteractionValidator). 그 순번을 읽는다. 없으면 -1.
    /// Set Id 바로 뒤의 대괄호만 읽으므로 Set Id 안에 '['가 있어도 틀리지 않는다.
    /// </summary>
    private static int ParseCardIndex(string message, string setId)
    {
        string prefix = setId + "[";
        if (message.StartsWith(prefix, StringComparison.Ordinal) == false)
            return -1;

        int close = message.IndexOf(']', prefix.Length);
        if (close < 0)
            return -1;
        return int.TryParse(message.Substring(prefix.Length, close - prefix.Length), out int index) ? index : -1;
    }

    /// <summary>화면용 목록을 다시 만든다. 선택이 바뀔 때도 불린다 — 다시 검사하지 않고 마지막 결과를 쓴다.</summary>
    private void RebuildValidationRows()
    {
        _entryRows.Clear();
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entriesCurrentSetOnly && _entries[i].Asset != _selected)
                continue;
            _entryRows.Add(_entries[i]);
        }

        _validationList?.RefreshItems();
        UpdateValidationTitle();
    }

    #region 검증 목록
    private VisualElement BuildValidationPane()
    {
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
            text = "현재 Set만",
            value = _entriesCurrentSetOnly,
            tooltip = "선택한 Set의 문제만 보기. 검사는 항상 모든 Set을 함께 한다 (Set Id 중복 때문)",
        };
        currentOnly.RegisterValueChangedCallback(e =>
        {
            _entriesCurrentSetOnly = e.newValue;
            RebuildValidationRows();
        });
        header.Add(currentOnly);

        header.Add(new Button(RunValidationNow) { text = "다시 검사" });
        pane.Add(header);

        _validationList = new ListView
        {
            itemsSource = _entryRows,
            fixedItemHeight = 20,
            selectionType = SelectionType.Single,
            makeItem = MakeValidationRow,
            bindItem = BindValidationRow,
        };
        _validationList.style.flexGrow = 1;
        pane.Add(_validationList);
        return pane;
    }

    private VisualElement MakeValidationRow()
    {
        var label = new Label();
        label.style.paddingLeft = 8;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;

        // 누를 때마다 이동 — selectionChanged는 같은 항목을 다시 누르면 오지 않아서 클릭 이벤트를 쓴다
        label.RegisterCallback<ClickEvent>(_ =>
        {
            if (label.userData is int row)
                JumpToEntry(row);
        });
        return label;
    }

    private void BindValidationRow(VisualElement element, int index)
    {
        var label = (Label)element;
        ValidationEntry entry = _entryRows[index];
        label.userData = index;

        // 카드를 가리키는 문장은 Set Id로 시작한다. Set 전체에 대한 문장만 어느 Set인지 앞에 붙인다
        string text = entry.CardIndex >= 0 ? entry.Message : $"{SetLabel(entry.Asset)}: {entry.Message}";
        label.text = (entry.IsError ? "✕  " : "⚠  ") + text;
        label.style.color = entry.IsError ? ErrorColor : WarningColor;
        label.tooltip = text;
    }

    private void UpdateValidationTitle()
    {
        if (_validationTitle == null)
            return;

        string broken = _brokenFiles.Count > 0 ? $" · 파싱 실패 파일 {_brokenFiles.Count}개" : "";
        string state = StateLinkNotice();
        string scope = _entriesCurrentSetOnly ? "   — 이 Set만 표시 중" : "";
        _validationTitle.text = _errorCount + _warningCount == 0
            ? $"검증 — 문제 없음{broken}{state}{scope}"
            : $"검증 — 에러 {_errorCount} · 경고 {_warningCount}{broken}{state}{scope}   (항목을 누르면 카드로 이동)";
    }

    private void JumpToEntry(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _entryRows.Count)
            return;

        ValidationEntry entry = _entryRows[rowIndex];
        if (entry.Asset == null || _assets.Contains(entry.Asset) == false)
            return;

        if (entry.Asset != _selected)
        {
            SelectSet(entry.Asset);
            RefreshSetList();
        }

        // 방금 선택한 Set이면 카드가 아직 배치되지 않았다 — 한 박자 늦춰 이동한다
        int cardIndex = entry.CardIndex;
        if (cardIndex < 0)
            return;
        _cardScroll.schedule.Execute(() =>
        {
            if (cardIndex < _cardViews.Count)
                _cardScroll.ScrollTo(_cardViews[cardIndex]);
        }).ExecuteLater(DeferMs);
    }
    #endregion
}