using System.Collections.Generic;

/// <summary>
/// 편집 사본 (BehaviorTreeDocumentAsset)의 SerializedProperty 경로 — 세부 설정 · Blackboard 패널이 칸을 묶을 곳.
/// 노드: 루트 "Tree.Root", 이어지지 않은 노드 "LooseNodes.Array.data[i]", 그 아래 "….Children.Array.data[i]" · "….Decorators.Array.data[i]" · "….Services.Array.data[i]"
/// ([SerializeReference] 노드의 칸은 그 경로 아래에 그대로 있다). 필드 이름은 nameof로 — 필드 이름을 바꾸면 경로도 따라간다.
/// </summary>
public static class BehaviorNodePaths
{
    private const string ArrayItem = ".Array.data[";

    /// <summary>트리 설정 "Tree.Settings"</summary>
    public static string Settings => nameof(BehaviorTreeDocumentAsset.Tree) + "." + nameof(BehaviorTreeAsset.Settings);

    /// <summary>Blackboard 키 목록 "Tree.Keys"</summary>
    public static string Keys => nameof(BehaviorTreeDocumentAsset.Tree) + "." + nameof(BehaviorTreeAsset.Keys);

    /// <summary>Blackboard 키 하나 "Tree.Keys.Array.data[i]"</summary>
    public static string KeyAt(int index)
    {
        return Keys + ArrayItem + index + "]";
    }

    /// <summary>문서 안 노드의 경로 (Main · Decorator · Service). 문서에 없으면 null. 같은 노드 객체가 두 자리에 있으면 처음 자리. O(노드 수)</summary>
    public static string PathOf(BehaviorTreeDocumentAsset document, BehaviorNode node)
    {
        if (document == null || node == null)
            return null;
        var seen = new HashSet<BehaviorNode>();
        string root = nameof(BehaviorTreeDocumentAsset.Tree) + "." + nameof(BehaviorTreeAsset.Root);
        string found = Find(document.Tree.Root, root, node, seen);
        if (found != null)
            return found;
        for (int i = 0; i < document.LooseNodes.Count; i++)
        {
            found = Find(document.LooseNodes[i], nameof(BehaviorTreeDocumentAsset.LooseNodes) + ArrayItem + i + "]", node, seen);
            if (found != null)
                return found;
        }
        return null;
    }

    private static string Find(BehaviorMainNode main, string path, BehaviorNode target, HashSet<BehaviorNode> seen)
    {
        if (main == null || seen.Add(main) == false)
            return null;
        if (main == target)
            return path;
        string found = FindAuxiliary(main.Decorators, path + "." + nameof(BehaviorMainNode.Decorators), target);
        if (found != null)
            return found;
        found = FindAuxiliary(main.Services, path + "." + nameof(BehaviorMainNode.Services), target);
        if (found != null)
            return found;
        if (main is BehaviorComposite composite && composite.Children != null)
        {
            for (int i = 0; i < composite.Children.Length; i++)
            {
                found = Find(composite.Children[i], path + "." + nameof(BehaviorComposite.Children) + ArrayItem + i + "]", target, seen);
                if (found != null)
                    return found;
            }
        }
        return null;
    }

    private static string FindAuxiliary<T>(T[] auxiliaries, string path, BehaviorNode target) where T : BehaviorAuxiliaryNode
    {
        if (auxiliaries == null)
            return null;
        for (int i = 0; i < auxiliaries.Length; i++)
        {
            if (auxiliaries[i] != null && auxiliaries[i] == target)
                return path + ArrayItem + i + "]";
        }
        return null;
    }
}