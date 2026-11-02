using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 그래프 카드의 모양 — 색 (Unreal처럼 Decorator 파랑 · Service 초록) · 크기 · 카드 몸 · 머리 · 칸 줄 · 검사 표시 (✕ · ⚠) · 메모 줄 ·
/// 디버거 표시 (진행 경로 테두리 · 실행 값 글 · 조건 결과 · breakpoint ●).
/// 카드 몸은 기본 Node의 제목 줄 · 칸 영역 대신 위에서 아래로: [입력 칸 줄] [가운데 — 카드가 채운다] [출력 칸 줄].
/// </summary>
public static class BehaviorGraphStyle
{
    public static readonly Color RootHeader = new Color(0.13f, 0.13f, 0.13f);
    public static readonly Color CompositeHeader = new Color(0.30f, 0.30f, 0.32f);
    public static readonly Color TaskHeader = new Color(0.38f, 0.25f, 0.53f);
    public static readonly Color DecoratorRow = new Color(0.17f, 0.34f, 0.60f);
    public static readonly Color ServiceRow = new Color(0.15f, 0.42f, 0.22f);
    public static readonly Color PortStrip = new Color(0.08f, 0.08f, 0.08f, 0.9f);
    public static readonly Color OrderBadge = new Color(0f, 0f, 0f, 0.45f);
    public static readonly Color SubText = new Color(1f, 1f, 1f, 0.6f);
    public static readonly Color ErrorMark = new Color(1f, 0.35f, 0.3f);
    public static readonly Color WarningMark = new Color(1f, 0.8f, 0.25f);
    public static readonly Color SelectedRowBorder = new Color(1f, 1f, 1f, 0.9f);
    public static readonly Color MemoBackground = new Color(1f, 0.95f, 0.6f, 0.12f);
    public static readonly Color DebugPath = new Color(1f, 0.72f, 0.2f);         // 진행 경로 — 카드 테두리 · 선
    public static readonly Color DebugRunning = new Color(0.45f, 0.95f, 0.45f);  // 진행 중 Task
    public static readonly Color DebugAborting = new Color(1f, 0.45f, 0.25f);    // 끊기를 미루는 Task
    public static readonly Color DebugHeld = new Color(1f, 0.25f, 0.25f);        // 멈춘 노드 (breakpoint)
    public static readonly Color DebugText = new Color(1f, 0.85f, 0.45f);        // 실행 값 글
    public static readonly Color ConditionTrue = new Color(0.45f, 0.95f, 0.45f);
    public static readonly Color ConditionFalse = new Color(1f, 0.4f, 0.35f);
    public static readonly Color BreakpointMark = new Color(0.95f, 0.2f, 0.2f);

    public const float CardMinWidth = 170f;
    public const float DebugPathBorder = 2f;   // 진행 경로 카드의 테두리 두께
    public const float DebugActiveBorder = 3f; // 진행 중 · 끊는 중 · 멈춘 카드의 테두리 두께
    public const float LooseOpacity = 0.65f; // 이어지지 않은 노드의 카드 — 실행 순서에 들지 않는다

    /// <summary>
    /// 카드 몸을 만들고 가운데를 반환한다. 입력 · 출력 칸 묶음 (inputContainer · outputContainer)을 몸의 위 · 아래로 옮긴다 —
    /// GraphView는 카드를 지울 때 이 둘의 칸에서 이은 선을 모은다. 기본 제목 줄 · 칸 영역 · 접기 단추는 감춘다 (접기는 칸 묶음을 원래 자리로 옮기므로 쓰지 않는다).
    /// </summary>
    public static VisualElement BuildBody(Node card)
    {
        var body = new VisualElement();
        body.Add(card.inputContainer);
        var middle = new VisualElement();
        body.Add(middle);
        body.Add(card.outputContainer);
        StylePortStrip(card.inputContainer);
        StylePortStrip(card.outputContainer);

        card.titleContainer.style.display = DisplayStyle.None;
        VisualElement contents = card.mainContainer.Q("contents");
        if (contents != null)
            contents.style.display = DisplayStyle.None;
        card.mainContainer.Insert(0, body);
        card.style.minWidth = CardMinWidth;
        return middle;
    }

    /// <summary>카드 머리 — 이름 (굵게) · 그 아래 작은 글 (이름표 등 — subtitleLabel, 비면 감춤) · 오른쪽 실행 순서 번호 (badge — 부르는 쪽이 글자를 넣는다)</summary>
    public static VisualElement Header(string title, string subtitle, Color color, out Label subtitleLabel, out Label badge)
    {
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.backgroundColor = color;
        header.style.paddingLeft = 8;
        header.style.paddingRight = 6;
        header.style.paddingTop = 5;
        header.style.paddingBottom = 5;

        var titles = new VisualElement();
        titles.style.flexGrow = 1;
        var titleLabel = new Label(title);
        titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        titleLabel.style.color = Color.white;
        titles.Add(titleLabel);
        subtitleLabel = new Label();
        subtitleLabel.style.fontSize = 10;
        subtitleLabel.style.color = SubText;
        titles.Add(subtitleLabel);
        SetText(subtitleLabel, subtitle);
        header.Add(titles);

        badge = new Label();
        badge.style.fontSize = 11;
        badge.style.unityFontStyleAndWeight = FontStyle.Bold;
        badge.style.color = Color.white;
        badge.style.backgroundColor = OrderBadge;
        badge.style.minWidth = 18;
        badge.style.unityTextAlign = TextAnchor.MiddleCenter;
        badge.style.paddingLeft = 4;
        badge.style.paddingRight = 4;
        badge.style.borderTopLeftRadius = 8;
        badge.style.borderTopRightRadius = 8;
        badge.style.borderBottomLeftRadius = 8;
        badge.style.borderBottomRightRadius = 8;
        badge.style.marginLeft = 6;
        header.Add(badge);
        return header;
    }

    /// <summary>글자를 넣고, 빈 글이면 감춘다</summary>
    public static void SetText(Label label, string text)
    {
        label.text = text ?? "";
        label.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
    }

    /// <summary>검사 표시 — 에러 "✕" (빨강) · 경고 "⚠" (노랑), 처음에는 감춤 (SetIssue)</summary>
    public static Label IssueMark()
    {
        var mark = new Label();
        mark.style.unityFontStyleAndWeight = FontStyle.Bold;
        mark.style.marginLeft = 4;
        mark.style.marginRight = 2;
        mark.style.display = DisplayStyle.None;
        return mark;
    }

    /// <summary>검사 표시를 맞춘다 — severity가 null이면 감춘다. 툴팁 = 그 노드의 검사 문장</summary>
    public static void SetIssue(Label mark, EBehaviorMessageSeverity? severity, string text)
    {
        if (severity.HasValue == false)
        {
            mark.style.display = DisplayStyle.None;
            mark.tooltip = "";
            return;
        }
        bool error = severity.Value == EBehaviorMessageSeverity.Error;
        mark.text = error ? "✕" : "⚠";
        mark.style.color = error ? ErrorMark : WarningMark;
        mark.tooltip = text ?? "";
        mark.style.display = DisplayStyle.Flex;
    }

    /// <summary>메모 줄 — 작은 기울임 글, 옅은 노랑 바탕, 비면 감춤 (SetText)</summary>
    public static Label MemoLine()
    {
        var memo = new Label();
        memo.style.fontSize = 10;
        memo.style.unityFontStyleAndWeight = FontStyle.Italic;
        memo.style.whiteSpace = WhiteSpace.Normal;
        memo.style.color = new Color(1f, 1f, 1f, 0.85f);
        memo.style.backgroundColor = MemoBackground;
        memo.style.paddingLeft = 6;
        memo.style.paddingRight = 6;
        memo.style.paddingTop = 2;
        memo.style.paddingBottom = 2;
        memo.style.maxWidth = 260;
        memo.style.display = DisplayStyle.None;
        return memo;
    }

    /// <summary>실행 값 글 (디버거) — 작은 글씨, 호박색, 비면 감춤 (SetText)</summary>
    public static Label DebugLine()
    {
        var line = new Label();
        line.style.fontSize = 10;
        line.style.color = DebugText;
        line.style.whiteSpace = WhiteSpace.Normal;
        line.style.maxWidth = 260;
        line.style.paddingLeft = 6;
        line.style.paddingRight = 6;
        line.style.display = DisplayStyle.None;
        return line;
    }

    /// <summary>breakpoint 표시 — 빨간 ●, 처음에는 감춤</summary>
    public static Label BreakpointDot()
    {
        var dot = new Label("●");
        dot.style.color = BreakpointMark;
        dot.style.marginRight = 4;
        dot.style.display = DisplayStyle.None;
        dot.tooltip = "breakpoint — Play 중 이 노드에 들어가면 Unity를 멈춘다 (카드 오른쪽 클릭으로 끈다)";
        return dot;
    }

    /// <summary>
    /// 디버거 테두리 — 카드 위에 겹쳐 그리는 틀 (position absolute · 네 변 0): 자리를 차지하지 않아 켜고 꺼도 카드 크기 · 선 자리가 그대로이고, 누르기는 아래 카드로 간다. 처음에는 감춤
    /// </summary>
    public static VisualElement DebugOutline()
    {
        var outline = new VisualElement { pickingMode = PickingMode.Ignore };
        outline.style.position = Position.Absolute;
        outline.style.left = 0;
        outline.style.top = 0;
        outline.style.right = 0;
        outline.style.bottom = 0;
        outline.style.display = DisplayStyle.None;
        return outline;
    }

    /// <summary>디버거 테두리의 색 · 두께 — 색이 없으면 감춘다</summary>
    public static void SetOutline(VisualElement outline, Color? color, float width)
    {
        if (color.HasValue == false)
        {
            outline.style.display = DisplayStyle.None;
            return;
        }
        outline.style.display = DisplayStyle.Flex;
        outline.style.borderLeftWidth = width;
        outline.style.borderRightWidth = width;
        outline.style.borderTopWidth = width;
        outline.style.borderBottomWidth = width;
        outline.style.borderLeftColor = color.Value;
        outline.style.borderRightColor = color.Value;
        outline.style.borderTopColor = color.Value;
        outline.style.borderBottomColor = color.Value;
    }

    /// <summary>칸 줄 — 칸을 가운데에, 어두운 바탕</summary>
    private static void StylePortStrip(VisualElement strip)
    {
        strip.style.flexDirection = FlexDirection.Row;
        strip.style.justifyContent = Justify.Center;
        strip.style.alignItems = Align.Center;
        strip.style.backgroundColor = PortStrip;
        strip.style.paddingTop = 1;
        strip.style.paddingBottom = 1;
    }
}