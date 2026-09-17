using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using static Define;

/// <summary>
/// Interaction 1개 = 카드 1장.
/// 헤더 필드는 bindingPath로 SerializedObject에 묶고(Undo 자동),
/// 조건/효과 목록은 SerializeReference 배열에 바인딩된 ListView (드래그 재정렬 지원).
/// </summary>
public sealed class InteractionCardView : VisualElement
{
    public struct Callbacks
    {
        public Action<int> MoveUp, MoveDown, Duplicate, Delete;
        public Action<SerializedObject, string> NodeSelected;
    }

    private readonly SerializedObject _so;
    private readonly int _index;
    private readonly string _path;      // Set.Interactions.Array.data[i]
    private readonly Callbacks _cb;
    public const string NodeRowClass = "interaction-node-row";
    private readonly List<ListView> _nodeLists = new List<ListView>(2);

    public InteractionCardView(SerializedObject so, int index, Callbacks callbacks)
    {
        _so = so;
        _index = index;
        _cb = callbacks;
        _path = so.FindProperty("Set.Interactions").GetArrayElementAtIndex(index).propertyPath;

        style.marginLeft = 6; style.marginRight = 6; style.marginTop = 4; style.marginBottom = 4;
        style.paddingLeft = 6; style.paddingRight = 6; style.paddingTop = 4; style.paddingBottom = 6;
        style.borderTopLeftRadius = style.borderTopRightRadius = style.borderBottomLeftRadius = style.borderBottomRightRadius = 4;
        style.backgroundColor = new Color(0, 0, 0, 0.12f);
        style.borderLeftWidth = 3;
        style.borderLeftColor = new Color(0.35f, 0.6f, 0.9f);

        Add(BuildHeaderRow());
        Add(BuildPolicyRow());

        var columns = new VisualElement();
        columns.style.flexDirection = FlexDirection.Row;
        columns.style.marginTop = 4;
        columns.Add(BuildNodeList("Conditions", "Conditions", isCondition: true));
        columns.Add(BuildNodeList("Effects", "EffectPrototypes", isCondition: false));
        Add(columns);

        this.Bind(_so); // 하위 bindingPath 전부 결합 (ListView 포함)
    }

    #region 헤더
    private VisualElement BuildHeaderRow()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;

        var id = new TextField { bindingPath = _path + ".Id" };
        id.style.flexGrow = 1;
        id.style.minWidth = 120;
        row.Add(id);

        var trigger = new EnumField(ETriggerType.Tick) { bindingPath = _path + ".Trigger" };
        trigger.style.width = 110;
        row.Add(trigger);

        var mode = new EnumField(EActivationMode.Fire) { bindingPath = _path + ".Mode" };
        mode.style.width = 90;
        row.Add(mode);

        row.Add(new Button(() => _cb.MoveUp(_index)) { text = "▲", tooltip = "위로" });
        row.Add(new Button(() => _cb.MoveDown(_index)) { text = "▼", tooltip = "아래로" });
        row.Add(new Button(() => _cb.Duplicate(_index)) { text = "복제" });
        row.Add(new Button(() => _cb.Delete(_index)) { text = "삭제" });
        return row;
    }

    private VisualElement BuildPolicyRow()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;

        var cooldown = new FloatField("Cooldown") { bindingPath = _path + ".Cooldown", tooltip = "재활성까지 대기 초. 0 = 없음" };
        cooldown.style.width = 200;
        row.Add(cooldown);

        var max = new IntegerField("Max") { bindingPath = _path + ".MaxActivations", tooltip = "최대 활성 횟수. 0 = 무제한" };
        max.style.width = 160;
        row.Add(max);
        return row;
    }
    #endregion

    #region 노드 목록
    private VisualElement BuildNodeList(string title, string relativePath, bool isCondition)
    {
        string arrayPath = _path + "." + relativePath;

        var box = new VisualElement();
        box.style.flexGrow = 1;
        box.style.flexBasis = 0;
        box.style.marginRight = isCondition ? 6 : 0;

        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.Add(new Label(title) { style = { flexGrow = 1, unityFontStyleAndWeight = FontStyle.Bold } });

        Button addButton = null;
        addButton = new Button(() => ShowAddPicker(addButton, arrayPath, isCondition)) { text = "+" };
        header.Add(addButton);
        box.Add(header);

        var list = new ListView
        {
            bindingPath = arrayPath,
            reorderable = true,
            reorderMode = ListViewReorderMode.Animated,
            fixedItemHeight = 22,
            selectionType = SelectionType.Single,
            showBorder = true,
            showBoundCollectionSize = false,   // 바인딩된 배열의 Size 행 숨김
        };
        list.makeItem = () =>
        {
            var row = new VisualElement();
            row.AddToClassList(NodeRowClass);   // ← 추가: 클릭 판정용 표식
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.Add(new Label { name = "name", style = { flexGrow = 1, paddingLeft = 4 } });
            row.Add(new Label { name = "cost", style = { opacity = 0.5f, marginRight = 4 } });
            row.Add(new Button { name = "remove", text = "×" });
            return row;
        };
        list.bindItem = (element, i) =>
        {
            SerializedProperty el = _so.FindProperty(arrayPath).GetArrayElementAtIndex(i);
            object node = el.managedReferenceValue;
            element.Q<Label>("name").text = Describe(node);
            element.Q<Label>("cost").text = node is InteractionCondition c ? c.Cost.ToString() : "";
            int captured = i;
            element.Q<Button>("remove").clickable = new Clickable(() => RemoveAt(arrayPath, captured)); // 재바인딩 시 핸들러 누적 방지
        };
        list.selectionChanged += _ =>
        {
            if (list.selectedIndex < 0) return;

            // 같은 카드의 다른 목록 선택 해제 (Conditions ↔ Effects)
            for (int i = 0; i < _nodeLists.Count; i++)
                if (_nodeLists[i] != list) _nodeLists[i].ClearSelection();

            _cb.NodeSelected(_so, $"{arrayPath}.Array.data[{list.selectedIndex}]");
        };
        box.Add(list);
        _nodeLists.Add(list);
        return box;
    }

    private void ShowAddPicker(Button activator, string arrayPath, bool isCondition)
    {
        Rect rect = GUIUtility.GUIToScreenRect(activator.worldBound);
        InteractionNodePickerWindow.Open(rect, isCondition, info =>
        {
            object instance = isCondition
                ? (InteractionNodeRegistry.TryCreateCondition(info.Key, out InteractionCondition c) ? c : null)
                : (object)(InteractionNodeRegistry.TryCreateEffect(info.Key, out InteractionEffect e) ? e : null);

            if (instance != null)
                AddNode(arrayPath, instance);
        });
    }

    private void AddNode(string arrayPath, object instance)
    {
        _so.Update();
        SerializedProperty array = _so.FindProperty(arrayPath);
        array.arraySize++;
        array.GetArrayElementAtIndex(array.arraySize - 1).managedReferenceValue = instance;
        _so.ApplyModifiedProperties(); // Undo 자동 기록
    }

    private void RemoveAt(string arrayPath, int index)
    {
        _so.Update();
        SerializedProperty array = _so.FindProperty(arrayPath);
        if (index < 0 || index >= array.arraySize) return;

        int before = array.arraySize;
        array.DeleteArrayElementAtIndex(index);
        if (array.arraySize == before)          // 참조가 null로만 바뀐 경우(버전별 동작 차이) 한 번 더
            array.DeleteArrayElementAtIndex(index);
        _so.ApplyModifiedProperties();
    }

    private static string Describe(object node)
    {
        if (node == null) return "<null> — 등록 안 된 타입이거나 삭제됨";
        if (InteractionNodeRegistry.TryGetKey(node.GetType(), out string key))
        {
            var infos = node is InteractionCondition ? InteractionNodeRegistry.ConditionInfos : InteractionNodeRegistry.EffectInfos;
            for (int i = 0; i < infos.Count; i++)
                if (infos[i].Key == key) return $"{infos[i].DisplayName}  ({key})";
        }
        return node.GetType().Name;
    }

    /// <summary>창이 여백 클릭을 감지했을 때 호출. 값에는 영향이 없다.</summary>
    public void ClearNodeSelection()
    {
        for (int i = 0; i < _nodeLists.Count; i++)
            _nodeLists[i].ClearSelection(); // selectionChanged를 발생시키지 않는다
    }
    #endregion
}