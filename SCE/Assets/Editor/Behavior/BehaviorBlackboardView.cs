using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Blackboard 패널 — 트리의 키 목록. 키마다: 이름 · 형식 · ▲ ▼ ✕ / 기본값 (형식에 맞는 칸) / 처음 값 출처 (Int 키) / 쓰는 노드 / 검사 줄. 맨 위 [+ 키] (형식 고르기).
/// 형식 · 기본값 · 출처는 SerializedObject로 바꾼다 (Undo · 값 추적은 Unity가 한다). 이름 (노드가 쓰는 이름도 함께) · 더하기 · 지우기 · 차례 · 형식을 바꾼 뒤 다시 그리기는 부르는 쪽 (Callbacks)이 한다.
/// 키마다 이름 "key:{차례}" — 검사 표시 (ShowIssues) · 키로 가기 (Reveal)가 찾는다.
/// </summary>
public static class BehaviorBlackboardView
{
    public sealed class Callbacks
    {
        public Action<EBehaviorKeyType> AddKey;
        public Action<int> RemoveKey;

        /// <summary>(자리, −1 위로 · +1 아래로)</summary>
        public Action<int, int> MoveKey;

        /// <summary>(자리, 새 이름) → 막은 까닭 (바꿨으면 null)</summary>
        public Func<int, string, string> RenameKey;

        /// <summary>형식을 바꾼 뒤 — 기본값 칸이 형식을 따라 바뀌도록 패널을 다시 그린다</summary>
        public Action TypeChanged;
    }

    private const string KeyPrefix = "key:";
    internal const string TopIssueName = "blackboard-issue"; // 키 자리가 없는 Blackboard 검사 줄 (목록 전체의 문제)
    private const string DefaultLabel = "기본값";

    public static VisualElement Build(SerializedObject so, BehaviorTreeDocumentAsset document, Callbacks callbacks)
    {
        var root = new VisualElement();
        root.style.paddingLeft = 6;
        root.style.paddingRight = 6;
        root.style.paddingTop = 4;
        EditorTextInput.CommitOnEnterOrBlur(root);

        BehaviorBlackboardKey[] keys = document.Tree.Keys ?? Array.Empty<BehaviorBlackboardKey>();
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        var title = new Label($"Blackboard 키 {keys.Length}개");
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.flexGrow = 1;
        header.Add(title);
        Button add = null;
        add = new Button(() => ShowAddMenu(add, callbacks)) { text = "+ 키", tooltip = "키를 더한다 — 형식을 고른다 (이름은 NewKey…, 바꿀 수 있음)" };
        header.Add(add);
        root.Add(header);
        Label topIssue = BehaviorNodeInspector.IssueLine("");
        topIssue.name = TopIssueName;
        topIssue.RemoveFromClassList(BehaviorNodeInspector.IssueClass); // 키 줄의 검사 표시 (ShowIssues — 줄 단위)와 따로 맞춘다
        root.Add(topIssue);
        root.Add(Note("노드의 키 고르기 · 값 또는 키 칸이 이 이름으로 키를 고른다. 이름을 바꾸면 노드가 쓰는 이름도 함께 바뀐다."));

        var users = new List<BehaviorBlackboardEditing.KeyUser>();
        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i] == null)
                continue;
            BehaviorBlackboardEditing.CollectUsers(document, keys[i].Name, users);
            root.Add(KeyRow(so, document, keys, i, users, callbacks));
        }
        root.Bind(so);
        return root;
    }

    private static void ShowAddMenu(VisualElement activator, Callbacks callbacks)
    {
        var menu = new GenericMenu();
        foreach (EBehaviorKeyType type in Enum.GetValues(typeof(EBehaviorKeyType)))
        {
            EBehaviorKeyType chosen = type;
            menu.AddItem(new GUIContent(type.ToString()), false, () => callbacks?.AddKey?.Invoke(chosen));
        }
        menu.DropDown(activator.worldBound);
    }

    /// <summary>키 한 줄 — 이름 · 형식 · ▲ ▼ ✕ / 기본값 / 처음 값 출처 (Int 키, 또는 이미 있으면) / 쓰는 노드 / 검사 줄</summary>
    private static VisualElement KeyRow(SerializedObject so, BehaviorTreeDocumentAsset document, BehaviorBlackboardKey[] keys, int index, List<BehaviorBlackboardEditing.KeyUser> users,
                                        Callbacks callbacks)
    {
        BehaviorBlackboardKey key = keys[index];
        string path = BehaviorNodePaths.KeyAt(index);
        var row = new VisualElement { name = KeyPrefix + index };
        row.style.marginTop = 4;
        row.style.paddingTop = 3;
        row.style.paddingBottom = 3;
        row.style.borderTopWidth = 1;
        row.style.borderTopColor = new Color(1f, 1f, 1f, 0.08f);

        var line = new VisualElement();
        line.style.flexDirection = FlexDirection.Row;
        line.style.alignItems = Align.Center;
        var name = new TextField { value = key.Name, isDelayed = true, tooltip = "키 이름 (JSON: name) — 비거나 앞뒤 공백 · 다른 키와 겹치면 안 됨" };
        name.style.flexGrow = 1;
        Label nameIssue = BehaviorNodeInspector.IssueLine("name");
        name.RegisterValueChangedCallback(evt =>
        {
            string error = callbacks?.RenameKey?.Invoke(index, evt.newValue);
            if (error == null)
                return;
            name.SetValueWithoutNotify(evt.previousValue);
            BehaviorNodeInspector.SetIssueText(nameIssue, EBehaviorMessageSeverity.Error, error);
        });
        line.Add(name);

        var type = new EnumField(key.Type) { bindingPath = path + "." + nameof(BehaviorBlackboardKey.Type), tooltip = "키 형식 (JSON: type)" };
        type.style.width = 80;
        type.RegisterValueChangedCallback(evt =>
        {
            if (evt.previousValue != null && Equals(evt.previousValue, evt.newValue) == false)
                callbacks?.TypeChanged?.Invoke();
        });
        line.Add(type);
        line.Add(SmallButton("▲", "위로", index > 0, () => callbacks?.MoveKey?.Invoke(index, -1)));
        line.Add(SmallButton("▼", "아래로", index + 1 < keys.Length, () => callbacks?.MoveKey?.Invoke(index, +1)));
        line.Add(SmallButton("✕", "키 지우기 — 이 키를 쓰는 노드 칸은 검사 목록에 에러로", true, () => callbacks?.RemoveKey?.Invoke(index)));
        row.Add(line);
        row.Add(nameIssue);

        string defaultName = DefaultFieldOf(key.Type);
        if (defaultName != null)
        {
            SerializedProperty defaultValue = so.FindProperty(path + "." + defaultName);
            if (defaultValue != null)
                row.Add(new PropertyField(defaultValue, DefaultLabel) { tooltip = "처음 값 (JSON: default) — 처음 값 출처가 없거나 해석하지 못할 때" });
        }
        else
        {
            row.Add(Note("기본값 없음 — Creature 키는 비어서 시작한다"));
        }
        row.Add(BehaviorNodeInspector.IssueLine("default"));

        if (key.Type == EBehaviorKeyType.Int || key.HasSeed)
        {
            var seed = new TextField("처음 값 출처")
            {
                bindingPath = path + "." + nameof(BehaviorBlackboardKey.Seed),
                isDelayed = true,
                tooltip = "몬스터마다 처음 값을 정하는 이름 (JSON: seed, Int 키만) — 예: skill.0 = 그 몬스터 스킬 목록의 0번째. 해석하지 못하면 기본값",
            };
            row.Add(seed);
            row.Add(BehaviorNodeInspector.IssueLine("seed"));
        }
        row.Add(BehaviorNodeInspector.IssueLine("type"));

        row.Add(Note(users.Count == 0 ? "쓰는 노드 없음" : "쓰는 노드: " + UsersText(users)));
        return row;
    }

    /// <summary>형식의 기본값 칸 이름 — Creature는 없음 (null)</summary>
    private static string DefaultFieldOf(EBehaviorKeyType type)
    {
        switch (type)
        {
            case EBehaviorKeyType.Bool: return nameof(BehaviorBlackboardKey.DefaultBool);
            case EBehaviorKeyType.Int: return nameof(BehaviorBlackboardKey.DefaultInt);
            case EBehaviorKeyType.Float: return nameof(BehaviorBlackboardKey.DefaultFloat);
            case EBehaviorKeyType.Vector2: return nameof(BehaviorBlackboardKey.DefaultVector2);
            default: return null;
        }
    }

    /// <summary>"노드 이름표 (칸)" 목록 — 많으면 앞 여섯 개 외 n개</summary>
    private static string UsersText(List<BehaviorBlackboardEditing.KeyUser> users)
    {
        const int maxShown = 6;
        var parts = new List<string>();
        for (int i = 0; i < users.Count && i < maxShown; i++)
            parts.Add(users[i].Node.NodeId + " (" + users[i].Field + ")");
        string text = string.Join(", ", parts);
        return users.Count > maxShown ? text + $" 외 {users.Count - maxShown}개" : text;
    }

    /// <summary>키마다 검사 줄을 맞춘다 (패널을 다시 만들지 않는다) — 키 자리의 줄은 그 키의 칸별로, 키 자리가 없는 Blackboard 줄 (목록 전체의 문제)은 맨 위에</summary>
    public static void ShowIssues(VisualElement view, IReadOnlyList<BehaviorTreeCheckItem> items)
    {
        if (view == null)
            return;
        foreach (VisualElement child in view.Children())
        {
            if (child.name != null && child.name.StartsWith(KeyPrefix, StringComparison.Ordinal)
                && int.TryParse(child.name.Substring(KeyPrefix.Length), out int keyIndex))
                BehaviorNodeInspector.ShowIssues(child, items, item => item.Place == EBehaviorCheckPlace.Blackboard && item.KeyIndex == keyIndex);
        }

        Label top = view.Q<Label>(TopIssueName);
        if (top == null)
            return;
        EBehaviorMessageSeverity? severity = null;
        string text = "";
        for (int i = 0; i < items.Count; i++)
        {
            BehaviorTreeCheckItem item = items[i];
            if (item.Place != EBehaviorCheckPlace.Blackboard || item.KeyIndex >= 0)
                continue;
            severity = item.IsError || severity == EBehaviorMessageSeverity.Error ? EBehaviorMessageSeverity.Error : EBehaviorMessageSeverity.Warning;
            string line = item.Field.Length > 0 ? item.Field + ": " + item.Text : item.Text;
            text = text.Length == 0 ? line : text + "\n" + line;
        }
        BehaviorNodeInspector.SetIssueText(top, severity, text);
    }

    /// <summary>그 키 줄이 보이게 스크롤하고 잠깐 밝힌다. 없으면 false</summary>
    public static bool Reveal(VisualElement view, int keyIndex)
    {
        VisualElement row = view?.Q(KeyPrefix + keyIndex);
        if (row == null)
            return false;
        row.GetFirstAncestorOfType<ScrollView>()?.ScrollTo(row);
        BehaviorNodeInspector.Flash(row);
        return true;
    }

    private static Button SmallButton(string text, string tooltip, bool enabled, Action clicked)
    {
        var button = new Button(clicked) { text = text, tooltip = tooltip };
        button.style.width = 22;
        button.SetEnabled(enabled);
        return button;
    }

    private static Label Note(string text)
    {
        var label = new Label(text);
        label.style.opacity = 0.6f;
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.fontSize = 10;
        label.style.marginLeft = 3;
        return label;
    }
}