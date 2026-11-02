using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// breakpoint 목록 — 트리 Id마다 노드 이름표 모음. 이 PC에만 남긴다 (EditorPrefs — 키 = keyPrefix + 트리 Id, 값 = 이름표들을 개행으로 나눈 글), 트리 JSON · 배치 파일에는 들어가지 않는다.
/// 트리마다 처음 물을 때 한 번 읽어 둔다 — 실행기의 알림마다 묻는다 (Contains — 사전 찾기). 노드 이름표를 바꾸면 그 breakpoint는 옛 이름표에 남아 걸리지 않는다
/// </summary>
public sealed class BehaviorBreakpoints
{
    private const char Separator = '\n';

    private readonly string _keyPrefix;
    private readonly Dictionary<string, HashSet<string>> _byTree = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

    /// <param name="keyPrefix">EditorPrefs 키 앞 — 프로젝트 (ProjectKeyPrefix) · 시험마다 다르게</param>
    public BehaviorBreakpoints(string keyPrefix)
    {
        _keyPrefix = keyPrefix ?? "";
    }

    /// <summary>이 프로젝트의 키 앞 — 프로젝트 폴더마다 다르다 (EditorPrefs는 같은 PC의 모든 프로젝트가 함께 쓴다)</summary>
    public static string ProjectKeyPrefix()
    {
        return "BehaviorTreeEditor.Breakpoints." + StableHash(Application.dataPath) + ".";
    }

    /// <summary>그 트리의 그 노드에 breakpoint가 있는가 — 빈 Id · 빈 이름표면 false. O(1)</summary>
    public bool Contains(string treeId, string nodeId)
    {
        if (string.IsNullOrEmpty(treeId) || string.IsNullOrEmpty(nodeId))
            return false;
        return Of(treeId).Contains(nodeId);
    }

    /// <summary>그 트리의 breakpoint 수</summary>
    public int CountOf(string treeId)
    {
        return string.IsNullOrEmpty(treeId) ? 0 : Of(treeId).Count;
    }

    /// <summary>켜고 끈다 — 켜졌으면 true. 빈 Id · 빈 이름표면 아무것도 하지 않고 false. 바뀌면 바로 남긴다</summary>
    public bool Toggle(string treeId, string nodeId)
    {
        if (string.IsNullOrEmpty(treeId) || string.IsNullOrEmpty(nodeId))
            return false;
        HashSet<string> nodes = Of(treeId);
        bool on = nodes.Add(nodeId);
        if (on == false)
            nodes.Remove(nodeId);
        Save(treeId, nodes);
        return on;
    }

    /// <summary>그 트리의 breakpoint를 모두 끈다</summary>
    public void Clear(string treeId)
    {
        if (string.IsNullOrEmpty(treeId))
            return;
        HashSet<string> nodes = Of(treeId);
        if (nodes.Count == 0)
            return;
        nodes.Clear();
        Save(treeId, nodes);
    }

    /// <summary>그 트리의 모음 — 처음이면 EditorPrefs에서 읽는다</summary>
    private HashSet<string> Of(string treeId)
    {
        if (_byTree.TryGetValue(treeId, out HashSet<string> nodes))
            return nodes;
        nodes = new HashSet<string>(StringComparer.Ordinal);
        string saved = EditorPrefs.GetString(_keyPrefix + treeId, "");
        foreach (string nodeId in saved.Split(Separator))
        {
            if (nodeId.Length > 0)
                nodes.Add(nodeId);
        }
        _byTree.Add(treeId, nodes);
        return nodes;
    }

    /// <summary>남긴다 — 비었으면 키를 지운다. 이름표 차례로 (같은 모음은 같은 글)</summary>
    private void Save(string treeId, HashSet<string> nodes)
    {
        string key = _keyPrefix + treeId;
        if (nodes.Count == 0)
        {
            EditorPrefs.DeleteKey(key);
            return;
        }
        var sorted = new List<string>(nodes);
        sorted.Sort(StringComparer.Ordinal);
        EditorPrefs.SetString(key, string.Join(Separator.ToString(), sorted));
    }

    /// <summary>FNV-1a 32비트 — 실행마다 같은 값 (string.GetHashCode는 실행마다 다를 수 있다)</summary>
    private static string StableHash(string text)
    {
        uint hash = 2166136261u;
        foreach (char c in text ?? "")
        {
            hash ^= c;
            hash *= 16777619u;
        }
        return hash.ToString("x8");
    }
}