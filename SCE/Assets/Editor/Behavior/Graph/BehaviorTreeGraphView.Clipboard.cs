using System.Collections.Generic;
using System.Text;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 그래프 복사 · 잘라내기 · 붙이기 · 복제 — GraphView 명령 (Ctrl+C · Ctrl+X · Ctrl+V · Ctrl+D · Edit 메뉴)이 이 그래프의 글 (BehaviorTreeClipboard)을 쓰게 잇는다.
///  - 복사하는 것: 고른 카드 (ROOT는 빼고)와 고른 카드끼리의 선, 또는 고른 Decorator · Service 줄 하나. 클립보드는 GraphView가 다룬다 — 메뉴도 단축키와 같은 GraphView의 복사 · 붙이기 · 복제를 부른다.
///  - 붙일 자리: 카드는 이어지지 않은 노드로 — 메뉴 '붙이기'는 누른 자리, 단축키는 마우스 자리 (그래프 밖이면 보이는 화면 가운데), 복제는 원래 자리에서 DuplicateOffset만큼.
///    줄은 고른 같은 종류 줄 다음, 아니면 고른 카드 (또는 고른 줄의 카드)의 그 종류 목록 끝.
///  - 붙이기 · 복제는 Undo 한 번, 붙인 카드 · 줄을 고르고, 결과 (붙인 수 · 더한 키 · 그대로 둔 키 · 못 붙인 까닭)를 상태 줄에 알린다.
///  - 잘라내기는 복사 + 지우기 (DeleteSelection — 지우기 규칙 그대로) — 복사하지 못했으면 지우지 않는다.
/// </summary>
public sealed partial class BehaviorTreeGraphView
{
    private const string CutOperation = "Cut";             // GraphView가 잘라낼 때 지우기에 넘기는 이름
    private const string DuplicateOperation = "Duplicate"; // GraphView가 복제할 때 붙이기에 넘기는 이름
    private const int KeysShown = 4; // 상태 줄에 이름으로 적는 키 수
    private static readonly Vector2 DuplicateOffset = new Vector2(30f, 30f); // 복제는 원래 카드에서 이만큼 옮겨 놓는다

    private Vector2 _mousePosition;  // 마지막 마우스 자리 (패널 좌표) — 그래프 위에서 움직일 때
    private bool _mouseInGraph;
    private Vector2? _pastePosition; // 메뉴 '붙이기'를 누른 자리 (그래프 좌표) — 그 메뉴의 붙이기 동안만
    private string _copied;          // 마지막 글 쓰기 (복사 · 잘라내기 · 복제)가 쓴 것 ("카드 2개" · "줄 'x'") — 쓰지 못했으면 null
    private string _copyError;       // 마지막 글 쓰기를 하지 못한 까닭 — 썼으면 null

    /// <summary>생성자에서 — GraphView의 복사 · 붙이기 · 잘라내기가 이 그래프의 글을 쓰게 하고, 붙일 자리 (마우스)를 따라간다</summary>
    private void SetupClipboard()
    {
        serializeGraphElements = SerializeForClipboard;
        canPasteSerializedData = BehaviorTreeClipboard.LooksLikeClipboard;
        unserializeAndPaste = OnUnserializeAndPaste;
        deleteSelection = OnDeleteSelection;
        RegisterCallback<PointerMoveEvent>(OnPointerMove);
        RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        _mousePosition = evt.position;
        _mouseInGraph = true;
    }

    private void OnPointerLeave(PointerLeaveEvent evt)
    {
        _mouseInGraph = false;
    }

    /// <summary>복사 · 잘라내기 · 복제 (GraphView 기본 — 복사와 같다)를 쓸 수 있는가 — 보이는 문서에서 카드 (ROOT 말고)나 줄을 골랐다</summary>
    protected override bool canCopySelection => _document != null && (_selectedAuxiliary != null || HasSelectedCard());

    protected override bool canCutSelection => canCopySelection;

    /// <summary>붙이기를 쓸 수 있는가 — 보이는 문서가 있고 클립보드 글이 이 툴의 글로 보인다 (GraphView 기본 → canPasteSerializedData)</summary>
    protected override bool canPaste => _document != null && base.canPaste;

    private bool HasSelectedCard()
    {
        for (int i = 0; i < selection.Count; i++)
        {
            if (selection[i] is BehaviorNodeCard)
                return true;
        }
        return false;
    }

    #region GraphView가 부르는 것
    /// <summary>
    /// 복사 · 잘라내기 · 복제할 때 — 고른 줄이 있으면 그 줄, 아니면 elements의 카드 (ROOT · 선은 뺀다 — 고른 카드끼리의 선은 문서에서)의 글.
    /// 쓸 수 없으면 빈 글 (GraphView는 클립보드를 그대로 둔다)과 상태 줄에 까닭. 쓴 것 · 까닭은 잘라내기 · 복제가 이어서 본다
    /// </summary>
    private string SerializeForClipboard(IEnumerable<GraphElement> elements)
    {
        _copied = null;
        if (_document == null)
        {
            _copyError = "보이는 트리가 없음";
            return "";
        }
        string text;
        if (_selectedAuxiliary != null)
        {
            text = BehaviorTreeClipboard.CopyRow(_document, _selectedAuxiliaryOwner, _selectedAuxiliary, out _copyError);
            if (text != null)
                _copied = $"줄 '{_selectedAuxiliary.NodeId}'";
        }
        else
        {
            var nodes = new List<BehaviorMainNode>();
            foreach (GraphElement element in elements)
            {
                if (element is BehaviorNodeCard card)
                    nodes.Add(card.MainNode);
            }
            text = BehaviorTreeClipboard.CopyCards(_document, nodes, out int copied, out _copyError);
            if (text != null)
                _copied = $"카드 {copied}개";
        }
        Report(text != null ? _copied + " 복사" : "복사하지 못함 — " + _copyError, text == null);
        return text ?? "";
    }

    /// <summary>
    /// 붙이기 · 복제할 때 — 붙일 자리: 복제는 원래 자리에서 조금 옮겨, 메뉴 '붙이기'는 누른 자리, 단축키는 마우스 자리 (그래프 밖이면 보이는 화면 가운데).
    /// 빈 글이면 하지 않는다 — 복제의 빈 글은 바로 앞 글 쓰기를 하지 못한 것이라 그 까닭을 알린다
    /// </summary>
    private void OnUnserializeAndPaste(string operationName, string data)
    {
        if (_document == null)
            return;
        bool duplicate = operationName == DuplicateOperation;
        if (string.IsNullOrEmpty(data))
        {
            if (duplicate && _copyError != null)
                Report("복제하지 못함 — " + _copyError, true);
            return;
        }
        Vector2 topLeft = duplicate ? Vector2.zero : _pastePosition ?? PastePositionNow(); // 복제는 글의 원래 자리를 쓴다
        PasteText(data, topLeft, duplicate);
    }

    /// <summary>
    /// 지우기 · 잘라내기할 때 — 지우기는 DeleteSelection (지우기 규칙 그대로). 잘라내기는 바로 앞 글 쓰기를 했을 때만 지운다
    /// (하지 못했으면 클립보드는 그대로라 지운 것을 다시 붙일 수 없다 — 지우지 않고 까닭을 알린다)
    /// </summary>
    private void OnDeleteSelection(string operationName, AskUser askUser)
    {
        if (operationName != CutOperation)
        {
            DeleteSelection();
            return;
        }
        if (_copied == null)
        {
            Report("잘라내지 못함 — " + (_copyError ?? "복사하지 못함"), true);
            return;
        }
        DeleteSelection();
        Report(_copied + " 잘라냄", false);
    }

    /// <summary>단축키로 붙일 자리 (그래프 좌표) — 마우스가 그래프 위면 그 자리, 아니면 보이는 화면 가운데</summary>
    private Vector2 PastePositionNow()
    {
        Vector2 panelPoint = _mouseInGraph && worldBound.Contains(_mousePosition) ? _mousePosition : worldBound.center;
        return contentViewContainer.WorldToLocal(panelPoint);
    }
    #endregion

    #region 붙이기
    /// <summary>
    /// 글을 문서에 붙인다 (Undo 한 번). 카드는 이어지지 않은 노드로 topLeft (그래프 좌표 — 복사한 카드들의 왼쪽 위가 올 자리)에 — duplicate면 글의 원래 자리에서 DuplicateOffset만큼 (topLeft는 보지 않음).
    /// 줄은 고른 같은 종류 줄 다음, 아니면 고른 카드 · 고른 줄의 카드의 그 종류 목록 끝. 붙인 것을 고른다.
    /// 읽지 못하거나 줄을 붙일 카드가 없으면 false — 문서 그대로, 상태 줄에 까닭
    /// </summary>
    internal bool PasteText(string text, Vector2 topLeft, bool duplicate)
    {
        if (_document == null)
            return false;
        if (BehaviorTreeClipboard.TryRead(text, _document.Registry, out BehaviorClipboardData data, out string error) == false)
        {
            Report("붙이지 못함 — " + error, true);
            return false;
        }
        var added = new List<string>();
        var clashed = new List<string>();
        string verb = duplicate ? "복제" : "붙임";
        string undoName = duplicate ? "Behavior Tree 복제" : "Behavior Tree 붙이기";

        if (data.IsRow)
        {
            if (TryGetRowTarget(data.Row, out BehaviorMainNode owner, out int index) == false)
            {
                Report("줄을 붙일 카드를 고르세요 (ROOT 말고 카드 하나, 또는 줄 하나)", true);
                return false;
            }
            Record(undoName);
            BehaviorAuxiliaryNode row = BehaviorTreeClipboard.PasteRow(_document, data, owner, index, added, clashed);
            AfterStructureChange();
            SelectAuxiliary(owner, row);
            Report($"줄 '{row.NodeId}' {verb}" + KeysNotice(added, clashed), clashed.Count > 0);
            return true;
        }

        Record(undoName);
        List<BehaviorMainNode> pasted = BehaviorTreeClipboard.PasteCards(_document, data, duplicate ? data.Origin + DuplicateOffset : topLeft, added, clashed);
        RebuildNow();
        ClearSelection();
        for (int i = 0; i < pasted.Count; i++)
        {
            if (_cards.TryGetValue(pasted[i], out BehaviorNodeCard card))
                AddToSelection(card);
        }
        _documentChanged?.Invoke();
        Report($"카드 {pasted.Count}개 {verb} — 이어지지 않은 노드 (선을 이으면 트리에 듦)" + KeysNotice(added, clashed), clashed.Count > 0);
        return true;
    }

    /// <summary>줄을 붙일 곳 — 고른 줄이 있으면 그 카드 (같은 종류 줄이면 그 다음 자리, 아니면 끝), 아니면 고른 카드 하나 (ROOT 말고 — 끝). 없으면 false</summary>
    private bool TryGetRowTarget(BehaviorAuxiliaryNode row, out BehaviorMainNode owner, out int index)
    {
        index = -1;
        if (_selectedAuxiliary != null && BehaviorTreeEditing.IsInDocument(_document, _selectedAuxiliaryOwner))
        {
            owner = _selectedAuxiliaryOwner;
            if ((_selectedAuxiliary is BehaviorDecorator) == (row is BehaviorDecorator))
            {
                int at = BehaviorTreeEditing.IndexOfAuxiliary(owner, _selectedAuxiliary, out _);
                if (at >= 0)
                    index = at + 1;
            }
            return true;
        }
        if (selection.Count == 1 && selection[0] is BehaviorNodeCard card)
        {
            owner = card.MainNode;
            return true;
        }
        owner = null;
        return false;
    }

    /// <summary>상태 줄 꼬리 — 더한 키 · 같은 이름이 다른 형식이라 그대로 둔 키 (앞 몇 개 이름)</summary>
    private static string KeysNotice(List<string> added, List<string> clashed)
    {
        var text = new StringBuilder();
        if (added.Count > 0)
        {
            text.Append(" · 키 ").Append(added.Count).Append("개도 더함: ");
            AppendNames(text, added);
        }
        if (clashed.Count > 0)
        {
            text.Append(" · 같은 이름이 다른 형식이라 그대로 둔 키: ");
            AppendNames(text, clashed);
            text.Append(" (검사 목록)");
        }
        return text.ToString();
    }

    private static void AppendNames(StringBuilder text, List<string> names)
    {
        var shown = new EditorShownList(text, KeysShown, ", ");
        for (int i = 0; i < names.Count; i++)
        {
            if (shown.Next())
                text.Append(names[i]);
        }
        shown.AppendRest(names.Count);
    }

    private void Report(string text, bool isError)
    {
        _status?.Invoke(text, isError);
    }
    #endregion

    #region 메뉴
    /// <summary>카드 메뉴 '복사' — 고르지 않은 카드면 그 카드만 고른 뒤 (지우기와 같다), 단축키와 같은 GraphView의 복사로</summary>
    internal void CopyCard(BehaviorNodeCard card)
    {
        SelectForMenu(card);
        CopySelectionCallback();
    }

    /// <summary>카드 메뉴 '복제' — 고르지 않은 카드면 그 카드만 고른 뒤</summary>
    internal void DuplicateCard(BehaviorNodeCard card)
    {
        SelectForMenu(card);
        DuplicateSelectionCallback();
    }

    /// <summary>줄 메뉴 '복사' — 그 줄을 고른 뒤</summary>
    internal void CopyRow(BehaviorMainNode owner, BehaviorAuxiliaryNode node)
    {
        SelectAuxiliary(owner, node);
        CopySelectionCallback();
    }

    /// <summary>줄 메뉴 '복제' — 그 줄을 고른 뒤 (복제는 그 줄 바로 다음에)</summary>
    internal void DuplicateRow(BehaviorMainNode owner, BehaviorAuxiliaryNode node)
    {
        SelectAuxiliary(owner, node);
        DuplicateSelectionCallback();
    }

    /// <summary>빈 곳 메뉴 '붙이기' — 누른 자리 (패널 좌표)에</summary>
    private void PasteAt(Vector2 panelPosition)
    {
        _pastePosition = contentViewContainer.WorldToLocal(panelPosition);
        try
        {
            PasteCallback();
        }
        finally
        {
            _pastePosition = null;
        }
    }

    private void SelectForMenu(BehaviorNodeCard card)
    {
        if (card.selected)
            return;
        ClearSelection();
        AddToSelection(card);
    }
    #endregion
}