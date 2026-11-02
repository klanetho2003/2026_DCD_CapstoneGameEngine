using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 변형 탭 (오른쪽 패널) — 고른 트리의 변형 목록과 고른 변형의 키 기본값 (BehaviorVariantView). 바꾸기는 Undo를 남기고 사본을 직접 바꾼다 (BehaviorVariantEditing) —
/// 패널을 만든 트리가 아니면 (그 사이 다른 트리를 고름) 하지 않는다. 고른 변형은 이름으로 기억한다 (창을 다시 만들어도 그대로 — 고른 트리에 그 이름이 없으면 첫 변형).
/// 값 칸을 바꾸면 패널은 그대로 두고 (칸이 바꾼 표시를 스스로 맞춘다) 카드 글 · 검사만 맞추고, 목록 · 이름 · 기반 값으로 바꾸기는 패널을 다시 만든다.
/// 이름 바꾸기 · 지우기는 그 변형을 가리키는 몬스터가 있으면 상태 줄에 알린다 (툴은 몬스터 데이터를 고치지 않는다 — 목록 위 '없는 변형' 알림).
/// </summary>
public sealed partial class BehaviorTreeEditorWindow
{
    [SerializeField] private string _variantName = ""; // 고른 변형의 이름 — 창을 다시 만들어도 그대로

    /// <summary>고른 트리에서 고른 변형의 자리 — 그 이름이 없으면 첫 변형, 변형이 없으면 -1</summary>
    private int SelectedVariantIndex()
    {
        BehaviorTreeVariant[] variants = _selected?.Tree.Variants;
        if (variants == null || variants.Length == 0)
            return -1;
        int index = BehaviorTreeVariants.IndexOf(variants, _variantName);
        return index >= 0 ? index : 0;
    }

    /// <summary>그 자리의 변형을 고른다 (이름으로 기억). 없는 자리면 그대로 — 패널은 부르는 쪽이 다시 만든다</summary>
    private void ChooseVariant(int index)
    {
        BehaviorTreeVariant variant = _selected != null ? BehaviorVariantEditing.At(_selected.Tree, index) : null;
        if (variant != null)
            _variantName = variant.Name ?? "";
    }

    private VisualElement BuildVariantsContent()
    {
        BehaviorTreeDocumentAsset document = _selected;
        EnsureUsers();
        Func<string, IReadOnlyList<BehaviorTreeUser>> usersOf = null;
        if (_users.CreatureTableRead)
            usersOf = _users.Get;
        return BehaviorVariantView.Build(document, SelectedVariantIndex(), usersOf, new BehaviorVariantView.Callbacks
        {
            AddVariant = () => AddVariant(document),
            SelectVariant = index => SelectVariant(document, index),
            DuplicateVariant = index => DuplicateVariant(document, index),
            RemoveVariant = index => RemoveVariant(document, index),
            RenameVariant = (index, name) => RenameVariant(document, index, name),
            SetValue = (index, value) => SetVariantValue(document, index, value),
            ClearValue = (index, key) => ClearVariantValue(document, index, key),
            RemoveValueAt = (index, valueIndex) => RemoveVariantValueAt(document, index, valueIndex),
        });
    }

    #region 변형의 바꾸기 (사본을 직접 — Undo를 먼저 남긴다)
    private void AddVariant(BehaviorTreeDocumentAsset document)
    {
        if (IsSelectedDocument(document) == false)
            return;
        Record("Behavior Tree 변형 더하기");
        ChooseVariant(BehaviorVariantEditing.Add(document.Tree));
        RequestReveal("name", -1);
        AfterVariantEdit();
    }

    private void SelectVariant(BehaviorTreeDocumentAsset document, int index)
    {
        if (IsSelectedDocument(document) == false)
            return;
        ChooseVariant(index);
        SchedulePanel();
    }

    private void DuplicateVariant(BehaviorTreeDocumentAsset document, int index)
    {
        if (IsSelectedDocument(document) == false || BehaviorVariantEditing.At(document.Tree, index) == null)
            return;
        Record("Behavior Tree 변형 복제");
        ChooseVariant(BehaviorVariantEditing.Duplicate(document.Tree, index));
        RequestReveal("name", -1);
        AfterVariantEdit();
    }

    /// <summary>변형 지우기 — 그 변형을 가리키는 몬스터가 있으면 상태 줄 (에러 색)에 수 (그 몬스터는 가만히 있게 된다)</summary>
    private void RemoveVariant(BehaviorTreeDocumentAsset document, int index)
    {
        BehaviorTreeVariant variant = IsSelectedDocument(document) ? BehaviorVariantEditing.At(document.Tree, index) : null;
        if (variant == null)
            return;
        int users = UsersOfVariant(document, variant.Name);
        Record("Behavior Tree 변형 지우기");
        BehaviorVariantEditing.Remove(document.Tree, index);
        AfterVariantEdit();
        SetStatus(users > 0
            ? $"변형 '{variant.Name}'를 지움 — 이 변형을 가리키는 몬스터 {users}개는 가만히 있게 됨 (몬스터 데이터를 고치거나 Ctrl+Z)"
            : $"변형 '{variant.Name}'를 지움", users > 0);
    }

    /// <summary>
    /// 변형 이름 바꾸기 — 쓸 수 없으면 (BehaviorVariantEditing.NameError) 바꾸지 않고 까닭 (Undo도 남기지 않는다).
    /// 옛 이름을 가리키는 몬스터가 있으면 상태 줄 (에러 색)에 — 몬스터 데이터의 트리 Id도 새 이름으로 바꿔야 한다
    /// </summary>
    private string RenameVariant(BehaviorTreeDocumentAsset document, int index, string name)
    {
        if (IsSelectedDocument(document) == false)
            return OtherTreeError;
        BehaviorTreeVariant variant = BehaviorVariantEditing.At(document.Tree, index);
        if (variant == null)
            return "변형을 찾을 수 없습니다";
        if (string.Equals(variant.Name, name, StringComparison.Ordinal))
            return null;
        string error = BehaviorVariantEditing.NameError(document.Tree, index, name);
        if (error != null)
            return error;
        string old = variant.Name;
        int users = UsersOfVariant(document, old);
        Record("Behavior Tree 변형 이름 바꾸기");
        BehaviorVariantEditing.Rename(document.Tree, index, name);
        _variantName = name;
        AfterVariantEdit();
        SetStatus(users > 0
            ? $"변형 이름을 '{name}'으로 — 옛 이름을 가리키는 몬스터 {users}개는 이 변형을 찾지 못함: 몬스터 데이터의 BehaviorTreeId를 '{BehaviorTreeVariants.FullId(document.Tree.Id, name)}'로"
            : $"변형 이름을 '{name}'으로", users > 0);
        return null;
    }

    /// <summary>변형 값 바꾸기 — 쓸 수 없으면 (BehaviorVariantEditing.SetValueError) 바꾸지 않고 까닭. 패널은 그대로 (값 칸이 바꾼 표시를 스스로 맞춘다), 카드 글 · 검사만</summary>
    private string SetVariantValue(BehaviorTreeDocumentAsset document, int index, BehaviorKeyOverride value)
    {
        if (IsSelectedDocument(document) == false)
            return OtherTreeError;
        string error = BehaviorVariantEditing.SetValueError(document.Tree, index, value);
        if (error != null)
            return error;
        Record("Behavior Tree 변형 값");
        BehaviorVariantEditing.SetValue(document.Tree, index, value);
        _selectedObject?.Update();
        AfterPanelEdit();
        return null;
    }

    /// <summary>기반 값으로 — 변형이 바꾼 그 키의 값을 지운다</summary>
    private void ClearVariantValue(BehaviorTreeDocumentAsset document, int index, string key)
    {
        BehaviorTreeVariant variant = IsSelectedDocument(document) ? BehaviorVariantEditing.At(document.Tree, index) : null;
        if (variant == null || BehaviorVariantEditing.IndexOfValue(variant, key) < 0)
            return;
        Record("Behavior Tree 변형 값 지우기");
        BehaviorVariantEditing.ClearValue(document.Tree, index, key);
        RequestReveal(BehaviorVariantEditing.ValueField(key), -1);
        AfterVariantEdit();
    }

    /// <summary>키 줄이 없는 값 (트리에 없는 키 · 같은 키 두 번) 지우기</summary>
    private void RemoveVariantValueAt(BehaviorTreeDocumentAsset document, int index, int valueIndex)
    {
        BehaviorKeyOverride[] values = IsSelectedDocument(document) ? BehaviorVariantEditing.At(document.Tree, index)?.Overrides : null;
        if (values == null || valueIndex < 0 || valueIndex >= values.Length)
            return;
        Record("Behavior Tree 변형 값 지우기");
        BehaviorVariantEditing.RemoveValueAt(document.Tree, index, valueIndex);
        AfterVariantEdit();
    }

    /// <summary>변형 목록 · 이름 · 값 지우기 뒤 — 칸의 SerializedObject를 맞추고, 패널 (목록 · 고른 변형)을 다시 만든다 (목록 위 '없는 변형' 알림은 검사 뒤에 맞춘다)</summary>
    private void AfterVariantEdit()
    {
        _selectedObject?.Update();
        AfterPanelEdit();
        SchedulePanel();
    }

    /// <summary>몬스터 데이터에서 그 변형 ("트리 Id/변형 이름")을 가리키는 몬스터 수 — 크리처 표를 읽지 못했거나 트리 Id가 없으면 0</summary>
    private int UsersOfVariant(BehaviorTreeDocumentAsset document, string variantName)
    {
        EnsureUsers();
        if (_users.CreatureTableRead == false || string.IsNullOrEmpty(document.Tree.Id))
            return 0;
        return _users.Get(BehaviorTreeVariants.FullId(document.Tree.Id, variantName)).Count;
    }
    #endregion
}