using System;
using System.Collections.Generic;
using System.Text;
using Data;
using static Define;

/// <summary>트리를 쓰는 몬스터 하나 — 몬스터 데이터 행 (templateID · prefabName)</summary>
public readonly struct BehaviorTreeUser
{
    public readonly int TemplateId;
    public readonly string Name;

    public BehaviorTreeUser(int templateId, string name)
    {
        TemplateId = templateId;
        Name = name ?? "";
    }
}

/// <summary>
/// 트리 Id → 그 트리를 쓰는 몬스터 (MonsterData.BehaviorTreeId). 크리처 표 (게임과 같은 합치기 — EditorGameData.ReadCreatureTable)로 만든다 — 순수 로직, 테스트 가능.
/// 빈 BehaviorTreeId는 기본 트리 (MonsterBase.DEFAULT_BEHAVIOR_TREE_ID)를 쓰는 것으로 센다 (게임과 같다). 트리 Id는 대소문자까지 같아야 한다 (게임의 주소 찾기와 같다).
/// 변형을 가리키는 Id ("트리 Id/변형 이름" — BehaviorTreeVariants)는 그 글 그대로 센다.
/// </summary>
public sealed class BehaviorTreeUsers
{
    private static readonly IReadOnlyList<BehaviorTreeUser> s_none = Array.Empty<BehaviorTreeUser>();

    private readonly Dictionary<string, List<BehaviorTreeUser>> _byTree = new Dictionary<string, List<BehaviorTreeUser>>(StringComparer.Ordinal);

    /// <summary>크리처 표를 읽었는가 — false면 쓰는 몬스터를 모른다 (없다는 뜻이 아니다)</summary>
    public bool CreatureTableRead { get; private set; }

    private BehaviorTreeUsers()
    {
    }

    /// <summary>읽지 못한 표는 null로 넘긴다. 몬스터 표의 행만 본다. O(데이터 행 수) + 트리마다 templateID 순 정렬</summary>
    public static BehaviorTreeUsers Build(MergedCreatureTable creatures)
    {
        var users = new BehaviorTreeUsers { CreatureTableRead = creatures != null };
        if (creatures == null)
            return users;

        foreach (CreatureTableRow row in creatures.Rows)
        {
            if (row.Table != EObjectType.Monster || row.Row is MonsterData monster == false)
                continue;
            string treeId = string.IsNullOrEmpty(monster.BehaviorTreeId) ? MonsterBase.DEFAULT_BEHAVIOR_TREE_ID : monster.BehaviorTreeId;
            if (users._byTree.TryGetValue(treeId, out List<BehaviorTreeUser> list) == false)
                users._byTree.Add(treeId, list = new List<BehaviorTreeUser>());
            list.Add(new BehaviorTreeUser(monster.templateID, monster.prefabName));
        }
        foreach (List<BehaviorTreeUser> list in users._byTree.Values)
            list.Sort((a, b) => a.TemplateId.CompareTo(b.TemplateId));
        return users;
    }

    /// <summary>그 트리를 쓰는 몬스터 (templateID 순). 없으면 빈 목록. O(1)</summary>
    public IReadOnlyList<BehaviorTreeUser> Get(string treeId)
    {
        return string.IsNullOrEmpty(treeId) == false && _byTree.TryGetValue(treeId, out List<BehaviorTreeUser> list) ? list : s_none;
    }

    /// <summary>
    /// 그 트리 또는 그 변형 ("treeId/변형 이름")을 쓰는 몬스터 (templateID 순) — 파일 없는 트리를 만들면 그 트리를 찾게 될 몬스터. variantUsers = 그 가운데 변형을 가리키는 몬스터 수.
    /// 없으면 빈 목록. O(가리키는 Id 수 + 찾은 몬스터 수 · log)
    /// </summary>
    public IReadOnlyList<BehaviorTreeUser> GetWithVariants(string treeId, out int variantUsers)
    {
        variantUsers = 0;
        if (string.IsNullOrEmpty(treeId))
            return s_none;
        List<BehaviorTreeUser> found = null;
        foreach (KeyValuePair<string, List<BehaviorTreeUser>> pair in _byTree)
        {
            if (BehaviorTreeVariants.BelongsTo(pair.Key, treeId) == false)
                continue;
            if (pair.Key.Length > treeId.Length)
                variantUsers += pair.Value.Count;
            if (found == null)
                found = new List<BehaviorTreeUser>();
            found.AddRange(pair.Value);
        }
        if (found == null)
            return s_none;
        found.Sort((a, b) => a.TemplateId.CompareTo(b.TemplateId));
        return found;
    }

    /// <summary>"Goblin (1001) · Orc (1002) 외 3개" — 앞의 max개. 없으면 "없음". O(max)</summary>
    public static string Describe(IReadOnlyList<BehaviorTreeUser> users, int max)
    {
        if (users == null || users.Count == 0)
            return "없음";
        var text = new StringBuilder();
        int shown = Math.Min(users.Count, Math.Max(max, 1));
        for (int i = 0; i < shown; i++)
        {
            if (i > 0)
                text.Append(" · ");
            text.Append(users[i].Name).Append(" (").Append(users[i].TemplateId).Append(')');
        }
        if (users.Count > shown)
            text.Append(" 외 ").Append(users.Count - shown).Append("개");
        return text.ToString();
    }

    /// <summary>
    /// 몬스터가 가리키는데 known (있는 트리 Id · 파일 이름 — 대소문자까지)에 없는 트리 Id — 먼저 비우고 Id 순 (Ordinal).
    /// 대소문자만 다른 트리가 있는 Id는 caseOnly에 (게임은 못 찾지만 그 Id로 트리를 만들 수 없다 — 파일 이름이 겹친다. 몬스터 데이터를 고칠 것), 나머지는 missing에. O(가리키는 Id 수 · log)
    /// 변형을 가리키는 Id는 known에 그 변형 Id가 있어야 있는 것 — 없는 트리면 기반 Id, 없는 변형이면 그 Id 그대로 missing에 (아래 CollectMissing)
    /// </summary>
    public void CollectMissing(ISet<string> known, List<string> missing, List<string> caseOnly)
    {
        var missingVariants = new List<string>();
        CollectMissing(known, null, missing, caseOnly, missingVariants);
        missing.AddRange(missingVariants);
        missing.Sort(StringComparer.Ordinal);
    }

    /// <summary>
    /// 몬스터가 가리키는데 없는 트리 · 변형 — 모두 먼저 비우고 Id 순 (Ordinal). known = 있는 트리 Id · 파일 이름과 그 변형 Id ("트리 Id/변형 이름" — 대소문자까지),
    /// unread = 파일은 있는데 읽지 못해 변형을 모르는 트리 (그 트리의 변형 Id는 있는 것으로 본다, 없으면 null).
    ///  - missingTrees: 트리가 없는 Id — 변형 Id면 그 기반 Id (겹침 없이 — [+ 새 트리]가 만들 수 있는 이름)
    ///  - caseOnly: 대소문자만 다른 트리 · 변형이 있는 Id (그대로 — 게임은 못 찾는다, 몬스터 데이터를 고칠 것)
    ///  - missingVariants: 기반 트리는 있는데 그 이름의 변형이 없는 Id (그대로)
    /// O(가리키는 Id 수 · log)
    /// </summary>
    public void CollectMissing(ISet<string> known, ISet<string> unread, List<string> missingTrees, List<string> caseOnly, List<string> missingVariants)
    {
        missingTrees.Clear();
        caseOnly.Clear();
        missingVariants.Clear();
        var knownIgnoringCase = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);
        var trees = new HashSet<string>(StringComparer.Ordinal);
        foreach (string treeId in _byTree.Keys)
        {
            if (known.Contains(treeId))
                continue;
            bool isVariant = BehaviorTreeVariants.TrySplit(treeId, out string baseId, out _);
            if (isVariant && unread != null && unread.Contains(baseId))
                continue;
            if (knownIgnoringCase.Contains(treeId))
                caseOnly.Add(treeId);
            else if (isVariant == false)
                AddOnce(missingTrees, trees, treeId);
            else if (known.Contains(baseId))
                missingVariants.Add(treeId);
            else if (knownIgnoringCase.Contains(baseId))
                caseOnly.Add(treeId);
            else
                AddOnce(missingTrees, trees, baseId);
        }
        missingTrees.Sort(StringComparer.Ordinal);
        caseOnly.Sort(StringComparer.Ordinal);
        missingVariants.Sort(StringComparer.Ordinal);
    }

    private static void AddOnce(List<string> list, HashSet<string> seen, string id)
    {
        if (seen.Add(id))
            list.Add(id);
    }

    /// <summary>그 트리 Id와 대소문자만 다른 Id를 가리키는 몬스터가 있는 Id들 — 먼저 비우고 Id 순 (Ordinal). O(가리키는 Id 수)</summary>
    public void CollectCaseVariants(string treeId, List<string> into)
    {
        into.Clear();
        if (string.IsNullOrEmpty(treeId))
            return;
        foreach (string id in _byTree.Keys)
        {
            if (string.Equals(id, treeId, StringComparison.OrdinalIgnoreCase) && string.Equals(id, treeId, StringComparison.Ordinal) == false)
                into.Add(id);
        }
        into.Sort(StringComparer.Ordinal);
    }
}