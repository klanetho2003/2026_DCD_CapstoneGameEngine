using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 노드 선택 팝업 — 조건·효과 레지스트리 목록을 공용 검색 팝업(EditorSearchPicker)에 넣어 연다.
/// 카드의 [+](노드 추가)와 파라미터 패널의 중첩 노드 선택이 같이 쓴다.
/// 팝업 자체(검색·키보드·선택)는 EditorSearchPicker 한 곳에 있다 — 상태 키 선택과 같은 조작법이 된다.
/// </summary>
public static class InteractionNodePicker
{
    /// <param name="currentKey">지금 노드의 레지스트리 키 — 목록에서 미리 선택해 둔다 (없으면 null)</param>
    /// <param name="onPick">고른 노드의 레지스트리 키를 받는다</param>
    public static void Show(Rect activatorScreenRect, bool isCondition, string currentKey, Action<string> onPick)
    {
        var infos = isCondition ? InteractionNodeRegistry.ConditionInfos : InteractionNodeRegistry.EffectInfos;

        var items = new List<EditorSearchPicker.Item>(infos.Count);
        for (int i = 0; i < infos.Count; i++)
        {
            InteractionNodeRegistry.NodeInfo info = infos[i];
            items.Add(new EditorSearchPicker.Item($"{info.DisplayName}  ({info.Key})", info.Description, info.Key));
        }

        string header = isCondition ? "조건 노드 — 이름·키·설명으로 검색" : "효과 노드 — 이름·키·설명으로 검색";
        EditorSearchPicker.Show(activatorScreenRect, header, items, currentKey, item => onPick((string)item.Payload));
    }
}