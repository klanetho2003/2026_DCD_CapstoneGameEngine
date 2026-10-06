using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

/// <summary>규칙 탭 · 키 선택 팝업 항목.</summary>
public sealed partial class StateDefinitionEditorWindow
{
    private StateRuleListView _ruleList;

    private VisualElement BuildRulesTab()
    {
        _ruleList = new StateRuleListView(_selectedObject, BuildKeyItems, DescribeSignalSenders);
        _ruleList.KeySelected = InspectKey;
        return _ruleList;
    }

    /// <summary>
    /// 키 선택 팝업 항목: 시스템 키 + 모든 파일에 선언된 키 (저장하지 않은 편집 내용 포함).
    /// forWrite(값 변경용)이면 시스템 키를 뺀다 — 규칙으로 바꿀 수 없으므로 고를 수조차 없게.
    /// 이름 규칙에 어긋난 키와 중복 선언(두 번째부터)은 뺀다. 그런 키는 키 표에서 빨갛게 보인다.
    /// </summary>
    private List<EditorSearchPicker.Item> BuildKeyItems(bool forWrite)
    {
        var items = new List<EditorSearchPicker.Item>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (forWrite == false)
        {
            var systemKeys = new StateKeyRegistry();
            GameStateManager.RegisterSystemKeys(systemKeys);
            for (int i = 0; i < systemKeys.Count; i++)
            {
                StateKeyDefinition definition = systemKeys.GetDefinition(i);
                if (seen.Add(definition.Key))
                    items.Add(new EditorSearchPicker.Item(definition.Key, $"{definition.Description} · 시스템 (읽기만 가능)", definition.Key));
            }
        }

        var declared = new List<EditorSearchPicker.Item>();
        for (int a = 0; a < _assets.Count; a++)
        {
            StateDefinitionAsset asset = _assets[a];
            StateKeyDefinition[] keys = asset.Set.Keys;
            for (int k = 0; k < keys.Length; k++)
            {
                StateKeyDefinition definition = keys[k];
                if (definition == null || StateKeyRegistry.IsValidKeyName(definition.Key, out _) == false)
                    continue;
                if (definition.Key.StartsWith(StateKeyRegistry.ReservedPrefix, StringComparison.Ordinal))
                    continue;
                if (seen.Add(definition.Key) == false)
                    continue;

                string description = string.IsNullOrEmpty(definition.Description) ? "" : definition.Description + " · ";
                declared.Add(new EditorSearchPicker.Item(definition.Key,
                    $"{description}{FileLabel(asset)} · {EditorEnumLabels.Of(definition.Reset)}", definition.Key));
            }
        }
        declared.Sort((x, y) => string.CompareOrdinal(x.Title, y.Title));
        items.AddRange(declared); // 시스템 키가 맨 위, 나머지는 이름순
        return items;
    }
}