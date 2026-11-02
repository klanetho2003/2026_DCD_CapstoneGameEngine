using UnityEngine.UIElements;

/// <summary>
/// 에디터 창 글자 칸(TextField · IntegerField · FloatField …)의 공용 입력 규칙 (R8c-1 보완).
/// 규칙: 글자 칸은 Enter나 포커스 이동 때 한 번만 확정한다 (isDelayed) — 글자마다 Undo 기록 · 검사 · 표시 갱신이 생기지 않게.
///  - 직접 만드는 칸: 만들 때 isDelayed = true (Interaction Editor 카드 · 머리글, State Definition Editor, Game State Monitor)
///  - 자동으로 생기는 칸 (PropertyField · 배열 원소): CommitOnEnterOrBlur(root) — 칸이 포커스를 받는 순간 규칙을 켠다 (Interaction Editor 파라미터 패널)
///  - 칸을 지우는 화면 전환: CommitFocused로 확정을 먼저 요청하고, 지우는 일은 한 틱 뒤에 한다 (Interaction Editor · State Definition Editor — 목록에서 고르기 등)
///    (클릭을 처리하는 중에 요청한 확정은 그 처리가 끝난 뒤에 일어난다 — 바로 지우면 확정 전 글자가 칸과 함께 버려진다)
/// 상태 없음. 처리기는 root마다 하나 — root를 버리면 함께 버려진다.
/// </summary>
public static class EditorTextInput
{
    /// <summary>
    /// root 아래 글자 칸이 포커스를 받는 순간(글자를 치기 전) 확정 규칙을 켠다 — 지금 있는 칸과 나중에 생기는 칸 모두.
    /// PropertyField는 바인딩 때, 배열 원소 칸은 화면에 보일 때 칸을 만들어서 만들 때 하나씩 켤 수 없다 — 이벤트 위임으로 root 한 곳에서.
    /// 비용: 포커스마다 O(요소 깊이). 할당 없음.
    /// </summary>
    public static void CommitOnEnterOrBlur(VisualElement root)
    {
        root.RegisterCallback<FocusInEvent>(OnFocusIn, TrickleDown.TrickleDown);
    }

    /// <summary>
    /// root가 있는 창에서 입력 중인 글자 칸을 확정한다 (포커스 해제). 확정을 요청했으면 true.
    /// clicked: 누른 곳 — 그 칸 안(칸 자신 · 라벨)을 누른 것이면 그대로 둔다. null이면 늘 확정한다 (창이 포커스를 잃을 때).
    /// 이벤트를 처리하는 중에 부르면 실제 확정은 그 처리가 끝난 뒤에 일어난다.
    /// </summary>
    public static bool CommitFocused(VisualElement root, VisualElement clicked = null)
    {
        Focusable focused = root.focusController?.focusedElement;
        VisualElement field = FieldOf(focused as VisualElement);
        if (field == null || IsInside(clicked, field))
            return false;

        focused.Blur();
        return true;
    }

    /// <summary>element 자신이나 조상 중 가장 가까운 글자 칸. 없으면 null. 포커스는 칸 안쪽 요소가 받기도 해서 위로 찾는다. O(깊이)</summary>
    public static VisualElement FieldOf(VisualElement element)
    {
        for (VisualElement e = element; e != null; e = e.hierarchy.parent)
        {
            if (Match(e, false))
                return e;
        }
        return null;
    }

    /// <summary>글자 칸이면 확정 규칙을 켜고 true. 글자 칸이 아니면(null 포함) 아무것도 하지 않고 false.</summary>
    public static bool MakeDelayed(VisualElement element)
    {
        return Match(element, true);
    }

    private static void OnFocusIn(FocusInEvent evt)
    {
        MakeDelayed(FieldOf(evt.target as VisualElement));
    }

    /// <summary>
    /// 글자 칸의 종류는 이 switch 한 곳 — 숫자 · 글자를 치는 Unity 칸은 모두 TextInputBaseField를 상속한다 (값 형식만 다름).
    /// Vector · Rect 같은 묶음 칸은 안의 FloatField · IntegerField가 포커스를 받으므로 따로 적지 않는다.
    /// 노드 필드에 새 값 형식(예: Hash128)을 쓰면 여기에 한 줄 더한다 — EditorTextInputTests가 형식별로 확인한다.
    /// </summary>
    private static bool Match(VisualElement element, bool makeDelayed)
    {
        switch (element)
        {
            case TextInputBaseField<string> field: return Matched(field, makeDelayed);
            case TextInputBaseField<int> field: return Matched(field, makeDelayed);
            case TextInputBaseField<long> field: return Matched(field, makeDelayed);
            case TextInputBaseField<uint> field: return Matched(field, makeDelayed);
            case TextInputBaseField<ulong> field: return Matched(field, makeDelayed);
            case TextInputBaseField<float> field: return Matched(field, makeDelayed);
            case TextInputBaseField<double> field: return Matched(field, makeDelayed);
            default: return false;
        }
    }

    /// <summary>글자 칸으로 맞았다 — makeDelayed면 규칙도 켠다.</summary>
    private static bool Matched<T>(TextInputBaseField<T> field, bool makeDelayed)
    {
        if (makeDelayed)
            field.isDelayed = true;
        return true;
    }

    private static bool IsInside(VisualElement element, VisualElement ancestor)
    {
        for (VisualElement e = element; e != null; e = e.hierarchy.parent)
        {
            if (e == ancestor)
                return true;
        }
        return false;
    }
}