using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 노드 고르기 팝업 — 등록소 (BehaviorNodeRegistry)의 노드를 공용 검색 팝업 (EditorSearchPicker)에 넣어 연다. 그래프의 노드 추가 · Decorator · Service 추가가 쓴다.
/// 차례: 고를 종류 차례, 같은 종류 안에서는 묶음 (BehaviorNodeAttribute.Category — 처음 나온 차례)끼리, 묶음 안은 등록 차례. 묶음 이름은 오른쪽 표시 (Badge).
/// </summary>
public static class BehaviorNodePicker
{
    /// <param name="kinds">보일 종류 — 이 차례로</param>
    /// <param name="onPick">고른 노드의 등록 정보</param>
    public static void Show(Rect activatorScreenRect, string header, BehaviorNodeRegistry registry, IReadOnlyList<EBehaviorNodeKind> kinds, Action<BehaviorNodeRegistry.NodeInfo> onPick)
    {
        EditorSearchPicker.Show(activatorScreenRect, header, BuildItems(registry, kinds), null,
            item => onPick((BehaviorNodeRegistry.NodeInfo)item.Payload));
    }

    /// <summary>팝업 항목 — 종류 · 묶음 차례. 항목의 Payload는 NodeInfo. O(종류 수 × 묶음 수 × 노드 수) — 팝업을 열 때 한 번</summary>
    public static List<EditorSearchPicker.Item> BuildItems(BehaviorNodeRegistry registry, IReadOnlyList<EBehaviorNodeKind> kinds)
    {
        var items = new List<EditorSearchPicker.Item>();
        var categories = new List<string>();
        IReadOnlyList<BehaviorNodeRegistry.NodeInfo> infos = registry.Infos;
        for (int k = 0; k < kinds.Count; k++)
        {
            categories.Clear();
            for (int i = 0; i < infos.Count; i++)
            {
                if (infos[i].Kind == kinds[k] && categories.Contains(infos[i].Category) == false)
                    categories.Add(infos[i].Category);
            }
            for (int c = 0; c < categories.Count; c++)
            {
                for (int i = 0; i < infos.Count; i++)
                {
                    BehaviorNodeRegistry.NodeInfo info = infos[i];
                    if (info.Kind == kinds[k] && info.Category == categories[c])
                        items.Add(new EditorSearchPicker.Item(Title(in info), info.Description, info, badge: info.Category));
                }
            }
        }
        return items;
    }

    /// <summary>노드 제목 "표시 이름  (키)" — 표시 이름이 키와 같으면 키만. 고르기 항목 · 카드 알림 글이 같은 글자</summary>
    public static string Title(in BehaviorNodeRegistry.NodeInfo info)
    {
        return info.DisplayName == info.Key ? info.Key : $"{info.DisplayName}  ({info.Key})";
    }
}