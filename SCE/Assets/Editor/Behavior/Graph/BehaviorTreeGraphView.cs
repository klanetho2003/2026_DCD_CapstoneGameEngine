using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 트리 그래프 화면 (GraphView) — 편집 사본 (BehaviorTreeDocumentAsset)을 카드 · 선으로 그리고, 화면에서 한 바꾸기를 문서에 옮긴다 (BehaviorTreeEditing).
///  - 구조 (잇기 · 끊기 · 지우기 · 노드 · Decorator · Service)가 바뀌면 문서에서 다시 그린다. GraphView가 바꾸기를 알리는 중 (graphViewChanged)에는 다음 갱신에 한 번
///    — GraphView가 알린 뒤에도 그 카드 · 선을 쓴다.
///  - 카드를 옮기면 자리만 적고, 자식 차례 (= 가로 자리)가 바뀌었으면 실행 순서 번호만 고친다. 칸 값 · 메모가 바뀌면 글만 다시 쓴다 (RefreshTexts).
///  - 문서를 바꾸기 전에 Undo를 기록한다 (사본 전체 — 트리 · 카드 자리 · 이어지지 않은 노드 · 메모를 함께). Undo 뒤 다시 그리기는 창이 부른다 (RebuildNow).
///  - 노드 추가: 빈 곳 오른쪽 클릭 · Space · 칸에서 끈 선을 빈 곳에 놓기 (그 칸에 잇는다).
///  - 복사 · 잘라내기 · 붙이기 · 복제: GraphView 명령 (Ctrl+C · Ctrl+X · Ctrl+V · Ctrl+D · Edit 메뉴)과 카드 · 줄 · 빈 곳 메뉴 — 글 · 붙이기는 BehaviorTreeClipboard (.Clipboard.cs).
///  - 고르기: 카드 (ROOT 포함) 또는 Decorator · Service 줄 하나 — 바뀌면 다음 갱신에 한 번 알린다 (세부 설정). Delete는 고른 줄도 지운다.
///  - 단축키 (A 화면 맞춤 · O 원점 · [ ] 이전 · 다음 카드 · Space 노드 추가)는 그래프에 포커스가 있을 때만 — 창의 다른 곳 (패널 글 칸)에 친 글자는 그래프를 움직이지 않는다.
///  - 검사 결과를 카드 · 줄 · ROOT 카드에 ✕ · ⚠로 보인다 (SetIssues — 다시 그려도 남는다).
///  - 디버거: Play 중 대상 몬스터의 실행 모습 · breakpoint 표시 (.Debug.cs — 다시 그려도 남는다). 선은 BehaviorGraphEdge (진행 경로를 칠한다).
/// 처음 보이는 문서에 카드 자리가 빠졌으면 카드 크기를 잰 뒤 정리 배치한다 (Undo 없음 — 그 전에 배치 편집이 없었으면 편집으로 세지 않는다), 그다음 화면 맞춤.
/// </summary>
public sealed partial class BehaviorTreeGraphView : GraphView
{
    private static readonly Vector2 EstimatedCardSize = new Vector2(180f, 64f); // 재기 전 카드 크기 어림 — 정리 배치 · 새 카드 자리
    private static readonly EBehaviorNodeKind[] MainKinds = { EBehaviorNodeKind.Composite, EBehaviorNodeKind.Task };
    private static readonly EBehaviorNodeKind[] CompositeKinds = { EBehaviorNodeKind.Composite };

    /// <summary>새 노드를 어디에 잇는가 — 끈 선을 빈 곳에 놓았을 때</summary>
    internal enum EAttach
    {
        None,     // 잇지 않음 (이어지지 않은 노드)
        AsRoot,   // ROOT 카드의 출력 칸에서 — 루트 자리
        AsChild,  // Composite의 출력 칸에서 — 그 자식
        AsParent, // 카드의 입력 칸에서 — 그 카드의 부모
    }

    private readonly EditorWindow _window;
    private readonly Action _documentChanged;
    private readonly Action _selectionChanged;
    private readonly Action<string, bool> _status;
    private readonly Dictionary<BehaviorMainNode, BehaviorNodeCard> _cards = new Dictionary<BehaviorMainNode, BehaviorNodeCard>();
    private readonly Dictionary<BehaviorMainNode, int> _order = new Dictionary<BehaviorMainNode, int>();
    private readonly List<BehaviorTreeCheckItem> _issues = new List<BehaviorTreeCheckItem>();
    private readonly IVisualElementScheduledItem _rebuildJob;
    private readonly IVisualElementScheduledItem _selectionJob; // 고르기가 바뀌었다 — 다음 갱신에 한 번 알린다
    private BehaviorTreeDocumentAsset _document;
    private BehaviorRootCard _rootCard;
    private BehaviorMainNode _selectedAuxiliaryOwner; // 고른 Decorator · Service 줄 — 카드 고르기와 함께 두지 않는다
    private BehaviorAuxiliaryNode _selectedAuxiliary;
    private int _unmeasuredCards;      // 크기를 기다리는 카드 수 (첫 GeometryChangedEvent)
    private bool _arrangeWhenMeasured; // 카드 크기를 재면 정리 배치 — 카드 자리가 빠진 문서를 처음 보일 때
    private bool _frameWhenMeasured;   // 카드 크기를 재면 화면 맞춤 — 문서를 보일 때

    /// <param name="window">팝업 자리 (화면 좌표)를 구할 창</param>
    /// <param name="documentChanged">문서를 바꾼 뒤 — 창이 미저장 판정 · 목록 · 패널을 맞춘다</param>
    /// <param name="selectionChanged">고르기 (카드 · 줄)가 바뀐 뒤 (다음 갱신에 한 번) — 창이 세부 설정을 맞춘다</param>
    /// <param name="status">알릴 글 (글, 에러인가) — 창의 상태 줄 (복사 · 붙이기). 없으면 알리지 않는다</param>
    public BehaviorTreeGraphView(EditorWindow window, Action documentChanged, Action selectionChanged, Action<string, bool> status = null)
    {
        _window = window;
        _documentChanged = documentChanged;
        _selectionChanged = selectionChanged;
        _status = status;
        isReframable = false; // GraphView 기본 단축키 (창 전체에서 받음)를 끄고 그래프 포커스에서만 받는다 (OnKeyDown)

        SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
        this.AddManipulator(new ContentDragger());
        this.AddManipulator(new SelectionDragger());
        this.AddManipulator(new RectangleSelector());
        var grid = new GridBackground();
        Insert(0, grid);
        grid.StretchToParentSize();
        style.flexGrow = 1;

        graphViewChanged = OnGraphViewChanged;
        RegisterCallback<KeyDownEvent>(OnKeyDown);
        _rebuildJob = schedule.Execute(RebuildNow);
        _rebuildJob.Pause();
        _selectionJob = schedule.Execute(() => _selectionChanged?.Invoke());
        _selectionJob.Pause();
        SetupClipboard();
    }

    /// <summary>보이는 문서 (없으면 null)</summary>
    public BehaviorTreeDocumentAsset Document => _document;

    #region 보이기 · 다시 그리기
    /// <summary>
    /// 문서를 보인다 (null이면 비운다). 카드 자리가 빠진 노드가 있거나 가로 차례가 자식 차례와 다르면 카드 크기를 잰 뒤 —
    /// 자리가 하나도 없으면 정리 배치, 있으면 가로 차례 맞추기 + 빠진 카드만 놓기 — 그다음 화면 맞춤. 검사 표시는 비운다 (창이 다시 넣는다)
    /// </summary>
    public void Show(BehaviorTreeDocumentAsset document)
    {
        _document = document;
        ForgetDebug(); // 디버거 모습은 창이 다음 갱신에 이 문서의 것으로 다시 넣는다
        _arrangeWhenMeasured = document != null && (HasMissingPlacement(document) || BehaviorTreeLayout.HasOrderMismatch(document));
        _frameWhenMeasured = document != null;
        _issues.Clear();
        ClearSelection(); // 고른 줄도
        RebuildNow();
        _selectionJob.ExecuteLater(0);
    }

    /// <summary>
    /// 문서에서 카드 · 선을 다시 만든다 — 보던 자리 · 확대는 그대로, 고른 카드 · 줄은 같은 이름표의 노드로 다시 고른다 (Undo가 노드 객체를 새로 만들어도). 메모 · 검사 표시도 다시 넣는다.
    /// 고른 줄의 노드가 없어졌으면 줄 고르기를 풀고 알린다. O(노드 수)
    /// </summary>
    public void RebuildNow()
    {
        _rebuildJob.Pause();
        string selectedAuxiliaryId = _selectedAuxiliary?.NodeId;
        var selectedIds = new HashSet<string>(StringComparer.Ordinal);
        bool rootSelected = false;
        foreach (ISelectable selectable in selection)
        {
            if (selectable is BehaviorNodeCard selectedCard && string.IsNullOrEmpty(selectedCard.MainNode.NodeId) == false)
                selectedIds.Add(selectedCard.MainNode.NodeId);
            else if (selectable is BehaviorRootCard)
                rootSelected = true;
        }

        SetSelectedAuxiliary(null, null); // 먼저 — ClearSelection이 줄 고르기를 알리지 않게 (아래에서 다시 고른다)
        ClearSelection();
        foreach (GraphElement element in graphElements.ToList())
            RemoveElement(element);
        _cards.Clear();
        _rootCard = null;
        if (_document == null)
            return;

        BehaviorTreeEditing.ExecutionOrder(_document.Tree, _order);
        _rootCard = new BehaviorRootCard(this, _document.Tree.Id);
        _rootCard.SetPosition(new Rect(_document.RootCardPosition, Vector2.zero));
        AddElement(_rootCard);

        var nodes = new List<BehaviorMainNode>();
        BehaviorTreeEditing.CollectMainNodes(_document, nodes);
        for (int i = 0; i < nodes.Count; i++)
        {
            BehaviorMainNode node = nodes[i];
            var card = new BehaviorNodeCard(this, node, _document.Registry);
            card.SetOrder(_order.TryGetValue(node, out int index) ? index : -1);
            ApplyMemos(card);
            _document.TryGetPosition(node.NodeId, out Vector2 position); // 자리가 없으면 (0, 0) — 처음 보일 때 정리 배치
            card.SetPosition(new Rect(position, Vector2.zero));
            AddElement(card);
            _cards.Add(node, card);
        }

        if (_document.Tree.Root != null && _cards.TryGetValue(_document.Tree.Root, out BehaviorNodeCard rootChild))
            AddEdge(_rootCard.OutputPort, rootChild.InputPort);
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is BehaviorComposite composite && composite.Children != null)
            {
                BehaviorNodeCard parentCard = _cards[composite];
                for (int c = 0; c < composite.Children.Length; c++)
                {
                    BehaviorMainNode child = composite.Children[c];
                    if (child != null && _cards.TryGetValue(child, out BehaviorNodeCard childCard))
                        AddEdge(parentCard.OutputPort, childCard.InputPort);
                }
            }
        }

        ApplyIssues();
        ApplyDebug();
        if (rootSelected)
            AddToSelection(_rootCard);
        foreach (BehaviorNodeCard card in _cards.Values)
        {
            if (selectedIds.Contains(card.MainNode.NodeId ?? ""))
                AddToSelection(card);
        }
        if (selectedAuxiliaryId != null)
        {
            if (TryFindRow(selectedAuxiliaryId, out BehaviorAuxiliaryRow row))
                SetSelectedAuxiliary(row.Owner, row.AuxiliaryNode);
            else
                _selectionJob.ExecuteLater(0);
        }
        if (_arrangeWhenMeasured || _frameWhenMeasured)
            WaitForCardSizes();
    }

    /// <summary>칸 값 · 이름표 · 메모 · 트리 Id가 바뀐 뒤 — 카드 · 줄의 글 · breakpoint 표시만 다시 쓴다 (다시 그리지 않는다 — 고르기 · 끌기 · 보던 자리 그대로). O(노드 수)</summary>
    public void RefreshTexts()
    {
        if (_document == null)
            return;
        _rootCard?.SetTreeId(_document.Tree.Id);
        foreach (BehaviorNodeCard card in _cards.Values)
        {
            card.Refresh();
            ApplyMemos(card);
            card.SetBreakpoint(HasBreakpoint(card.MainNode)); // 이름표 · 트리 Id가 바뀌면 그 breakpoint
        }
    }

    /// <summary>카드 · 그 줄에 문서의 메모를 넣는다</summary>
    private void ApplyMemos(BehaviorNodeCard card)
    {
        card.SetMemo(_document.TryGetMemo(card.MainNode.NodeId, out string memo) ? memo : "");
        IReadOnlyList<BehaviorAuxiliaryRow> rows = card.Rows;
        for (int i = 0; i < rows.Count; i++)
            rows[i].SetMemo(_document.TryGetMemo(rows[i].AuxiliaryNode.NodeId, out string rowMemo) ? rowMemo : "");
    }

    private bool TryFindRow(string nodeId, out BehaviorAuxiliaryRow found)
    {
        foreach (BehaviorNodeCard card in _cards.Values)
        {
            IReadOnlyList<BehaviorAuxiliaryRow> rows = card.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                if (string.Equals(rows[i].AuxiliaryNode.NodeId, nodeId, StringComparison.Ordinal))
                {
                    found = rows[i];
                    return true;
                }
            }
        }
        found = null;
        return false;
    }

    private void AddEdge(Port output, Port input)
    {
        AddElement(output.ConnectTo<BehaviorGraphEdge>(input));
    }

    private void ScheduleRebuild()
    {
        _rebuildJob.ExecuteLater(0);
    }

    /// <summary>실행 순서 번호만 다시 — 자식 차례만 바뀌었을 때 (선은 그대로)</summary>
    private void RefreshOrder()
    {
        BehaviorTreeEditing.ExecutionOrder(_document.Tree, _order);
        foreach (KeyValuePair<BehaviorMainNode, BehaviorNodeCard> pair in _cards)
            pair.Value.SetOrder(_order.TryGetValue(pair.Key, out int index) ? index : -1);
    }

    private void SelectCardOf(BehaviorMainNode node)
    {
        ClearSelection();
        if (_cards.TryGetValue(node, out BehaviorNodeCard card))
            AddToSelection(card);
    }
    #endregion

    #region 고르기
    /// <summary>
    /// 세부 설정이 보일 대상 — 고른 Decorator · Service 줄의 노드, 아니면 고른 것이 카드 하나일 때 그 노드 (ROOT 카드면 node null · isRoot).
    /// 고른 것이 없거나 여럿 · 선이면 false
    /// </summary>
    public bool TryGetSelected(out BehaviorNode node, out bool isRoot)
    {
        node = null;
        isRoot = false;
        if (_document == null)
            return false;
        if (_selectedAuxiliary != null)
        {
            node = _selectedAuxiliary;
            return true;
        }
        if (selection.Count != 1)
            return false;
        if (selection[0] is BehaviorNodeCard card)
        {
            node = card.MainNode;
            return true;
        }
        if (selection[0] is BehaviorRootCard)
        {
            isRoot = true;
            return true;
        }
        return false;
    }

    /// <summary>고른 것의 수 — 줄을 골랐으면 1</summary>
    public int SelectedCount => _selectedAuxiliary != null ? 1 : selection.Count;

    /// <summary>노드를 고르고 화면 가운데로 — Main 노드는 그 카드, Decorator · Service는 그 줄 (가진 카드를 가운데로). 화면에 없는 노드면 false</summary>
    public bool SelectNode(BehaviorNode node)
    {
        if (_document == null)
            return false;
        if (node is BehaviorMainNode main && _cards.TryGetValue(main, out BehaviorNodeCard card))
        {
            ClearSelection();
            AddToSelection(card);
            CenterOn(card);
            return true;
        }
        if (node is BehaviorAuxiliaryNode auxiliary && BehaviorTreeEditing.TryGetOwner(_document, auxiliary, out BehaviorMainNode owner)
            && _cards.TryGetValue(owner, out BehaviorNodeCard ownerCard))
        {
            SelectAuxiliary(owner, auxiliary);
            CenterOn(ownerCard);
            return true;
        }
        return false;
    }

    /// <summary>ROOT 카드를 고르고 화면 가운데로 (트리 설정)</summary>
    public void SelectRoot()
    {
        if (_rootCard == null)
            return;
        ClearSelection();
        AddToSelection(_rootCard);
        CenterOn(_rootCard);
    }

    /// <summary>Decorator · Service 줄 하나를 고른다 — 카드 · 선 고르기와 고른 줄은 풀고, 그래프에 포커스를 둔다 (Delete · 단축키)</summary>
    internal void SelectAuxiliary(BehaviorMainNode owner, BehaviorAuxiliaryNode node)
    {
        ClearSelection();
        SetSelectedAuxiliary(owner, node);
        Focus();
        _selectionJob.ExecuteLater(0);
    }

    /// <summary>카드 (ROOT 포함)의 고르기가 바뀌었다 — 알림은 다음 갱신에 한 번</summary>
    internal void OnCardSelectionChanged()
    {
        _selectionJob.ExecuteLater(0);
    }

    /// <summary>고르기를 비운다 — 고른 Decorator · Service 줄도 (빈 곳 · 선 · 다른 카드를 누름, 사각형 고르기). 줄 고르기가 풀리면 알린다</summary>
    public override void ClearSelection()
    {
        base.ClearSelection();
        ClearAuxiliarySelection();
    }

    /// <summary>카드 · 선을 고르면 고른 줄은 푼다 (Ctrl로 더해 고를 때도) — 줄과 카드 · 선은 함께 고르지 않는다</summary>
    public override void AddToSelection(ISelectable selectable)
    {
        base.AddToSelection(selectable);
        ClearAuxiliarySelection();
    }

    private void ClearAuxiliarySelection()
    {
        if (_selectedAuxiliary == null)
            return;
        SetSelectedAuxiliary(null, null);
        _selectionJob.ExecuteLater(0);
    }

    private void SetSelectedAuxiliary(BehaviorMainNode owner, BehaviorAuxiliaryNode node)
    {
        _selectedAuxiliaryOwner = node != null ? owner : null;
        _selectedAuxiliary = node;
        foreach (BehaviorNodeCard card in _cards.Values)
        {
            IReadOnlyList<BehaviorAuxiliaryRow> rows = card.Rows;
            for (int i = 0; i < rows.Count; i++)
                rows[i].SetSelected(node != null && rows[i].AuxiliaryNode == node);
        }
    }

    /// <summary>요소를 화면 가운데로 — 확대는 그대로. 패널에 붙지 않았으면 (화면 크기를 모름 — 시험) 하지 않는다</summary>
    private void CenterOn(GraphElement element)
    {
        if (panel == null)
            return;
        Vector2 center = PositionOf(element) + SizeOf(element) / 2f;
        Vector2 view = layout.size / 2f;
        float zoom = scale;
        UpdateViewTransform(new Vector3(view.x - center.x * zoom, view.y - center.y * zoom, 0f), new Vector3(zoom, zoom, 1f));
    }
    #endregion

    #region 검사 표시
    /// <summary>검사 결과를 카드 · 줄 · ROOT 카드에 ✕ · ⚠로 보인다 (다시 그려도 남는다). 노드 문제는 그 카드 · 줄에, 트리 · 설정 · Blackboard 문제는 ROOT 카드에</summary>
    public void SetIssues(IReadOnlyList<BehaviorTreeCheckItem> items)
    {
        _issues.Clear();
        if (items != null)
            _issues.AddRange(items);
        ApplyIssues();
    }

    private void ApplyIssues()
    {
        if (_rootCard == null)
            return;
        var marks = new Dictionary<BehaviorNode, IssueMark>();
        IssueMark tree = default;
        for (int i = 0; i < _issues.Count; i++)
        {
            BehaviorTreeCheckItem item = _issues[i];
            if (item.Place == EBehaviorCheckPlace.Node && item.Node != null)
            {
                marks.TryGetValue(item.Node, out IssueMark mark);
                marks[item.Node] = mark.With(item);
            }
            else
            {
                tree = tree.With(item);
            }
        }
        _rootCard.SetIssue(tree.Severity, tree.Text);
        foreach (BehaviorNodeCard card in _cards.Values)
        {
            marks.TryGetValue(card.MainNode, out IssueMark cardMark);
            card.SetIssue(cardMark.Severity, cardMark.Text);
            IReadOnlyList<BehaviorAuxiliaryRow> rows = card.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                marks.TryGetValue(rows[i].AuxiliaryNode, out IssueMark rowMark);
                rows[i].SetIssue(rowMark.Severity, rowMark.Text);
            }
        }
    }

    /// <summary>한 자리의 검사 표시 — 가장 무거운 무게와 문장들 ("✕ 칸: 문장" 줄마다)</summary>
    private struct IssueMark
    {
        public EBehaviorMessageSeverity? Severity;
        public string Text;

        public IssueMark With(BehaviorTreeCheckItem item)
        {
            string line = (item.IsError ? "✕ " : "⚠ ") + (item.Field.Length > 0 ? item.Field + ": " : "") + item.Text;
            return new IssueMark
            {
                Severity = item.IsError || Severity == EBehaviorMessageSeverity.Error ? EBehaviorMessageSeverity.Error : EBehaviorMessageSeverity.Warning,
                Text = string.IsNullOrEmpty(Text) ? line : Text + "\n" + line,
            };
        }
    }
    #endregion

    #region 정리 배치 · 화면 맞춤
    /// <summary>정리 배치 (BehaviorTreeLayout) — Undo를 남기고 카드를 옮긴 뒤 화면을 맞춘다</summary>
    public void ArrangeAll()
    {
        if (_document == null)
            return;
        Record("Behavior Tree 정리 배치");
        ArrangeNow();
        _documentChanged?.Invoke();
        FrameAllCards();
    }

    /// <summary>모든 카드가 보이게 — 보이는 문서가 없거나 패널에 붙지 않았으면 (화면 크기를 모름 — 시험) 하지 않는다</summary>
    public void FrameAllCards()
    {
        if (_document != null && panel != null)
            FrameAll();
    }

    /// <summary>
    /// 화면 맞춤 (FrameAll)의 범위 — 카드마다 문서의 자리 + 잰 크기. 카드를 옮긴 직후에도 맞다 (배치를 기다리지 않는다).
    /// 보이는 문서가 없으면 GraphView 기본 (배치된 요소)
    /// </summary>
    public override Rect CalculateRectToFitAll(VisualElement container)
    {
        if (_document == null || _rootCard == null)
            return base.CalculateRectToFitAll(container);

        Vector2 min = _document.RootCardPosition;
        Vector2 max = min + SizeOf(_rootCard);
        foreach (KeyValuePair<BehaviorMainNode, BehaviorNodeCard> pair in _cards)
        {
            if (_document.TryGetPosition(pair.Key.NodeId, out Vector2 position) == false)
                continue;
            min = Vector2.Min(min, position);
            max = Vector2.Max(max, position + SizeOf(pair.Value));
        }
        return new Rect(min, max - min);
    }

    /// <summary>문서의 카드 자리를 정리 배치로 바꾸고 카드를 옮긴다 — 카드 크기는 잰 크기 (재기 전이면 어림)</summary>
    private void ArrangeNow()
    {
        BehaviorTreeLayout.Arrange(_document, SizeOf(_rootCard), CardSizeOf);
        MoveCardsToDocumentPositions();
    }

    /// <summary>
    /// 저장된 자리를 쓰는 처음 배치 — 가로 차례가 자식 차례와 다른 카드를 맞추고 (BehaviorTreeLayout.MatchOrder — 카드 하나를 옮길 때 자식 차례가 옛 가로 자리 차례로 바뀌지 않게),
    /// 자리가 없는 카드만 놓는다 (PlaceMissing). 저장된 자리를 옮기지 않았으면 true
    /// </summary>
    private bool PlaceFromSavedPositions()
    {
        int moved = BehaviorTreeLayout.MatchOrder(_document, CardSizeOf);
        BehaviorTreeLayout.PlaceMissing(_document, SizeOf(_rootCard), CardSizeOf);
        MoveCardsToDocumentPositions();
        return moved == 0;
    }

    /// <summary>노드 카드의 크기 — 재기 전이면 어림</summary>
    private Vector2 CardSizeOf(BehaviorMainNode node)
    {
        return _cards.TryGetValue(node, out BehaviorNodeCard card) ? SizeOf(card) : EstimatedCardSize;
    }

    /// <summary>ROOT 카드 · 카드를 문서의 자리로</summary>
    private void MoveCardsToDocumentPositions()
    {
        _rootCard.SetPosition(new Rect(_document.RootCardPosition, Vector2.zero));
        foreach (KeyValuePair<BehaviorMainNode, BehaviorNodeCard> pair in _cards)
        {
            if (_document.TryGetPosition(pair.Key.NodeId, out Vector2 position))
                pair.Value.SetPosition(new Rect(position, Vector2.zero));
        }
    }

    private static Vector2 SizeOf(VisualElement card)
    {
        return IsMeasured(card) ? card.layout.size : EstimatedCardSize;
    }

    private static bool IsMeasured(VisualElement card)
    {
        return float.IsNaN(card.layout.width) == false && card.layout.width > 0f;
    }

    private static bool HasMissingPlacement(BehaviorTreeDocumentAsset document)
    {
        var nodes = new List<BehaviorMainNode>();
        BehaviorTreeEditing.CollectMainNodes(document, nodes);
        for (int i = 0; i < nodes.Count; i++)
        {
            if (document.TryGetPosition(nodes[i].NodeId, out _) == false)
                return true;
        }
        return false;
    }

    /// <summary>카드 크기가 모두 정해지면 (카드마다 첫 GeometryChangedEvent) 기다리던 정리 배치 · 화면 맞춤을 한다. 그 사이 다시 그리면 새 카드로 다시 센다</summary>
    private void WaitForCardSizes()
    {
        _unmeasuredCards = 0;
        CountUnmeasured(_rootCard);
        foreach (BehaviorNodeCard card in _cards.Values)
            CountUnmeasured(card);
        if (_unmeasuredCards == 0)
            AfterCardsMeasured();
    }

    private void CountUnmeasured(VisualElement card)
    {
        if (IsMeasured(card))
            return;
        _unmeasuredCards++;
        card.RegisterCallback<GeometryChangedEvent>(OnCardMeasured);
    }

    private void OnCardMeasured(GeometryChangedEvent evt)
    {
        ((VisualElement)evt.currentTarget).UnregisterCallback<GeometryChangedEvent>(OnCardMeasured);
        _unmeasuredCards--;
        if (_unmeasuredCards == 0)
            AfterCardsMeasured();
    }

    /// <summary>카드 크기를 모두 잰 뒤 — 기다리던 처음 배치 · 화면 맞춤. 패널에 붙지 않은 그래프 (시험)는 크기를 재지 않아 시험이 부른다</summary>
    internal void AfterCardsMeasured()
    {
        if (_document == null)
            return;
        if (_arrangeWhenMeasured)
        {
            _arrangeWhenMeasured = false;
            bool layoutUnedited = _document.IsLayoutAtBaseline(); // 트리를 열기만 해도 `*`가 붙지 않게 — 그 전에 배치 편집이 있었으면 (새 트리 · 되살린 편집) 그대로
            bool savedPositionsKept = true;
            if (_document.Placements.Count == 0)
                ArrangeNow(); // 처음 자리 — Undo를 남기지 않는다
            else
                savedPositionsKept = PlaceFromSavedPositions(); // 자리가 있는 트리 (배치 파일) — 있던 자리는 그대로
            if (layoutUnedited && savedPositionsKept)
                _document.MarkLayoutBaseline(); // 저장된 자리를 옮겼으면 (가로 차례 맞추기) `*` — 저장하면 맞춘 자리가 배치 파일에
        }
        if (_frameWhenMeasured)
        {
            _frameWhenMeasured = false;
            FrameAllCards();
        }
    }
    #endregion

    #region GraphView가 알린 바꾸기
    /// <summary>
    /// 지우기 (선 · 카드 — 키 · 메뉴) · 잇기 (선 놓기) · 옮기기를 문서에 옮긴다. 지울 때는 선 먼저 (이은 자리를 끊고), 그다음 카드.
    /// 이을 수 없는 선 (BehaviorTreeEditing.CanConnect가 아님)은 만들 선에서 뺀다. Undo는 문서가 바뀔 때만 남긴다.
    /// </summary>
    private GraphViewChange OnGraphViewChanged(GraphViewChange change)
    {
        if (_document == null)
            return change;

        bool structureChanged = false;
        bool orderChanged = false;
        if (change.elementsToRemove != null && RemovesSomething(change.elementsToRemove))
        {
            Record("Behavior Tree 지우기");
            foreach (GraphElement element in change.elementsToRemove)
            {
                if (element is Edge edge && TryGetEnds(edge, out BehaviorMainNode parent, out BehaviorMainNode child))
                    structureChanged |= BehaviorTreeEditing.Disconnect(_document, parent, child);
            }
            foreach (GraphElement element in change.elementsToRemove)
            {
                if (element is BehaviorNodeCard card)
                    structureChanged |= BehaviorTreeEditing.Remove(_document, card.MainNode);
            }
        }
        if (change.edgesToCreate != null && ConnectsSomething(change.edgesToCreate))
        {
            Record("Behavior Tree 잇기");
            for (int i = change.edgesToCreate.Count - 1; i >= 0; i--)
            {
                Edge edge = change.edgesToCreate[i];
                if (TryGetEnds(edge, out BehaviorMainNode parent, out BehaviorMainNode child) && BehaviorTreeEditing.Connect(_document, parent, child))
                    structureChanged = true;
                else
                    change.edgesToCreate.RemoveAt(i);
            }
        }
        else if (change.edgesToCreate != null)
        {
            change.edgesToCreate.Clear(); // 이을 수 있는 선이 없다
        }
        if (change.movedElements != null && MovesSomething(change.movedElements))
        {
            Record("Behavior Tree 카드 옮기기");
            orderChanged = ApplyMoves(change.movedElements);
        }

        if (structureChanged)
            ScheduleRebuild();
        else if (orderChanged)
            RefreshOrder();
        _documentChanged?.Invoke();
        return change;
    }

    /// <summary>지울 것 가운데 문서에 있는 것 — 이어진 선 · 문서의 노드 카드</summary>
    private bool RemovesSomething(List<GraphElement> elements)
    {
        for (int i = 0; i < elements.Count; i++)
        {
            if (elements[i] is Edge edge && TryGetEnds(edge, out BehaviorMainNode parent, out BehaviorMainNode child) && BehaviorTreeEditing.IsConnected(_document, parent, child))
                return true;
            if (elements[i] is BehaviorNodeCard card && BehaviorTreeEditing.IsInDocument(_document, card.MainNode))
                return true;
        }
        return false;
    }

    /// <summary>만들 선 가운데 이을 수 있는 것이 있는가</summary>
    private bool ConnectsSomething(List<Edge> edges)
    {
        for (int i = 0; i < edges.Count; i++)
        {
            if (TryGetEnds(edges[i], out BehaviorMainNode parent, out BehaviorMainNode child) && BehaviorTreeEditing.CanConnect(_document, parent, child))
                return true;
        }
        return false;
    }

    /// <summary>옮긴 카드 가운데 문서의 자리와 다른 것이 있는가 — 누르기만 한 끌기는 Undo를 남기지 않게. 지금 그래프의 카드만 본다 (IsShown)</summary>
    private bool MovesSomething(List<GraphElement> moved)
    {
        for (int i = 0; i < moved.Count; i++)
        {
            if (IsShown(moved[i]) == false)
                continue;
            Vector2 position = PositionOf(moved[i]);
            if (moved[i] is BehaviorRootCard && position != _document.RootCardPosition)
                return true;
            if (moved[i] is BehaviorNodeCard card && (_document.TryGetPosition(card.MainNode.NodeId, out Vector2 stored) == false || stored != position))
                return true;
        }
        return false;
    }

    /// <summary>선의 두 끝 — 위 카드의 노드 (ROOT 카드면 null — 루트 자리)와 아래 카드의 노드. 이 화면의 카드가 아니면 false</summary>
    private static bool TryGetEnds(Edge edge, out BehaviorMainNode parent, out BehaviorMainNode child)
    {
        parent = null;
        child = null;
        var lower = edge.input?.node as BehaviorNodeCard;
        if (lower == null)
            return false;
        child = lower.MainNode;
        Node upper = edge.output?.node;
        if (upper is BehaviorNodeCard upperCard)
        {
            parent = upperCard.MainNode;
            return true;
        }
        return upper is BehaviorRootCard;
    }

    /// <summary>옮긴 카드의 자리를 문서에 적고, 옮긴 노드의 부모마다 자식 차례를 가로 자리로 다시 정한다. 차례가 바뀌었으면 true. 지금 그래프의 카드만 (IsShown). O(옮긴 카드 수 × 노드 수)</summary>
    private bool ApplyMoves(List<GraphElement> moved)
    {
        var parents = new HashSet<BehaviorComposite>();
        foreach (GraphElement element in moved)
        {
            if (IsShown(element) == false)
                continue;
            Vector2 position = PositionOf(element);
            if (element is BehaviorRootCard)
            {
                _document.RootCardPosition = position;
                continue;
            }
            var card = element as BehaviorNodeCard;
            if (card == null)
                continue;
            _document.SetPosition(card.MainNode.NodeId, position);
            if (BehaviorTreeEditing.TryGetParent(_document, card.MainNode, out BehaviorComposite parent))
                parents.Add(parent);
        }

        bool changed = false;
        foreach (BehaviorComposite parent in parents)
            changed |= BehaviorTreeEditing.SortChildren(_document, parent);
        return changed;
    }

    /// <summary>
    /// 지금 그래프의 카드 (ROOT 카드 포함)인가 — 끌기를 시작할 때 잡힌 카드는 놓을 때 그대로 알려 오므로, 그사이 다시 그렸거나 다른 문서를 보였으면 (멈춘 순간에 창이 트리를 바꿈) 옛 카드다. O(1)
    /// </summary>
    private bool IsShown(GraphElement element)
    {
        if (element is BehaviorRootCard root)
            return ReferenceEquals(root, _rootCard);
        return element is BehaviorNodeCard card && _cards.TryGetValue(card.MainNode, out BehaviorNodeCard shown) && ReferenceEquals(shown, card);
    }

    /// <summary>카드의 왼쪽 위 — 카드 자리는 SetPosition이 정한 left · top (끌기의 마지막 자리 그대로). 정하지 않았으면 GetPosition</summary>
    private static Vector2 PositionOf(GraphElement element)
    {
        StyleLength left = element.style.left;
        StyleLength top = element.style.top;
        if (left.keyword == StyleKeyword.Undefined && top.keyword == StyleKeyword.Undefined)
            return new Vector2(left.value.value, top.value.value);
        return element.GetPosition().position;
    }

    /// <summary>
    /// 잇기를 끌 때 놓을 수 있는 칸 — 문서 규칙 (BehaviorTreeEditing.CanConnect)과 같다. 이미 이어진 칸도 든다 — 있던 선을 끌어 그 칸에 다시 놓으면 그대로 이어진다.
    /// O(노드 수) — 끌기를 시작할 때 한 번
    /// </summary>
    public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
    {
        var ports = new List<Port>();
        if (_document == null)
            return ports;

        if (startPort.direction == Direction.Output)
        {
            Node upper = startPort.node;
            BehaviorMainNode parent = (upper as BehaviorNodeCard)?.MainNode; // ROOT 카드면 null — 루트 자리
            if (parent == null && upper is BehaviorRootCard == false)
                return ports;
            var children = new List<BehaviorMainNode>();
            BehaviorTreeEditing.CollectConnectableChildren(_document, parent, children);
            for (int i = 0; i < children.Count; i++)
            {
                if (_cards.TryGetValue(children[i], out BehaviorNodeCard card))
                    ports.Add(card.InputPort);
            }
            return ports;
        }

        var lower = startPort.node as BehaviorNodeCard;
        if (lower == null)
            return ports;
        var parents = new List<BehaviorComposite>();
        if (BehaviorTreeEditing.CollectConnectableParents(_document, lower.MainNode, parents))
            ports.Add(_rootCard.OutputPort);
        for (int i = 0; i < parents.Count; i++)
        {
            if (_cards.TryGetValue(parents[i], out BehaviorNodeCard card))
                ports.Add(card.OutputPort);
        }
        return ports;
    }
    #endregion

    #region 메뉴 · 노드 추가
    /// <summary>
    /// 빈 곳: 노드 추가 · 붙이기 (누른 자리에) · 정리 배치 · 화면 맞춤, 선: 끊기. 카드 · Decorator · Service 줄의 메뉴는 그쪽이 만든다.
    /// GraphView 기본 메뉴 (영어 Cut · Copy · Paste · Delete · Duplicate)는 넣지 않는다 — 같은 일을 이 메뉴 · 카드 · 줄 메뉴가 한다
    /// </summary>
    public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
    {
        if (_document == null)
            return;
        if (evt.target is GraphView)
        {
            Vector2 position = evt.mousePosition;
            evt.menu.AppendAction("노드 추가…", _ => OpenAddNodePicker(position, EAttach.None, null));
            evt.menu.AppendAction("붙이기", _ => PasteAt(position), canPaste ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("정리 배치", _ => ArrangeAll());
            evt.menu.AppendAction("화면 맞춤", _ => FrameAllCards());
        }
        else if (evt.target is Edge edge)
        {
            evt.menu.AppendAction("끊기", _ => DeleteElements(new GraphElement[] { edge }));
        }
    }

    /// <summary>
    /// 그래프 단축키 — 그래프 (빈 곳 · 카드)에 포커스가 있을 때만 받는다 (GraphView 기본은 창 전체에서 받는다 — 생성자에서 끔). 글자는 GraphView 기본과 같다:
    /// A 화면 맞춤 · O 원점 · [ ] 이전 · 다음 카드 (고르고 화면 이동) · Space 노드 추가 (마우스가 그래프 안일 때). 마우스를 잡은 요소가 있으면 (끌기 중) 하지 않는다
    /// </summary>
    private void OnKeyDown(KeyDownEvent evt)
    {
        if (panel == null || panel.GetCapturingElement(PointerId.mousePointerId) != null)
            return;
        bool handled = true;
        switch (evt.character)
        {
            case 'a':
                FrameAllCards();
                break;
            case 'o':
                FrameOrigin();
                break;
            case '[':
                FramePrev();
                break;
            case ']':
                FrameNext();
                break;
            case ' ':
                handled = _document != null && worldBound.Contains(evt.originalMousePosition);
                if (handled)
                    OpenAddNodePicker(evt.originalMousePosition, EAttach.None, null);
                break;
            default:
                handled = false;
                break;
        }
        if (handled)
        {
            evt.StopPropagation();
            evt.imguiEvent?.Use();
        }
    }

    /// <summary>카드의 칸에서 끈 선을 빈 곳에 놓았다 — 그 자리에 새 노드를 만들어 그 칸에 잇는다 (ROOT · Composite의 출력 칸: 자식, 입력 칸: 부모 Composite)</summary>
    internal void OnPortDroppedOutside(BehaviorGraphPort port, Vector2 position)
    {
        if (_document == null)
            return;
        Node owner = port.node;
        if (port.direction == Direction.Output)
        {
            if (owner is BehaviorRootCard)
                OpenAddNodePicker(position, EAttach.AsRoot, null);
            else if (owner is BehaviorNodeCard upper)
                OpenAddNodePicker(position, EAttach.AsChild, upper.MainNode);
        }
        else if (owner is BehaviorNodeCard lower)
        {
            OpenAddNodePicker(position, EAttach.AsParent, lower.MainNode);
        }
    }

    /// <summary>
    /// 노드 고르기를 연다 (position — 패널 좌표). 고르면 그 자리에 카드를 만들고 (attach에 따라 잇는다), 그 카드를 고른다.
    /// 고르기를 연 사이 다른 문서를 보이면 하지 않는다. 이을 노드가 그 사이 없어졌으면 (Undo 등) 잇지 않는다.
    /// </summary>
    private void OpenAddNodePicker(Vector2 position, EAttach attach, BehaviorMainNode other)
    {
        BehaviorTreeDocumentAsset document = _document;
        if (document == null)
            return;

        Vector2 graphPosition = contentViewContainer.WorldToLocal(position);
        Vector2 cardPosition = graphPosition;
        if (attach == EAttach.AsParent)
            cardPosition -= new Vector2(EstimatedCardSize.x / 2f, EstimatedCardSize.y); // 놓은 자리 위로
        else if (attach != EAttach.None)
            cardPosition -= new Vector2(EstimatedCardSize.x / 2f, 0f); // 놓은 자리 아래로

        bool compositeOnly = attach == EAttach.AsRoot || attach == EAttach.AsParent;
        string header = compositeOnly ? "Composite — 이름·키·설명으로 검색" : "Composite · Task — 이름·키·설명으로 검색";
        BehaviorNodePicker.Show(ScreenRectAt(position), header, document.Registry, compositeOnly ? CompositeKinds : MainKinds, info =>
        {
            if (_document == document)
                AddNode(info, cardPosition, attach, other);
        });
    }

    /// <summary>
    /// 노드를 만들어 cardPosition (그래프 좌표 — 카드 왼쪽 위)에 두고 attach에 따라 잇는다 — 이을 수 없으면 (그 사이 이을 노드가 없어짐 등) 이어지지 않은 노드로 둔다.
    /// 만든 카드를 고른다. 만든 노드를 반환한다 (만들지 못하면 null)
    /// </summary>
    internal BehaviorMainNode AddNode(BehaviorNodeRegistry.NodeInfo info, Vector2 cardPosition, EAttach attach, BehaviorMainNode other)
    {
        if (_document == null || TryCreate(_document, info, out BehaviorNode created) == false || created is BehaviorMainNode == false)
            return null;
        var node = (BehaviorMainNode)created;
        Record("Behavior Tree 노드 추가");
        node.NodeId = BehaviorNodeIds.Next(_document, node);
        BehaviorTreeEditing.AddLoose(_document, node, cardPosition);
        if (attach == EAttach.AsRoot)
            BehaviorTreeEditing.Connect(_document, null, node);
        else if (attach == EAttach.AsChild && other != null)
            BehaviorTreeEditing.Connect(_document, other, node);
        else if (attach == EAttach.AsParent)
            BehaviorTreeEditing.Connect(_document, node, other);
        RebuildNow();
        SelectCardOf(node);
        _documentChanged?.Invoke();
        return node;
    }

    /// <summary>패널 좌표의 점 → 화면의 빈 사각형 (팝업 자리)</summary>
    private Rect ScreenRectAt(Vector2 position)
    {
        Vector2 local = _window.rootVisualElement.WorldToLocal(position);
        return new Rect(_window.position.position + local, Vector2.zero);
    }

    private static bool TryCreate(BehaviorTreeDocumentAsset document, BehaviorNodeRegistry.NodeInfo info, out BehaviorNode node)
    {
        if (document.Registry.TryCreate(info.Key, info.Kind, out node, out string error))
            return true;
        Debug.LogWarning($"[Behavior Tree Editor] 노드를 만들지 못함 — {error}");
        return false;
    }
    #endregion

    #region 카드 · 줄의 바꾸기
    /// <summary>Decorator · Service 고르기를 연다 — 고르면 owner의 목록 끝에 더한다</summary>
    internal void AddAuxiliary(BehaviorMainNode owner, EBehaviorNodeKind kind, Vector2 position)
    {
        BehaviorTreeDocumentAsset document = _document;
        if (document == null)
            return;

        string header = kind == EBehaviorNodeKind.Decorator ? "Decorator — 이름·키·설명으로 검색" : "Service — 이름·키·설명으로 검색";
        BehaviorNodePicker.Show(ScreenRectAt(position), header, document.Registry, new[] { kind }, info =>
        {
            if (_document == document)
                AddAuxiliaryNode(owner, info);
        });
    }

    /// <summary>Decorator · Service를 만들어 owner의 목록 끝에 더한다. owner가 문서에 없으면 (그 사이 지움 등) 하지 않는다. 만든 노드를 반환한다 (못 만들면 null)</summary>
    internal BehaviorAuxiliaryNode AddAuxiliaryNode(BehaviorMainNode owner, BehaviorNodeRegistry.NodeInfo info)
    {
        if (_document == null || BehaviorTreeEditing.IsInDocument(_document, owner) == false || TryCreate(_document, info, out BehaviorNode created) == false)
            return null;
        if (created is BehaviorDecorator == false && created is BehaviorService == false)
            return null;
        Record(created is BehaviorDecorator ? "Behavior Tree Decorator 추가" : "Behavior Tree Service 추가");
        created.NodeId = BehaviorNodeIds.Next(_document, created);
        if (created is BehaviorDecorator decorator)
            BehaviorTreeEditing.AddDecorator(owner, decorator);
        else
            BehaviorTreeEditing.AddService(owner, (BehaviorService)created);
        AfterStructureChange();
        SelectAuxiliary(owner, (BehaviorAuxiliaryNode)created);
        return (BehaviorAuxiliaryNode)created;
    }

    internal void RemoveAuxiliary(BehaviorMainNode owner, BehaviorAuxiliaryNode node)
    {
        if (_document == null || BehaviorTreeEditing.IndexOfAuxiliary(owner, node, out _) < 0)
            return;
        Record("Behavior Tree 지우기");
        BehaviorTreeEditing.RemoveAuxiliary(_document, owner, node);
        AfterStructureChange();
    }

    /// <summary>Decorator · Service를 같은 목록 안에서 delta 칸 옮긴다 (−1 위로 · +1 아래로) — 목록 끝을 넘으면 하지 않는다</summary>
    internal void MoveAuxiliary(BehaviorMainNode owner, BehaviorAuxiliaryNode node, int delta)
    {
        if (_document == null)
            return;
        int index = BehaviorTreeEditing.IndexOfAuxiliary(owner, node, out int count);
        if (index < 0 || delta == 0 || index + delta < 0 || index + delta >= count)
            return;
        Record("Behavior Tree 차례 바꾸기");
        BehaviorTreeEditing.MoveAuxiliary(owner, node, delta);
        AfterStructureChange();
    }

    /// <summary>이어진 자리 (루트 자리 · 부모 Composite)에서 끊는다 — 부분 트리는 이어지지 않은 노드로</summary>
    internal void DisconnectFromParent(BehaviorMainNode node)
    {
        if (TryGetParentSlot(node, out BehaviorMainNode parent) == false)
            return;
        Record("Behavior Tree 끊기");
        if (BehaviorTreeEditing.Disconnect(_document, parent, node))
            AfterStructureChange();
    }

    internal bool HasParent(BehaviorMainNode node)
    {
        return TryGetParentSlot(node, out _);
    }

    /// <summary>카드를 지운다 — 고른 카드면 고른 것 모두 (Delete 키와 같다), 아니면 그 카드만. 지우기는 GraphView를 거쳐 문서에 옮긴다 (OnGraphViewChanged)</summary>
    internal void DeleteCard(BehaviorNodeCard card)
    {
        if (card.selected == false)
        {
            ClearSelection();
            AddToSelection(card);
        }
        DeleteSelection();
    }

    /// <summary>Delete · 메뉴 지우기 — 고른 Decorator · Service 줄이 있으면 그것을, 아니면 GraphView 기본 (고른 카드 · 선 — OnGraphViewChanged가 문서에 옮긴다)</summary>
    public override EventPropagation DeleteSelection()
    {
        if (_selectedAuxiliary != null && selection.Count == 0)
        {
            RemoveAuxiliary(_selectedAuxiliaryOwner, _selectedAuxiliary);
            return EventPropagation.Stop;
        }
        return base.DeleteSelection();
    }

    protected override bool canDeleteSelection => _selectedAuxiliary != null || base.canDeleteSelection;

    /// <summary>노드가 이어진 자리 — 루트 자리면 parent null로 true, 부모 Composite면 그 부모로 true, 이어지지 않았으면 false</summary>
    private bool TryGetParentSlot(BehaviorMainNode node, out BehaviorMainNode parent)
    {
        parent = null;
        if (_document == null || node == null)
            return false;
        if (_document.Tree.Root == node)
            return true;
        bool found = BehaviorTreeEditing.TryGetParent(_document, node, out BehaviorComposite composite);
        parent = composite;
        return found;
    }

    private void AfterStructureChange()
    {
        RebuildNow();
        _documentChanged?.Invoke();
    }

    /// <summary>문서를 바꾸기 전 — 사본 전체를 Undo에 남긴다</summary>
    private void Record(string undoName)
    {
        Undo.RegisterCompleteObjectUndo(_document, undoName);
    }
    #endregion
}