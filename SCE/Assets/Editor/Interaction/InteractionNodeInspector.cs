using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 선택된 노드의 파라미터 뷰를 SerializedProperty에서 자동 생성한다.
/// 노드에 필드를 추가하면 툴 수정 없이 UI가 따라온다 (확장성의 핵심).
/// 중첩 SerializeReference 필드(NotCondition.Inner)는 타입 선택 버튼을 함께 그린다.
/// 특정 필드만 전용 칸이 필요하면 fieldOverride로 바꾼다 — 나머지 필드는 여전히 자동 생성된다.
/// </summary>
public static class InteractionNodeInspector
{
    /// <param name="fieldOverride">
    /// [8-C] (필드를 가진 노드, 필드) → 전용 UI. null을 돌려주면 기본 칸(PropertyField)을 쓴다.
    /// 이 클래스는 어떤 필드가 특별한지 모른다 — 그 지식은 넘기는 쪽(창)에 있다.
    /// </param>
    public static VisualElement Build(SerializedObject so, string nodePath, Action onStructureChanged,
                                      Func<object, SerializedProperty, VisualElement> fieldOverride = null)
    {
        var root = new VisualElement();
        root.style.paddingLeft = 6;
        root.style.paddingTop = 4;
        root.style.paddingRight = 6;

        SerializedProperty node = so.FindProperty(nodePath);
        if (node == null)
        {
            root.Add(Hint("노드를 찾을 수 없습니다 (목록이 변경되었을 수 있습니다)"));
            return root;
        }

        object value = node.managedReferenceValue;
        if (value == null)
        {
            root.Add(Hint("비어 있는 노드입니다"));
            return root;
        }

        // 헤더: 표시 이름 + 설명
        if (InteractionNodeRegistry.TryGetKey(value.GetType(), out string key))
        {
            var infos = value is InteractionCondition
                ? InteractionNodeRegistry.ConditionInfos
                : InteractionNodeRegistry.EffectInfos;

            for (int i = 0; i < infos.Count; i++)
            {
                if (infos[i].Key != key) continue;

                var title = new Label($"{infos[i].DisplayName}  ({key})");
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                root.Add(title);

                if (string.IsNullOrEmpty(infos[i].Description) == false)
                {
                    var desc = new Label(infos[i].Description);
                    desc.style.opacity = 0.6f;
                    desc.style.whiteSpace = WhiteSpace.Normal;
                    desc.style.marginBottom = 6;
                    root.Add(desc);
                }
                break;
            }
        }
        else
        {
            root.Add(Hint($"레지스트리에 없는 타입: {value.GetType().Name}"));
        }

        BuildFields(so, node, root, onStructureChanged, fieldOverride);
        root.Bind(so);
        return root;
    }

    /// <summary>노드의 직속 자식 프로퍼티만 순회한다 (GetEndProperty 경계).</summary>
    private static void BuildFields(SerializedObject so, SerializedProperty node, VisualElement parent, Action onStructureChanged,
                                    Func<object, SerializedProperty, VisualElement> fieldOverride)
    {
        object owner = node.managedReferenceValue; // [8-C] 이 필드들을 가진 노드 — fieldOverride가 노드 종류를 볼 수 있게 넘긴다

        SerializedProperty iterator = node.Copy();
        SerializedProperty end = node.GetEndProperty();
        bool enterChildren = true;

        while (iterator.NextVisible(enterChildren) && SerializedProperty.EqualContents(iterator, end) == false)
        {
            enterChildren = false;

            if (iterator.propertyType == SerializedPropertyType.ManagedReference)
            {
                parent.Add(BuildManagedReferenceField(so, iterator.Copy(), onStructureChanged, fieldOverride));
                continue;
            }

            // [8-C] 전용 칸이 있으면 그것을, 없으면 자동 생성 칸을 쓴다
            SerializedProperty field = iterator.Copy();
            VisualElement custom = fieldOverride != null ? fieldOverride(owner, field) : null;
            parent.Add(custom ?? new PropertyField(field));
        }
    }

    private static VisualElement BuildManagedReferenceField(SerializedObject so, SerializedProperty prop, Action onStructureChanged,
                                                            Func<object, SerializedProperty, VisualElement> fieldOverride)
    {
        string path = prop.propertyPath;
        bool isCondition = IsConditionField(prop);
        object current = prop.managedReferenceValue;
        string currentKey = current != null && InteractionNodeRegistry.TryGetKey(current.GetType(), out string registeredKey)
            ? registeredKey
            : null;

        var box = new VisualElement();
        box.style.marginTop = 4;
        box.style.paddingLeft = 8;
        box.style.borderLeftWidth = 2;
        box.style.borderLeftColor = new Color(1f, 1f, 1f, 0.15f);

        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;

        var label = new Label(ObjectNames.NicifyVariableName(prop.name));
        label.style.flexGrow = 1;
        row.Add(label);

        Button pick = null;
        pick = new Button(() =>
        {
            Rect rect = GUIUtility.GUIToScreenRect(pick.worldBound);
            InteractionNodePicker.Show(rect, isCondition, currentKey, key =>
            {
                // 패널이 다시 그려졌으면 무시. 지금과 같은 종류를 고른 경우도 무시 — 새 노드로 바꾸면 파라미터만 초기화된다
                if (pick.panel == null || key == currentKey)
                    return;

                so.Update();
                SerializedProperty target = so.FindProperty(path);
                if (target == null)
                    return;

                target.managedReferenceValue = CreateNode(key, isCondition);
                so.ApplyModifiedProperties();
                onStructureChanged?.Invoke();
            });
        })
        { text = current != null ? DescribeShort(current) : "(비어 있음) 선택…" };
        row.Add(pick);

        if (current != null)
        {
            row.Add(new Button(() =>
            {
                so.Update();
                so.FindProperty(path).managedReferenceValue = null;
                so.ApplyModifiedProperties();
                onStructureChanged?.Invoke();
            })
            { text = "×" });
        }

        box.Add(row);

        if (current != null)
            BuildFields(so, prop, box, onStructureChanged, fieldOverride); // 재귀 — Not(Not(...))도 편집 가능. 안쪽 노드의 필드에도 전용 칸이 적용된다

        return box;
    }

    private static object CreateNode(string key, bool isCondition)
    {
        if (isCondition)
            return InteractionNodeRegistry.TryCreateCondition(key, out InteractionCondition c) ? c : null;
        return InteractionNodeRegistry.TryCreateEffect(key, out InteractionEffect e) ? e : null;
    }

    /// <summary>필드 선언 타입으로 조건/효과를 구분한다. 값이 null이어도 판정 가능.</summary>
    private static bool IsConditionField(SerializedProperty prop)
    {
        string typeName = prop.managedReferenceFieldTypename; // "<assembly> <type>"
        return string.IsNullOrEmpty(typeName) == false
            && typeName.EndsWith(nameof(InteractionCondition), StringComparison.Ordinal);
    }

    private static string DescribeShort(object node)
    {
        return InteractionNodeRegistry.TryGetKey(node.GetType(), out string key) ? key : node.GetType().Name;
    }

    private static Label Hint(string text)
    {
        var label = new Label(text);
        label.style.opacity = 0.6f;
        label.style.whiteSpace = WhiteSpace.Normal;
        return label;
    }
}