using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 변형 패널 — 트리의 변형 목록 (위)과 고른 변형의 키 기본값 (아래). 변형 = 이 트리의 노드 · 설정을 그대로 쓰고 Blackboard 키 기본값만 바꾼 것 (몬스터 데이터의 트리 Id "트리 Id/변형 이름").
///  - 목록: 맨 위 [+ 변형], 변형마다 이름 (누르면 고름) · 검사 표시 · 쓰는 몬스터 수 · [복제] · [✕]
///  - 고른 변형: 이름 칸 (바꾸기 — 막히면 원래대로 두고 까닭) · 몬스터 데이터에 넣을 트리 Id (고를 수 있는 글) · 쓰는 몬스터 ·
///    키마다 한 줄: 이름 (형식) · 기반 값 · 변형 값 칸 (바꾸지 않은 키는 기반 값을 흐리게 — 다른 값을 넣으면 변형 값) · [✕] (기반 값으로).
///    바꿀 수 없는 키 (Creature · 처음 값 출처 — BehaviorTreeVariants.LockedReason)는 까닭만 (그래도 바꾼 값이 있으면 그 값과 [✕]), 키 줄이 없는 값 (트리에 없는 키 · 같은 키 두 번)은 아래에 따로 [✕]
/// 바꾸기는 부르는 쪽 (Callbacks)이 문서에 한다 (Undo · 검사). 값 · 이름 칸은 Enter · 포커스 이동 때 확정하고, 막히면 칸을 원래 값으로 두고 까닭을 보인다.
/// 부르는 쪽에 넘기는 변형 자리는 누른 때의 자리 (패널을 만든 뒤 목록이 바뀌었으면 그 변형을 다시 찾고, 없으면 -1 — 부르는 쪽이 막는다).
/// 칸마다 이름 "field:{칸}" — 이름 칸은 "name", 키 줄은 "blackboard.키 이름" (BehaviorVariantEditing.ValueField — 검사 표시 BehaviorNodeInspector.ShowIssues · 칸으로 가기 Reveal가 찾는다)
/// </summary>
public static class BehaviorVariantView
{
    public sealed class Callbacks
    {
        public Action AddVariant;
        public Action<int> SelectVariant;
        public Action<int> DuplicateVariant;
        public Action<int> RemoveVariant;

        /// <summary>(자리, 새 이름) → 막은 까닭 (바꿨으면 null)</summary>
        public Func<int, string, string> RenameVariant;

        /// <summary>(자리, 값 — 키 이름 · 형식 · 값) → 막은 까닭 (바꿨으면 null)</summary>
        public Func<int, BehaviorKeyOverride, string> SetValue;

        /// <summary>(자리, 키 이름) — 기반 값으로</summary>
        public Action<int, string> ClearValue;

        /// <summary>(자리, 값 차례) — 키 줄이 없는 값 지우기</summary>
        public Action<int, int> RemoveValueAt;
    }

    internal const string RowPrefix = "variant-row:";
    internal const string MarkClass = "behavior-variant-mark";
    private const float LabelWidth = 130f;
    private const float BaseWidth = 72f;
    private const float NotOverriddenOpacity = 0.55f;

    /// <summary>
    /// 패널을 만든다 — selected = 고른 변형 차례 (없는 자리면 아래 칸 없이). usersOf = 몬스터 데이터의 트리 Id → 쓰는 몬스터 (크리처 표를 읽지 못했으면 null).
    /// O(변형 수 + 키 수 + 값 수 × 키 수)
    /// </summary>
    public static VisualElement Build(BehaviorTreeDocumentAsset document, int selected, Func<string, IReadOnlyList<BehaviorTreeUser>> usersOf, Callbacks callbacks)
    {
        var root = new VisualElement();
        root.style.paddingLeft = 6;
        root.style.paddingRight = 6;
        root.style.paddingTop = 4;
        EditorTextInput.CommitOnEnterOrBlur(root);

        BehaviorTreeAsset tree = document.Tree;
        BehaviorTreeVariant[] variants = tree.Variants ?? Array.Empty<BehaviorTreeVariant>();
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        var title = new Label($"변형 {variants.Length}개");
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.flexGrow = 1;
        header.Add(title);
        header.Add(new Button(() => callbacks?.AddVariant?.Invoke()) { text = "+ 변형", tooltip = "변형을 더한다 — 바꾼 값 없이 (이름은 Variant…, 바꿀 수 있음)" });
        root.Add(header);
        root.Add(Note($"몬스터 데이터의 BehaviorTreeId = '트리 Id{BehaviorTreeVariants.Separator}변형 이름' — 노드 · 설정은 이 트리 그대로, Blackboard 키 기본값만 바꾼다. " +
                      "노드 칸의 수를 변형마다 바꾸려면 세부 설정에서 그 칸을 [키로] 뺀다. " +
                      "키 값에는 노드의 값 검사 (0 이상 · 다른 칸과의 관계 등)를 하지 않는다 — 변형 값은 그 칸의 규칙에 맞게 넣는다."));
        if (variants.Length == 0)
            root.Add(Note("변형 없음 — [+ 변형]으로 더한다"));

        for (int i = 0; i < variants.Length; i++)
            root.Add(ListRow(document, variants[i], i, i == selected, usersOf, callbacks));

        BehaviorTreeVariant chosen = BehaviorVariantEditing.At(tree, selected);
        if (chosen != null)
            root.Add(Detail(document, chosen, usersOf, callbacks));
        return root;
    }

    /// <summary>변형의 지금 자리 — 누른 때 다시 찾는다 (패널을 만든 뒤 Undo 등으로 목록이 바뀌었을 수 있다). 없으면 -1. O(변형 수)</summary>
    private static int IndexNow(BehaviorTreeDocumentAsset document, BehaviorTreeVariant variant)
    {
        BehaviorTreeVariant[] variants = document.Tree != null ? document.Tree.Variants : null;
        return variants != null ? Array.IndexOf(variants, variant) : -1;
    }

    /// <summary>목록 한 줄 — 이름 (누르면 고름) · 검사 표시 · 쓰는 몬스터 수 · [복제] · [✕]</summary>
    private static VisualElement ListRow(BehaviorTreeDocumentAsset document, BehaviorTreeVariant variant, int index, bool selected, Func<string, IReadOnlyList<BehaviorTreeUser>> usersOf,
                                         Callbacks callbacks)
    {
        var row = new VisualElement { name = RowPrefix + index };
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginTop = 1;
        if (selected)
            row.style.backgroundColor = new Color(1f, 1f, 1f, 0.08f);

        string name = variant != null && string.IsNullOrEmpty(variant.Name) == false ? variant.Name : "(이름 없음)";
        var select = new Button(() => callbacks?.SelectVariant?.Invoke(IndexNow(document, variant))) { text = name, tooltip = "이 변형의 키 값을 아래에 보인다" };
        select.style.flexGrow = 1;
        select.style.unityTextAlign = TextAnchor.MiddleLeft;
        if (selected)
            select.style.unityFontStyleAndWeight = FontStyle.Bold;
        row.Add(select);

        var mark = new Label { userData = index };
        mark.AddToClassList(MarkClass);
        mark.style.width = 14;
        mark.style.unityFontStyleAndWeight = FontStyle.Bold;
        row.Add(mark);

        var users = new Label(UsersCountText(document.Tree, variant, usersOf));
        users.style.opacity = 0.7f;
        users.style.fontSize = 10;
        users.style.minWidth = 56;
        row.Add(users);
        row.Add(SmallButton("복제", "같은 값의 변형을 바로 아래에 더한다", 40, () => callbacks?.DuplicateVariant?.Invoke(IndexNow(document, variant))));
        row.Add(SmallButton("✕", "변형 지우기 — 이 변형을 가리키는 몬스터는 가만히 있게 된다 (목록 위 '없는 변형' 알림)", 22, () => callbacks?.RemoveVariant?.Invoke(IndexNow(document, variant))));
        return row;
    }

    /// <summary>고른 변형 — 맨 위 검사 줄 · 이름 칸 · 트리 Id · 쓰는 몬스터 · 키 줄 · 키 줄이 없는 값</summary>
    private static VisualElement Detail(BehaviorTreeDocumentAsset document, BehaviorTreeVariant variant, Func<string, IReadOnlyList<BehaviorTreeUser>> usersOf, Callbacks callbacks)
    {
        BehaviorTreeAsset tree = document.Tree;
        var detail = new VisualElement();
        detail.style.marginTop = 8;
        detail.style.paddingTop = 4;
        detail.style.borderTopWidth = 1;
        detail.style.borderTopColor = new Color(1f, 1f, 1f, 0.15f);
        detail.Add(BehaviorNodeInspector.IssueLine(""));

        VisualElement nameRow = BehaviorNodeInspector.FieldContainer("name");
        var nameField = new TextField("이름") { value = variant.Name ?? "", isDelayed = true, tooltip = "변형 이름 (JSON: name) — 비거나 앞뒤 공백 · '/' · 다른 변형과 겹치면 안 됨 (대소문자를 가리지 않는다)" };
        nameField.labelElement.style.minWidth = LabelWidth;
        Label nameIssue = BehaviorNodeInspector.IssueLine("name");
        nameField.RegisterValueChangedCallback(evt =>
        {
            string error = callbacks?.RenameVariant?.Invoke(IndexNow(document, variant), evt.newValue);
            if (error == null)
                return;
            nameField.SetValueWithoutNotify(evt.previousValue);
            BehaviorNodeInspector.SetIssueText(nameIssue, EBehaviorMessageSeverity.Error, error);
        });
        nameRow.Add(nameField);
        nameRow.Add(nameIssue);
        detail.Add(nameRow);

        string fullId = string.IsNullOrEmpty(tree.Id) ? "" : BehaviorTreeVariants.FullId(tree.Id, variant.Name);
        var id = new TextField("몬스터 데이터에") { value = fullId.Length > 0 ? fullId : "(트리 Id 없음)", isReadOnly = true, tooltip = "몬스터 데이터의 BehaviorTreeId에 이 글을 넣으면 이 변형을 쓴다 (대소문자까지)" };
        id.labelElement.style.minWidth = LabelWidth;
        detail.Add(id);
        detail.Add(Note(UsersText(fullId, usersOf)));

        var head = new VisualElement();
        head.style.flexDirection = FlexDirection.Row;
        head.style.marginTop = 4;
        head.Add(HeadLabel("키 (형식)", LabelWidth));
        head.Add(HeadLabel("기반", BaseWidth));
        head.Add(HeadLabel("변형 — 바꾸지 않으면 기반 값 (흐림)", 0f));
        detail.Add(head);

        BehaviorBlackboardKey[] keys = tree.Keys ?? Array.Empty<BehaviorBlackboardKey>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int k = 0; k < keys.Length; k++)
        {
            if (keys[k] != null && seen.Add(keys[k].Name ?? ""))
                detail.Add(KeyRow(keys[k], document, variant, callbacks));
        }
        if (keys.Length == 0)
            detail.Add(Note("Blackboard 키 없음 — [Blackboard] 탭에서 키를 더하거나 세부 설정에서 칸을 [키로] 뺀다"));

        var strays = new List<int>();
        BehaviorVariantEditing.CollectStrayValues(tree, variant, strays);
        for (int s = 0; s < strays.Count; s++)
            detail.Add(StrayRow(variant.Overrides[strays[s]], strays[s], document, variant, callbacks));
        return detail;
    }

    /// <summary>
    /// 키 한 줄 — 이름 (형식) · 기반 값 · 변형 값 칸 · [✕] / 바꿀 수 없으면 까닭 (바꾼 값이 남아 있으면 — 출처 · 형식을 나중에 바꿈 — 그 값과 [✕]) /
    /// 남은 값의 형식이 키와 맞지 않으면 (키 형식을 나중에 바꿈) 그 값과 [✕] — 칸에 0을 보이지 않게 / 검사 줄
    /// </summary>
    private static VisualElement KeyRow(BehaviorBlackboardKey key, BehaviorTreeDocumentAsset document, BehaviorTreeVariant variant, Callbacks callbacks)
    {
        string field = BehaviorVariantEditing.ValueField(key.Name);
        VisualElement row = BehaviorNodeInspector.FieldContainer(field);
        var line = new VisualElement();
        line.style.flexDirection = FlexDirection.Row;
        line.style.alignItems = Align.Center;
        var name = new Label($"{key.Name}  ({key.Type})");
        name.style.minWidth = LabelWidth;
        name.style.paddingLeft = 3;
        line.Add(name);
        row.Add(line);

        int at = BehaviorVariantEditing.IndexOfValue(variant, key.Name);
        bool overridden = at >= 0;
        Action clearValue = () => callbacks?.ClearValue?.Invoke(IndexNow(document, variant), key.Name);
        string locked = BehaviorTreeVariants.LockedReason(key);
        bool mismatched = locked == null && overridden && BehaviorTreeVariants.ValueError(key, variant.Overrides[at]) != null;
        if (locked != null || mismatched)
        {
            Label reason = Note(locked ?? "변형 값이 이 키에 맞지 않음 — [✕]로 지우고 다시 넣는다");
            reason.style.marginBottom = 0;
            reason.style.flexShrink = 1;
            line.Add(reason);
            if (overridden)
            {
                var stored = new Label($"변형 값 {ValueText(variant.Overrides[at])}") { tooltip = "이 키에 쓸 수 없는 변형 값 — 저장할 수 없다: [✕]로 지운다" };
                stored.style.paddingLeft = 6;
                line.Add(stored);
                line.Add(SmallButton("✕", "변형 값을 지운다", 22, clearValue));
            }
            row.Add(BehaviorNodeInspector.IssueLine(field));
            return row;
        }

        var baseText = new Label(ValueText(key)) { tooltip = "트리의 기본값 ([Blackboard] 탭)" };
        baseText.style.minWidth = BaseWidth;
        baseText.style.opacity = 0.75f;
        line.Add(baseText);

        BehaviorKeyOverride current = overridden ? variant.Overrides[at] : FromDefault(key);
        var body = new VisualElement();
        body.style.flexGrow = 1;
        body.style.flexDirection = FlexDirection.Row;
        body.style.alignItems = Align.Center;
        line.Add(body);
        Label issue = BehaviorNodeInspector.IssueLine(field);
        Button clear = SmallButton("✕", "기반 값으로 — 변형 값을 지운다", 22, clearValue);
        Func<BehaviorKeyOverride, bool> commit = value =>
        {
            string error = callbacks?.SetValue?.Invoke(IndexNow(document, variant), value);
            if (error != null)
            {
                BehaviorNodeInspector.SetIssueText(issue, EBehaviorMessageSeverity.Error, error);
                return false;
            }
            body.style.opacity = 1f;
            clear.style.visibility = Visibility.Visible;
            return true;
        };
        FillValueFields(key, current, body, commit);
        body.style.opacity = overridden ? 1f : NotOverriddenOpacity;
        clear.style.visibility = overridden ? Visibility.Visible : Visibility.Hidden;
        line.Add(clear);
        row.Add(issue);
        return row;
    }

    /// <summary>형식에 맞는 값 칸 — Bool 체크 · Int · Float 칸 · Vector2 칸 둘 (x · y). 확정하면 commit (키 이름 · 키 형식 · 값), 막히면 (false) 칸을 원래 값으로</summary>
    private static void FillValueFields(BehaviorBlackboardKey key, BehaviorKeyOverride current, VisualElement body, Func<BehaviorKeyOverride, bool> commit)
    {
        string keyName = key.Name;
        switch (key.Type)
        {
            case EBehaviorKeyType.Bool:
                var toggle = new Toggle { value = current.BoolValue, tooltip = "변형의 기본값" };
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (commit(new BehaviorKeyOverride { Key = keyName, Type = EBehaviorKeyType.Bool, BoolValue = evt.newValue }) == false)
                        toggle.SetValueWithoutNotify(evt.previousValue);
                });
                body.Add(toggle);
                break;
            case EBehaviorKeyType.Int:
                var integer = new IntegerField { value = current.IntValue, isDelayed = true, tooltip = "변형의 기본값" };
                integer.style.flexGrow = 1;
                integer.RegisterValueChangedCallback(evt =>
                {
                    if (commit(new BehaviorKeyOverride { Key = keyName, Type = EBehaviorKeyType.Int, IntValue = evt.newValue }) == false)
                        integer.SetValueWithoutNotify(evt.previousValue);
                });
                body.Add(integer);
                break;
            case EBehaviorKeyType.Float:
                var number = new FloatField { value = BehaviorTreeVariants.FloatOf(current), isDelayed = true, tooltip = "변형의 기본값" };
                number.style.flexGrow = 1;
                number.RegisterValueChangedCallback(evt =>
                {
                    if (commit(new BehaviorKeyOverride { Key = keyName, Type = EBehaviorKeyType.Float, FloatValue = evt.newValue }) == false)
                        number.SetValueWithoutNotify(evt.previousValue);
                });
                body.Add(number);
                break;
            case EBehaviorKeyType.Vector2:
                var x = new FloatField("x") { value = current.VectorValue.x, isDelayed = true, tooltip = "변형의 기본값 x" };
                var y = new FloatField("y") { value = current.VectorValue.y, isDelayed = true, tooltip = "변형의 기본값 y" };
                x.style.flexGrow = 1;
                y.style.flexGrow = 1;
                x.labelElement.style.minWidth = 12;
                y.labelElement.style.minWidth = 12;
                x.RegisterValueChangedCallback(evt =>
                {
                    if (commit(new BehaviorKeyOverride { Key = keyName, Type = EBehaviorKeyType.Vector2, VectorValue = new Vector2(evt.newValue, y.value) }) == false)
                        x.SetValueWithoutNotify(evt.previousValue);
                });
                y.RegisterValueChangedCallback(evt =>
                {
                    if (commit(new BehaviorKeyOverride { Key = keyName, Type = EBehaviorKeyType.Vector2, VectorValue = new Vector2(x.value, evt.newValue) }) == false)
                        y.SetValueWithoutNotify(evt.previousValue);
                });
                body.Add(x);
                body.Add(y);
                break;
        }
    }

    /// <summary>
    /// 키 줄이 없는 값 — "키 = 값 — 까닭" · [✕] / 검사 줄. [✕]는 그 자리 (valueIndex)에 아직 같은 값이 있을 때만 지운다 —
    /// 패널을 만든 뒤 앞의 값이 지워져 차례가 밀렸으면 하지 않는다 (값을 덧붙이는 것은 차례를 밀지 않는다)
    /// </summary>
    private static VisualElement StrayRow(BehaviorKeyOverride value, int valueIndex, BehaviorTreeDocumentAsset document, BehaviorTreeVariant variant, Callbacks callbacks)
    {
        string field = BehaviorVariantEditing.ValueField(value.Key);
        VisualElement row = BehaviorNodeInspector.FieldContainer(field);
        var line = new VisualElement();
        line.style.flexDirection = FlexDirection.Row;
        line.style.alignItems = Align.Center;
        var text = new Label($"{(string.IsNullOrEmpty(value.Key) ? "(빈 키 이름)" : value.Key)} = {ValueText(value)} — 키 줄이 없는 값 (트리에 없는 키 · 같은 키 두 번)");
        text.style.flexGrow = 1;
        text.style.whiteSpace = WhiteSpace.Normal;
        text.style.paddingLeft = 3;
        line.Add(text);
        line.Add(SmallButton("✕", "이 값을 지운다", 22, () =>
        {
            BehaviorKeyOverride[] values = variant.Overrides;
            if (values != null && valueIndex < values.Length && SameValue(values[valueIndex], value))
                callbacks?.RemoveValueAt?.Invoke(IndexNow(document, variant), valueIndex);
        }));
        row.Add(line);
        row.Add(BehaviorNodeInspector.IssueLine(field));
        return row;
    }

    /// <summary>같은 바꾼 값인가 — 키 이름 · 적힌 형식 · 값</summary>
    private static bool SameValue(in BehaviorKeyOverride a, in BehaviorKeyOverride b)
    {
        return string.Equals(a.Key, b.Key, StringComparison.Ordinal) && a.Type == b.Type && a.BoolValue == b.BoolValue && a.IntValue == b.IntValue
               && a.FloatValue.Equals(b.FloatValue) && a.VectorValue.Equals(b.VectorValue);
    }

    /// <summary>
    /// 검사 줄을 맞춘다 (패널을 다시 만들지 않는다) — 고른 변형의 줄은 칸마다 (이름 · 키 이름, 칸이 없는 줄은 맨 위), 목록에는 변형마다 ✕ · ⚠ 표시. O(검사 줄 수 × (줄 수 + 변형 수))
    /// </summary>
    public static void ShowIssues(VisualElement view, IReadOnlyList<BehaviorTreeCheckItem> items, int selected)
    {
        if (view == null)
            return;
        BehaviorNodeInspector.ShowIssues(view, items, item => item.Place == EBehaviorCheckPlace.Variant && item.VariantIndex == selected);
        foreach (Label mark in view.Query<Label>(className: MarkClass).ToList())
        {
            int variantIndex = mark.userData is int number ? number : -1;
            EBehaviorMessageSeverity? severity = null;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Place == EBehaviorCheckPlace.Variant && items[i].VariantIndex == variantIndex)
                    severity = items[i].IsError || severity == EBehaviorMessageSeverity.Error ? EBehaviorMessageSeverity.Error : EBehaviorMessageSeverity.Warning;
            }
            mark.text = severity == null ? "" : severity == EBehaviorMessageSeverity.Error ? "✕" : "⚠";
            mark.style.color = severity == EBehaviorMessageSeverity.Error ? BehaviorGraphStyle.ErrorMark : BehaviorGraphStyle.WarningMark;
        }
    }

    /// <summary>그 칸 (이름 "name" · 키 이름)이 보이게 스크롤하고 잠깐 밝힌다. 없으면 false</summary>
    public static bool Reveal(VisualElement view, string field)
    {
        return BehaviorNodeInspector.Reveal(view, field ?? "");
    }

    /// <summary>키의 기본값을 변형 값 모양으로 (바꾸지 않은 키의 칸에 보인다)</summary>
    internal static BehaviorKeyOverride FromDefault(BehaviorBlackboardKey key)
    {
        return new BehaviorKeyOverride
        {
            Key = key.Name,
            Type = key.Type,
            BoolValue = key.DefaultBool,
            IntValue = key.DefaultInt,
            FloatValue = key.DefaultFloat,
            VectorValue = key.DefaultVector2,
        };
    }

    /// <summary>키 기본값의 글 — Bool "true" · "false", 수는 소수 둘째 자리까지, Vector2 "(x, y)"</summary>
    internal static string ValueText(BehaviorBlackboardKey key)
    {
        return ValueText(FromDefault(key));
    }

    /// <summary>값의 글 — 적힌 형식대로 (Bool "true" · "false", 수는 소수 둘째 자리까지, Vector2 "(x, y)")</summary>
    internal static string ValueText(BehaviorKeyOverride value)
    {
        switch (value.Type)
        {
            case EBehaviorKeyType.Bool:
                return value.BoolValue ? "true" : "false";
            case EBehaviorKeyType.Int:
                return value.IntValue.ToString(CultureInfo.InvariantCulture);
            case EBehaviorKeyType.Float:
                return BehaviorDebugText.Number(value.FloatValue);
            case EBehaviorKeyType.Vector2:
                return BehaviorDebugText.Point(value.VectorValue);
            default:
                return "-";
        }
    }

    /// <summary>목록의 쓰는 몬스터 수 — "몬스터 n", 크리처 표를 읽지 못했으면 "몬스터 ?"</summary>
    private static string UsersCountText(BehaviorTreeAsset tree, BehaviorTreeVariant variant, Func<string, IReadOnlyList<BehaviorTreeUser>> usersOf)
    {
        if (usersOf == null || variant == null || string.IsNullOrEmpty(tree.Id))
            return "몬스터 ?";
        return "몬스터 " + usersOf(BehaviorTreeVariants.FullId(tree.Id, variant.Name)).Count;
    }

    /// <summary>고른 변형의 쓰는 몬스터 글</summary>
    private static string UsersText(string fullId, Func<string, IReadOnlyList<BehaviorTreeUser>> usersOf)
    {
        if (usersOf == null)
            return "쓰는 몬스터: 알 수 없음 — 크리처 표를 읽지 못함";
        if (fullId.Length == 0)
            return "쓰는 몬스터: 없음 — 트리 Id가 없음";
        IReadOnlyList<BehaviorTreeUser> users = usersOf(fullId);
        return users.Count == 0
            ? "쓰는 몬스터: 없음 — 몬스터 데이터의 BehaviorTreeId가 이 변형을 가리키지 않음"
            : $"쓰는 몬스터 {users.Count}: {BehaviorTreeUsers.Describe(users, 6)}";
    }

    private static Label HeadLabel(string text, float width)
    {
        var label = new Label(text);
        label.style.fontSize = 10;
        label.style.opacity = 0.6f;
        label.style.paddingLeft = 3;
        if (width > 0f)
            label.style.minWidth = width;
        return label;
    }

    private static Button SmallButton(string text, string tooltip, float width, Action clicked)
    {
        var button = new Button(clicked) { text = text, tooltip = tooltip };
        button.style.width = width;
        return button;
    }

    private static Label Note(string text)
    {
        var label = new Label(text);
        label.style.opacity = 0.6f;
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.fontSize = 10;
        label.style.marginLeft = 3;
        label.style.marginBottom = 2;
        return label;
    }
}