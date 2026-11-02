using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 세부 설정 패널 — 고른 노드 (Main · Decorator · Service) 또는 트리 설정 (ROOT 카드를 골랐을 때 — 트리 Id · 파일 · 쓰는 몬스터 · 설정 칸).
/// 노드 칸은 SerializedProperty에서 자동으로 만든다 (Interaction Editor 노드 패널과 같은 방식 — 노드에 칸을 더하면 툴 수정 없이 따라온다). 구조 칸 (Decorators · Services · Children)은 그래프가 다룬다.
///  - 키 고르기 (BehaviorKeySelector): 받는 형식의 키만 (노드 Compile이 밝힌 형식 — BehaviorTreeCompiler.CollectKeyFields), 꼭 골라야 하지 않으면 '(없음)'도
///  - 값 또는 키 (BehaviorFloat · Int · Bool · Vector2): [값] · [키] 바꾸기 — 값 칸 또는 그 형식의 키 목록. 값을 쓰면 [키로] — 그 값을 기본값으로 하는 새 키로 뺀다 (변형이 몬스터마다 바꿀 수 있게)
///  - enum: 노드가 목록을 좁히면 (BehaviorNode.AllowsChoice) 고를 수 있는 값만 — 지금 값이 그 밖이면 맨 앞에 "⚠ 지금 값", 좁히지 않으면 PropertyField (이름 목록, [Flags] enum은 늘)
///  - 그 밖: PropertyField
/// 노드가 지금 설정에서 쓰지 않는다고 밝힌 칸 (BehaviorNode.UnusedFieldReason)은 잠그고 (회색 — 값은 그대로 저장) 아래에 까닭을 보인다. 노드 규칙이 예외를 내면 잠그지 · 좁히지 않는다.
/// 잠금 · enum 목록은 다른 칸 (Check · 키 · Abort Mode 등)을 따라 바뀐다 — 창이 패널을 만든 때의 FieldStateKey와 견주어 다시 만든다.
/// 칸 값은 SerializedObject로 바꾼다 (Undo · 값 추적은 Unity가 한다). 이름표 (NodeId) · 메모는 부르는 쪽 (Callbacks)이 문서에 한다 — 카드 자리 · 메모가 이름표를 따라가야 해서.
/// 칸마다 이름 "field:{JSON 키}" — 검사 표시 (ShowIssues) · 칸으로 가기 (Reveal)가 찾는다. 글자 칸은 Enter · 포커스 이동 때 확정한다 (EditorTextInput).
/// </summary>
public static class BehaviorNodeInspector
{
    public sealed class Callbacks
    {
        /// <summary>(노드, 새 이름표) → 막은 까닭 (바꿨으면 null)</summary>
        public Func<BehaviorNode, string, string> RenameNode;

        /// <summary>(노드, 메모 — 빈 글이면 지운다)</summary>
        public Action<BehaviorNode, string> SetMemo;

        /// <summary>(노드, 값 또는 키 칸의 JSON 키) — 그 값을 기본값으로 하는 새 키로 뺀다 → 막은 까닭 (뺐으면 null)</summary>
        public Func<BehaviorNode, string, string> PromoteToKey;
    }

    /// <summary>트리 설정 패널에 넘기는 것 — 트리 Id 바꾸기와 창이 정한 글 (파일 · 쓰는 몬스터)</summary>
    public sealed class TreeInfo
    {
        /// <summary>새 트리 Id → 막은 까닭 (바꿨으면 null)</summary>
        public Func<string, string> RenameTree;

        /// <summary>"파일: Behavior_Boss.json" 등 — 저장하면 바뀔 이름까지</summary>
        public string FileText;

        /// <summary>"쓰는 몬스터 2: Goblin (1001) · …" 등 · 툴팁 (모두)</summary>
        public string UsersText;
        public string UsersTooltip;
    }

    private const string FieldPrefix = "field:";
    private const string NoneChoice = "(없음)";
    private const float LabelWidth = 130f;

    private static readonly Dictionary<string, string> s_settingTooltips = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        { "tickInterval", "평가 간격 (초) — 0이면 매 프레임" },
        { "nearDistance", "조종 캐릭터와 이 거리 안이면 평가 간격 그대로, 밖이면 간격 × farIntervalMultiplier — 0이면 거리를 보지 않음" },
        { "farIntervalMultiplier", "nearDistance 밖의 평가 간격 배수 — 1 이상" },
        { "activationDistance", "조종 캐릭터와 이 거리 밖이면 평가하지 않음 (진행 중 Task도 멈춤) — 0이면 늘 평가" },
    };

    #region 노드
    /// <summary>노드의 세부 설정. 문서에서 노드를 찾지 못하면 안내 글만</summary>
    public static VisualElement Build(SerializedObject so, BehaviorTreeDocumentAsset document, BehaviorNode node, Callbacks callbacks)
    {
        VisualElement root = Root();
        string path = BehaviorNodePaths.PathOf(document, node);
        SerializedProperty property = path != null ? so.FindProperty(path) : null;
        if (property == null || property.propertyType != SerializedPropertyType.ManagedReference)
        {
            root.Add(EditorPaneUI.Hint("노드를 찾을 수 없습니다 — 그래프에서 다시 고르세요"));
            return root;
        }

        bool known = document.Registry.TryGetInfo(node.GetType(), out BehaviorNodeRegistry.NodeInfo info);
        var title = new Label(known ? BehaviorNodePicker.Title(in info) : node.GetType().Name + " — 등록되지 않은 노드");
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        root.Add(title);
        root.Add(Note(KindText(node) + (known && string.IsNullOrEmpty(info.Category) == false ? " · " + info.Category : "")));
        if (known && string.IsNullOrEmpty(info.Description) == false)
            root.Add(Note(info.Description));
        root.Add(IssueLine(""));

        root.Add(NodeIdRow(node, callbacks));
        root.Add(MemoRow(document, node, callbacks));

        var keyFields = new List<BehaviorKeyFieldInfo>();
        BehaviorTreeCompiler.CollectKeyFields(node, keyFields);

        SerializedProperty iterator = property.Copy();
        SerializedProperty end = property.GetEndProperty();
        bool enterChildren = true;
        while (iterator.NextVisible(enterChildren) && SerializedProperty.EqualContents(iterator, end) == false)
        {
            enterChildren = false;
            if (BehaviorNodeFields.TryGetFieldByName(node.GetType(), iterator.name, out BehaviorNodeFields.Field field) == false || IsStructureField(field))
                continue;
            root.Add(FieldRow(so, iterator.Copy(), field, document, keyFields, node, callbacks));
        }

        root.Bind(so);
        return root;
    }

    /// <summary>그래프가 다루는 칸 — 이름표 (위의 이름표 줄) · Decorators · Services · Children</summary>
    private static bool IsStructureField(BehaviorNodeFields.Field field)
    {
        string name = field.Info.Name;
        return name == nameof(BehaviorNode.NodeId) || name == nameof(BehaviorMainNode.Decorators)
            || name == nameof(BehaviorMainNode.Services) || name == nameof(BehaviorComposite.Children);
    }

    private static string KindText(BehaviorNode node)
    {
        switch (node)
        {
            case BehaviorComposite _: return "Composite";
            case BehaviorTask _: return "Task";
            case BehaviorDecorator _: return "Decorator";
            case BehaviorService _: return "Service";
            default: return node.GetType().Name;
        }
    }

    /// <summary>이름표 — 바꾸면 카드 자리 · 메모가 따라간다 (Callbacks.RenameNode). 막히면 칸 값을 원래대로 두고 까닭을 칸 아래에</summary>
    private static VisualElement NodeIdRow(BehaviorNode node, Callbacks callbacks)
    {
        VisualElement row = FieldContainer("id");
        var field = new TextField("이름표")
        {
            value = node.NodeId,
            isDelayed = true,
            tooltip = "노드 이름표 (JSON: id) — 카드 자리 · 메모 · 디버거가 노드를 찾는 이름. 트리 안에서 겹치지 않아야 한다",
        };
        field.labelElement.style.minWidth = LabelWidth;
        Label issue = IssueLine("id");
        field.RegisterValueChangedCallback(evt =>
        {
            string error = callbacks?.RenameNode?.Invoke(node, evt.newValue);
            if (error == null)
            {
                SetIssueText(issue, null, "");
                return;
            }
            field.SetValueWithoutNotify(evt.previousValue);
            SetIssueText(issue, EBehaviorMessageSeverity.Error, error);
        });
        row.Add(field);
        row.Add(issue);
        return row;
    }

    /// <summary>메모 (편집기만 — 트리 JSON에 들지 않는다). 바꾸면 Callbacks.SetMemo</summary>
    private static VisualElement MemoRow(BehaviorTreeDocumentAsset document, BehaviorNode node, Callbacks callbacks)
    {
        VisualElement row = FieldContainer("memo");
        var field = new TextField("메모")
        {
            value = document.TryGetMemo(node.NodeId, out string memo) ? memo : "",
            multiline = true,
            isDelayed = true,
            tooltip = "편집기 메모 — 카드 위에 보인다. 게임 데이터 (트리 JSON)에는 들지 않는다",
        };
        field.labelElement.style.minWidth = LabelWidth;
        field.style.whiteSpace = WhiteSpace.Normal;
        field.RegisterValueChangedCallback(evt => callbacks?.SetMemo?.Invoke(node, evt.newValue));
        row.Add(field);
        return row;
    }

    /// <summary>칸 하나 — 형식에 따라 키 고르기 · 값 또는 키 · 좁힌 enum 목록 · PropertyField. 노드가 쓰지 않는다고 밝힌 칸이면 잠그고 까닭. 아래에 검사 줄</summary>
    private static VisualElement FieldRow(SerializedObject so, SerializedProperty property, BehaviorNodeFields.Field field, BehaviorTreeDocumentAsset document,
                                          List<BehaviorKeyFieldInfo> keyFields, BehaviorNode node, Callbacks callbacks)
    {
        VisualElement row = FieldContainer(field.JsonKey);
        Type type = field.Info.FieldType;
        string tooltip = "JSON: " + field.JsonKey;
        BehaviorBlackboardKey[] keys = document.Tree.Keys;
        VisualElement shown; // 칸 (잠그면 이것을)
        if (type == typeof(BehaviorKeySelector))
        {
            KeyFieldOf(keyFields, field.JsonKey, out EBehaviorKeyTypeMask allowed, out bool required);
            VisualElement line = Line(property.displayName, tooltip + " — " + AllowedText(allowed) + " 키" + (required ? "" : " (고르지 않아도 됨)"));
            line.Add(KeyPopup(so, property.FindPropertyRelative(nameof(BehaviorKeySelector.Name)).propertyPath, keys, allowed, required == false));
            row.Add(line);
            shown = line;
        }
        else if (BehaviorNodeFields.ValueKeyType(type) != 0)
        {
            EBehaviorKeyTypeMask allowed = BehaviorNodeFields.ValueKeyType(type);
            VisualElement line = Line(property.displayName, tooltip + " — 값 또는 " + AllowedText(allowed) + " 키");
            var body = new VisualElement();
            body.style.flexGrow = 1;
            body.style.flexDirection = FlexDirection.Row;
            body.style.alignItems = Align.Center;
            var mode = new Button { tooltip = "값을 쓸지 Blackboard 키를 쓸지 — 키를 고르면 몬스터마다 그 키의 값" };
            mode.style.width = 34;
            line.Add(mode);
            line.Add(body);
            row.Add(line);
            string keyPath = property.FindPropertyRelative(nameof(BehaviorFloat.Key)).propertyPath;
            string valuePath = property.FindPropertyRelative(nameof(BehaviorFloat.Value)).propertyPath;
            Action promote = callbacks?.PromoteToKey == null ? null : () => ShowPromoteError(row, callbacks.PromoteToKey(node, field.JsonKey));
            FillValueOrKey(so, keyPath, valuePath, keys, allowed, mode, body, promote);
            shown = line;
        }
        else if (type.IsEnum && TryGetNarrowedChoices(node, field, keys, out List<int> choices, out int current))
        {
            shown = ChoiceLine(so, property.propertyPath, property.displayName, tooltip, type, current, choices);
            row.Add(shown);
        }
        else
        {
            shown = new PropertyField(property) { tooltip = tooltip };
            row.Add(shown);
        }
        string unused = UnusedReasonOf(node, field.JsonKey, keys);
        if (unused != null)
            ShowUnused(row, shown, unused);
        row.Add(IssueLine(field.JsonKey));
        return row;
    }

    /// <summary>[키로]가 막힌 까닭 — 그 칸 아래에 검사 줄과 따로 (검사 줄의 문장을 가리지 않게). 뺐으면 (null) 그대로 — 창이 패널을 다시 만든다</summary>
    private static void ShowPromoteError(VisualElement row, string error)
    {
        if (error == null)
            return;
        Label label = row.Q<Label>(PromoteErrorName);
        if (label == null)
        {
            label = new Label { name = PromoteErrorName };
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.fontSize = 10;
            label.style.marginLeft = 3;
            label.style.color = BehaviorGraphStyle.ErrorMark;
            row.Add(label);
        }
        label.text = "✕ " + error;
    }

    /// <summary>
    /// 노드 칸의 잠금 · enum 목록을 한 줄로 — 칸마다 JSON 키 · 쓰지 않는 까닭 · (좁힌 enum이면) 목록과 목록 밖의 지금 값. 창이 패널을 만든 때와 견주어 바뀌었으면 다시 만든다.
    /// 구조 칸은 빼고. O(칸 수 × enum 값 수)
    /// </summary>
    public static string FieldStateKey(BehaviorNode node, BehaviorBlackboardKey[] keys)
    {
        if (node == null)
            return "";
        var text = new StringBuilder();
        IReadOnlyList<BehaviorNodeFields.Field> fields = BehaviorNodeFields.Of(node.GetType());
        for (int i = 0; i < fields.Count; i++)
        {
            BehaviorNodeFields.Field field = fields[i];
            if (IsStructureField(field))
                continue;
            text.Append(field.JsonKey).Append('=').Append(UnusedReasonOf(node, field.JsonKey, keys) ?? "");
            if (field.Info.FieldType.IsEnum && TryGetNarrowedChoices(node, field, keys, out List<int> choices, out int current))
            {
                text.Append('[');
                for (int c = 0; c < choices.Count; c++)
                    text.Append(choices[c]).Append(',');
                if (choices.Contains(current) == false)
                    text.Append('!').Append(current);
                text.Append(']');
            }
            text.Append('|');
        }
        return text.ToString();
    }

    /// <summary>enum 칸 목록에 둘 값 (값 차례) — 노드가 밝힌다 (BehaviorNode.AllowsChoice — 예외를 내면 둔다). O(enum 값 수)</summary>
    internal static List<int> ChoiceValues(BehaviorNode node, string field, Type enumType, BehaviorBlackboardKey[] keys)
    {
        var values = new List<int>();
        IReadOnlyList<int> all = EnumValues(enumType);
        for (int i = 0; i < all.Count; i++)
        {
            if (AllowsChoiceOf(node, field, all[i], keys))
                values.Add(all[i]);
        }
        return values;
    }

    /// <summary>노드가 밝힌 쓰지 않는 칸의 까닭 (BehaviorNode.UnusedFieldReason) — 노드 코드가 예외를 내면 잠그지 않는다 (예외는 노드 형식 · 칸마다 한 번 Console에)</summary>
    private static string UnusedReasonOf(BehaviorNode node, string field, BehaviorBlackboardKey[] keys)
    {
        try
        {
            return node.UnusedFieldReason(field, keys);
        }
        catch (Exception exception)
        {
            ReportRuleError(node, field, exception);
            return null;
        }
    }

    /// <summary>노드가 enum 칸 목록에 그 값을 두는가 (BehaviorNode.AllowsChoice) — 노드 코드가 예외를 내면 둔다 (좁히지 않음, 예외는 노드 형식 · 칸마다 한 번 Console에)</summary>
    private static bool AllowsChoiceOf(BehaviorNode node, string field, int value, BehaviorBlackboardKey[] keys)
    {
        try
        {
            return node.AllowsChoice(field, value, keys);
        }
        catch (Exception exception)
        {
            ReportRuleError(node, field, exception);
            return true;
        }
    }

    /// <summary>칸 규칙의 예외를 노드 형식 · 칸마다 한 번 알린다 — 패널 · 값 추적이 칸이 바뀔 때마다 부르므로</summary>
    private static void ReportRuleError(BehaviorNode node, string field, Exception exception)
    {
        if (s_reportedRuleErrors.Add(node.GetType().FullName + "." + field))
            Debug.LogException(exception);
    }

    /// <summary>알린 칸 규칙 예외를 잊는다 — 시험이 같은 노드 형식의 예외를 다시 볼 때</summary>
    internal static void ForgetReportedRuleErrors()
    {
        s_reportedRuleErrors.Clear();
    }

    /// <summary>노드가 enum 칸의 목록을 좁히는가 — 모든 값을 두고 지금 값도 그 안이면 false (PropertyField 그대로), [Flags] enum도 false. current = 지금 값</summary>
    private static bool TryGetNarrowedChoices(BehaviorNode node, BehaviorNodeFields.Field field, BehaviorBlackboardKey[] keys, out List<int> choices, out int current)
    {
        Type enumType = field.Info.FieldType;
        choices = null;
        current = 0;
        if (enumType.IsDefined(typeof(FlagsAttribute), false))
            return false; // 조합 값 ([Flags]) — 좁히지 않는다 (PropertyField가 조합을 고른다)
        current = Convert.ToInt32(field.Info.GetValue(node));
        choices = ChoiceValues(node, field.JsonKey, enumType, keys);
        return choices.Count < EnumValues(enumType).Count || choices.Contains(current) == false;
    }

    private static readonly Dictionary<Type, int[]> s_enumValues = new Dictionary<Type, int[]>();
    private static readonly HashSet<string> s_reportedRuleErrors = new HashSet<string>(StringComparer.Ordinal); // 예외를 알린 칸 규칙 (노드 형식.칸)

    /// <summary>enum의 값 (값 차례 — 같은 값은 한 번). 형식마다 처음 한 번 모은다</summary>
    private static IReadOnlyList<int> EnumValues(Type enumType)
    {
        if (s_enumValues.TryGetValue(enumType, out int[] cached))
            return cached;
        var values = new List<int>();
        foreach (object value in Enum.GetValues(enumType))
        {
            int number = Convert.ToInt32(value);
            if (values.Contains(number) == false)
                values.Add(number);
        }
        cached = values.ToArray();
        s_enumValues.Add(enumType, cached);
        return cached;
    }

    /// <summary>
    /// 노드가 목록을 좁힌 enum 칸 — 고를 수 있는 값만 (Unity 칸과 같은 이름). 지금 값이 목록에 없으면 맨 앞에 "⚠ 지금 값 — 지금 설정에 맞지 않음" 그대로 (다른 것을 고르면 고쳐진다).
    /// 고르면 그 칸의 intValue를 바꾼다 (SerializedObject — Undo · 값 추적)
    /// </summary>
    internal static VisualElement ChoiceLine(SerializedObject so, string path, string label, string tooltip, Type enumType, int current, IReadOnlyList<int> choices)
    {
        VisualElement line = Line(label, tooltip);
        var values = new List<int>(choices);
        var names = new List<string>();
        for (int i = 0; i < values.Count; i++)
            names.Add(ChoiceName(enumType, values[i]));
        int index = values.IndexOf(current);
        if (index < 0)
        {
            values.Insert(0, current);
            names.Insert(0, "⚠ " + ChoiceName(enumType, current) + " — 지금 설정에 맞지 않음");
            index = 0;
        }
        var popup = new DropdownField(names, index) { tooltip = tooltip };
        popup.style.flexGrow = 1;
        popup.RegisterValueChangedCallback(evt =>
        {
            int chosen = names.IndexOf(evt.newValue);
            if (chosen >= 0)
                SetEnumValue(so, path, values[chosen]);
        });
        line.Add(popup);
        return line;
    }

    /// <summary>enum 칸 값을 SerializedObject로 바꾼다 (Undo · 값 추적) — 같으면 그대로. 칸이 없으면 false</summary>
    internal static bool SetEnumValue(SerializedObject so, string path, int value)
    {
        so.Update();
        SerializedProperty property = so.FindProperty(path);
        if (property == null)
            return false;
        if (property.intValue != value)
        {
            property.intValue = value;
            so.ApplyModifiedProperties();
        }
        return true;
    }

    /// <summary>enum 값의 이름 — Unity 칸과 같게 띄어 쓴다 (ObjectNames.NicifyVariableName). 정의에 없는 값이면 수</summary>
    private static string ChoiceName(Type enumType, int value)
    {
        string name = Enum.GetName(enumType, Enum.ToObject(enumType, value));
        return name != null ? ObjectNames.NicifyVariableName(name) : value.ToString();
    }

    internal const string UnusedClass = "behavior-unused-field";

    /// <summary>쓰지 않는 칸 — 칸을 잠그고 (회색 · 값은 그대로 저장) 아래에 까닭을 보인다. 칸의 툴팁 (JSON 키)은 남기고 까닭을 덧붙인다</summary>
    internal static void ShowUnused(VisualElement row, VisualElement shown, string reason)
    {
        string why = "지금 설정에서는 쓰지 않음 — " + reason + " (값은 그대로 저장)";
        shown.SetEnabled(false);
        shown.tooltip = string.IsNullOrEmpty(shown.tooltip) ? why : shown.tooltip + "\n" + why;
        var note = new Label("쓰지 않음 — " + reason) { tooltip = why }; // 켜진 글 — 잠긴 칸의 툴팁이 보이지 않아도 까닭이 보인다
        note.AddToClassList(UnusedClass);
        note.style.opacity = 0.6f;
        note.style.fontSize = 10;
        note.style.marginLeft = 3;
        note.style.whiteSpace = WhiteSpace.Normal;
        row.Add(note);
    }

    /// <summary>
    /// 값 또는 키 칸의 몸 — 키를 골랐으면 [키] + 키 목록, 아니면 [값] + 값 칸 (+ promote가 있으면 [키로]). [값] · [키]를 누르면 바꾼다 (키로 바꿀 때 그 형식의 첫 키, 키가 없으면 누를 수 없음).
    /// [키로]는 그 값을 기본값으로 하는 새 키를 만들어 이 칸이 쓰게 한다 (부르는 쪽 — 키 목록이 바뀌어 패널을 다시 만든다)
    /// </summary>
    private static void FillValueOrKey(SerializedObject so, string keyPath, string valuePath, BehaviorBlackboardKey[] keys, EBehaviorKeyTypeMask allowed, Button mode, VisualElement body,
                                       Action promote)
    {
        body.Clear();
        SerializedProperty key = so.FindProperty(keyPath);
        if (key == null)
            return;
        bool usesKey = string.IsNullOrEmpty(key.stringValue) == false;
        string firstKey = FirstKeyOf(keys, allowed);
        mode.text = usesKey ? "키" : "값";
        mode.SetEnabled(usesKey || firstKey != null);
        mode.clickable = new Clickable(() =>
        {
            so.Update();
            SerializedProperty current = so.FindProperty(keyPath);
            if (current == null)
                return;
            bool toKey = string.IsNullOrEmpty(current.stringValue);
            if (toKey && firstKey == null)
                return;
            current.stringValue = toKey ? firstKey : "";
            so.ApplyModifiedProperties();
            FillValueOrKey(so, keyPath, valuePath, keys, allowed, mode, body, promote);
        });

        if (usesKey)
        {
            body.Add(KeyPopup(so, keyPath, keys, allowed, false));
            return;
        }
        SerializedProperty value = so.FindProperty(valuePath);
        if (value == null)
            return;
        var valueField = new PropertyField(value, "");
        valueField.style.flexGrow = 1;
        body.Add(valueField);
        valueField.Bind(so);
        if (promote != null)
        {
            var toKey = new Button(promote) { text = "키로", tooltip = "이 값을 기본값으로 하는 Blackboard 키를 새로 만들어 이 칸이 그 키를 쓰게 한다 — 동작은 같고, [변형] 탭에서 몬스터마다 값을 바꿀 수 있다. " +
                                                     "키 값에는 노드의 값 검사 (0 이상 등)를 하지 않는다 — 트리에 이어진 노드에서, 검사 에러를 고친 뒤 뺀다" };
            toKey.style.width = 40;
            body.Add(toKey);
        }
    }

    /// <summary>
    /// 키 목록 칸 — 받는 형식의 키 (목록 차례, "이름  (형식)"). includeNone이면 맨 앞에 '(없음)'.
    /// 지금 이름이 목록에 없으면 (없는 키 · 형식이 다른 키 · 고르지 않음) 맨 앞에 그대로 보인다 — 다른 것을 고르면 고쳐진다.
    /// 고르면 namePath의 글자를 바꾼다 (SerializedObject — Undo · 값 추적).
    /// </summary>
    private static DropdownField KeyPopup(SerializedObject so, string namePath, BehaviorBlackboardKey[] keys, EBehaviorKeyTypeMask allowed, bool includeNone)
    {
        SerializedProperty property = so.FindProperty(namePath);
        string current = property != null ? property.stringValue ?? "" : "";
        var names = new List<string>();
        var choices = new List<string>();
        if (includeNone)
        {
            names.Add("");
            choices.Add(NoneChoice);
        }
        if (keys != null)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i] != null && (allowed & BehaviorCompileContext.MaskOf(keys[i].Type)) != 0 && names.Contains(keys[i].Name) == false)
                {
                    names.Add(keys[i].Name);
                    choices.Add(keys[i].Name + "  (" + keys[i].Type + ")");
                }
            }
        }
        int index = names.IndexOf(current);
        if (index < 0)
        {
            names.Insert(0, current);
            choices.Insert(0, current.Length == 0 ? "(고르지 않음 — 키를 고르세요)" : "⚠ " + current + " — 없는 키이거나 형식이 다름");
            index = 0;
        }

        var popup = new DropdownField(choices, index);
        popup.style.flexGrow = 1;
        popup.RegisterValueChangedCallback(_ =>
        {
            int chosen = popup.index;
            if (chosen < 0 || chosen >= names.Count)
                return;
            so.Update();
            SerializedProperty name = so.FindProperty(namePath);
            if (name == null || name.stringValue == names[chosen])
                return;
            name.stringValue = names[chosen];
            so.ApplyModifiedProperties();
        });
        return popup;
    }

    /// <summary>노드 Compile이 밝힌 키 고르기 칸의 받는 형식 · 꼭 골라야 하는가 — 밝히지 않은 칸이면 모든 형식 · 골라야 하지 않음</summary>
    private static void KeyFieldOf(List<BehaviorKeyFieldInfo> keyFields, string jsonKey, out EBehaviorKeyTypeMask allowed, out bool required)
    {
        for (int i = 0; i < keyFields.Count; i++)
        {
            if (string.Equals(keyFields[i].Field, jsonKey, StringComparison.Ordinal))
            {
                allowed = keyFields[i].Allowed;
                required = keyFields[i].Required;
                return;
            }
        }
        allowed = EBehaviorKeyTypeMask.Any;
        required = false;
    }

    private static string FirstKeyOf(BehaviorBlackboardKey[] keys, EBehaviorKeyTypeMask allowed)
    {
        if (keys == null)
            return null;
        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i] != null && string.IsNullOrEmpty(keys[i].Name) == false && (allowed & BehaviorCompileContext.MaskOf(keys[i].Type)) != 0)
                return keys[i].Name;
        }
        return null;
    }

    /// <summary>받는 형식 글 — "Creature" · "Bool · Int" · 모두면 "모든 형식의"</summary>
    private static string AllowedText(EBehaviorKeyTypeMask allowed)
    {
        if ((allowed & EBehaviorKeyTypeMask.Any) == EBehaviorKeyTypeMask.Any)
            return "모든 형식의";
        var names = new List<string>();
        foreach (EBehaviorKeyType type in Enum.GetValues(typeof(EBehaviorKeyType)))
        {
            if ((allowed & BehaviorCompileContext.MaskOf(type)) != 0)
                names.Add(type.ToString());
        }
        return string.Join(" · ", names);
    }
    #endregion

    #region 트리 설정
    /// <summary>
    /// 트리 설정 — Id (바꾸면 info.RenameTree — 막히면 칸 값을 원래대로 두고 까닭을 칸 아래에) · 파일 · 쓰는 몬스터 · 평가 간격 · 거리 LOD (SerializedObject로 묶음) · Blackboard 키 수.
    /// 파일 이름 · Addressable 주소는 저장할 때 Id를 따라간다
    /// </summary>
    public static VisualElement BuildTree(SerializedObject so, BehaviorTreeDocumentAsset document, TreeInfo info)
    {
        VisualElement root = Root();
        var title = new Label("트리 설정");
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        root.Add(title);
        root.Add(IssueLine(""));

        VisualElement idRow = FieldContainer("id");
        var id = new TextField("Id")
        {
            value = document.Tree.Id,
            isDelayed = true,
            tooltip = "게임이 트리를 찾는 이름 — 파일 이름 · Addressable 주소 · 몬스터 데이터의 BehaviorTreeId. 바꾸면 저장할 때 파일 이름 · 주소도 바꾼다 (몬스터 데이터는 그대로)",
        };
        id.labelElement.style.minWidth = LabelWidth;
        Label issue = IssueLine("id");
        id.RegisterValueChangedCallback(evt =>
        {
            string error = info?.RenameTree?.Invoke(evt.newValue);
            if (error == null)
            {
                SetIssueText(issue, null, "");
                return;
            }
            id.SetValueWithoutNotify(evt.previousValue);
            SetIssueText(issue, EBehaviorMessageSeverity.Error, error);
        });
        idRow.Add(id);
        if (string.IsNullOrEmpty(info?.FileText) == false)
            idRow.Add(Note(info.FileText));
        if (string.IsNullOrEmpty(info?.UsersText) == false)
        {
            Label users = Note(info.UsersText);
            users.tooltip = info.UsersTooltip;
            idRow.Add(users);
        }
        idRow.Add(issue);
        root.Add(idRow);

        SerializedProperty settings = so.FindProperty(BehaviorNodePaths.Settings);
        if (settings != null)
        {
            SerializedProperty iterator = settings.Copy();
            SerializedProperty end = settings.GetEndProperty();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren) && SerializedProperty.EqualContents(iterator, end) == false)
            {
                enterChildren = false;
                if (BehaviorNodeFields.TryGetFieldByName(typeof(BehaviorTreeSettings), iterator.name, out BehaviorNodeFields.Field field) == false)
                    continue;
                VisualElement row = FieldContainer(field.JsonKey);
                row.Add(new PropertyField(iterator.Copy())
                {
                    tooltip = (s_settingTooltips.TryGetValue(field.JsonKey, out string tip) ? tip + " — " : "") + "JSON: settings." + field.JsonKey,
                });
                row.Add(IssueLine(field.JsonKey));
                root.Add(row);
            }
        }

        int keyCount = document.Tree.Keys?.Length ?? 0;
        root.Add(Note($"Blackboard 키 {keyCount}개 — [Blackboard] 탭에서 고친다"));
        root.Bind(so);
        return root;
    }
    #endregion

    #region 검사 표시 · 칸으로 가기
    /// <summary>
    /// 칸마다 검사 줄을 맞춘다 — items 가운데 이 패널의 것 (belongs)을 칸 (JSON 키)별로, 패널에 없는 칸 · 칸이 없는 것은 맨 위 줄에.
    /// 패널을 다시 만들지 않는다 (입력 중인 칸 그대로). O(검사 줄 수 × 줄 수)
    /// </summary>
    public static void ShowIssues(VisualElement panel, IReadOnlyList<BehaviorTreeCheckItem> items, Func<BehaviorTreeCheckItem, bool> belongs)
    {
        if (panel == null)
            return;
        List<Label> labels = panel.Query<Label>(className: IssueClass).ToList();
        var fields = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < labels.Count; i++)
            fields.Add(labels[i].userData as string ?? "");
        for (int l = 0; l < labels.Count; l++)
        {
            string field = labels[l].userData as string ?? "";
            EBehaviorMessageSeverity? severity = null;
            string text = "";
            for (int i = 0; i < items.Count; i++)
            {
                BehaviorTreeCheckItem item = items[i];
                bool here = string.Equals(item.Field, field, StringComparison.Ordinal) || (field.Length == 0 && fields.Contains(item.Field) == false);
                if (here == false || belongs(item) == false)
                    continue;
                severity = item.IsError || severity == EBehaviorMessageSeverity.Error ? EBehaviorMessageSeverity.Error : EBehaviorMessageSeverity.Warning;
                string line = field.Length == 0 && item.Field.Length > 0 ? item.Field + ": " + item.Text : item.Text;
                text = text.Length == 0 ? line : text + "\n" + line;
            }
            SetIssueText(labels[l], severity, text);
        }
    }

    /// <summary>그 칸 (JSON 키)이 보이게 스크롤하고 잠깐 밝힌다. 칸이 없으면 false</summary>
    public static bool Reveal(VisualElement panel, string field)
    {
        VisualElement row = panel?.Q(FieldPrefix + field);
        if (row == null)
            return false;
        row.GetFirstAncestorOfType<ScrollView>()?.ScrollTo(row);
        Flash(row);
        return true;
    }

    /// <summary>잠깐 밝힌다 — 0.8초 뒤 원래대로</summary>
    internal static void Flash(VisualElement element)
    {
        element.style.backgroundColor = new Color(1f, 0.85f, 0.3f, 0.18f);
        element.schedule.Execute(() => element.style.backgroundColor = StyleKeyword.Null).ExecuteLater(800);
    }
    #endregion

    #region 조각
    internal const string IssueClass = "behavior-issue-line";
    private const string PromoteErrorName = "promote-error";

    private static VisualElement Root()
    {
        var root = new VisualElement();
        root.style.paddingLeft = 6;
        root.style.paddingRight = 6;
        root.style.paddingTop = 4;
        EditorTextInput.CommitOnEnterOrBlur(root); // 자동으로 생기는 칸 (PropertyField)도 Enter · 포커스 이동 때 확정
        return root;
    }

    /// <summary>칸 한 줄을 담는 상자 — 이름 "field:{JSON 키}" (검사 표시 · 칸으로 가기가 찾는다 — 변형 패널의 칸도)</summary>
    internal static VisualElement FieldContainer(string jsonKey)
    {
        var container = new VisualElement { name = FieldPrefix + jsonKey };
        container.style.marginTop = 2;
        return container;
    }

    /// <summary>라벨 + 칸 한 줄 (PropertyField가 아닌 칸 — 라벨 폭을 PropertyField와 비슷하게)</summary>
    private static VisualElement Line(string label, string tooltip)
    {
        var line = new VisualElement();
        line.style.flexDirection = FlexDirection.Row;
        line.style.alignItems = Align.Center;
        var labelElement = new Label(label) { tooltip = tooltip };
        labelElement.style.minWidth = LabelWidth;
        labelElement.style.paddingLeft = 3;
        line.Add(labelElement);
        return line;
    }

    /// <summary>검사 줄 — 처음에는 감춤 (ShowIssues · SetIssueText). userData = 칸 (JSON 키, 칸이 없는 줄이면 "")</summary>
    internal static Label IssueLine(string field)
    {
        var label = new Label { userData = field ?? "" };
        label.AddToClassList(IssueClass);
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.fontSize = 10;
        label.style.marginLeft = 3;
        label.style.display = DisplayStyle.None;
        return label;
    }

    internal static void SetIssueText(Label label, EBehaviorMessageSeverity? severity, string text)
    {
        if (severity.HasValue == false || string.IsNullOrEmpty(text))
        {
            label.style.display = DisplayStyle.None;
            return;
        }
        bool error = severity.Value == EBehaviorMessageSeverity.Error;
        label.text = (error ? "✕ " : "⚠ ") + text;
        label.style.color = error ? BehaviorGraphStyle.ErrorMark : BehaviorGraphStyle.WarningMark;
        label.style.display = DisplayStyle.Flex;
    }

    private static Label Note(string text)
    {
        var label = new Label(text);
        label.style.opacity = 0.6f;
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.marginBottom = 2;
        return label;
    }
    #endregion
}