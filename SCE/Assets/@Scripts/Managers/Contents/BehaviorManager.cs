using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

/// <summary>
/// 몬스터 행동 트리 보관 — 트리 Id (= 트리 JSON의 Addressable 주소 · MonsterData.BehaviorTreeId)마다 컴파일한 정의 하나를 그 트리를 쓰는 몬스터가 함께 쓴다.
/// 변형 Id ("기반 Id/변형 이름" — BehaviorTreeVariants)는 기반 정의의 노드를 함께 쓰는 변형 정의 하나 (키 기본값만 다름).
/// 처음 쓸 때 (몬스터의 SetInfo — GetDefinition) 읽어 컴파일한다. 실패한 Id는 기억해 한 번만 알린다. 정의는 읽기 전용이라 세션 동안 그대로 쓴다.
/// </summary>
public class BehaviorManager
{
    private readonly Dictionary<string, BehaviorTreeDefinition> _definitions = new Dictionary<string, BehaviorTreeDefinition>(StringComparer.Ordinal);
    private readonly HashSet<string> _failedIds = new HashSet<string>(StringComparer.Ordinal);
    private readonly List<BehaviorCompileMessage> _messages = new List<BehaviorCompileMessage>();

    /// <summary>
    /// 트리 Id의 정의 — 처음이면 트리 JSON (주소 = Id, PreLoad label)을 읽어 컴파일한다. 변형 Id ("기반 Id/변형 이름")면 기반 정의 (없으면 읽어 컴파일)로 변형 정의를 만든다.
    /// Id가 비었으면 null. 없거나 컴파일 · 변형 에러면 편집기 에러 (문제마다 한 줄) 뒤 null이고, 그 Id는 다시 읽지 않는다. 읽은 뒤에는 O(1)
    /// </summary>
    public BehaviorTreeDefinition GetDefinition(string treeId)
    {
        if (string.IsNullOrEmpty(treeId))
            return null;
        if (_definitions.TryGetValue(treeId, out BehaviorTreeDefinition definition))
            return definition;
        if (_failedIds.Contains(treeId))
            return null;

        definition = BehaviorTreeVariants.TrySplit(treeId, out string baseId, out string variantName) ? LoadVariant(treeId, baseId, variantName) : Load(treeId);
        if (definition == null)
        {
            _failedIds.Add(treeId);
            return null;
        }
        _definitions.Add(treeId, definition);
        return definition;
    }

    /// <summary>트리 JSON을 읽어 컴파일한다 — 문제는 편집기 로그로. 실패하면 null</summary>
    private BehaviorTreeDefinition Load(string treeId)
    {
        TextAsset text = Managers.Resource.Load<TextAsset>(treeId);
        if (text == null)
        {
            LogPrinter.LogError($"[BehaviorManager] 트리 '{treeId}' 없음 — 트리 JSON의 Addressable 주소가 트리 Id와 같은지, {ResourceManager.PreLoadLabel} label이 있는지 확인");
            return null;
        }

        _messages.Clear();
        BehaviorTreeDefinition definition = BehaviorTreeLoader.Load(text.text, BehaviorNodeRegistry.Default, _messages);
        ReportMessages(treeId);
        if (definition == null)
        {
            LogPrinter.LogError($"[BehaviorManager] 트리 '{treeId}' 컴파일 실패 — 이 트리를 쓰는 몬스터는 가만히 있는다");
            return null;
        }
        if (definition.Asset.Id != treeId)
            LogPrinter.LogWarning($"[BehaviorManager] 트리 '{treeId}'의 id가 '{definition.Asset.Id}' — 주소와 같게 고칠 것");
        return definition;
    }

    /// <summary>
    /// 변형 Id의 정의 — 기반 트리의 정의 (GetDefinition — 실패는 그쪽이 알린다)에 변형의 키 기본값을 입힌다 (BehaviorTreeVariants.Build).
    /// 기반을 쓸 수 없거나 변형이 없거나 맞지 않으면 편집기 에러 뒤 null
    /// </summary>
    private BehaviorTreeDefinition LoadVariant(string treeId, string baseId, string variantName)
    {
        BehaviorTreeDefinition baseDefinition = GetDefinition(baseId);
        if (baseDefinition == null)
        {
            LogPrinter.LogError($"[BehaviorManager] 트리 '{treeId}' — 기반 트리 '{baseId}'를 쓸 수 없음 — 이 변형을 쓰는 몬스터는 가만히 있는다");
            return null;
        }

        _messages.Clear();
        BehaviorTreeDefinition definition = BehaviorTreeVariants.Build(baseDefinition, variantName, _messages);
        ReportMessages(treeId);
        if (definition == null)
            LogPrinter.LogError($"[BehaviorManager] 트리 '{treeId}' 변형 실패 — 이 변형을 쓰는 몬스터는 가만히 있는다");
        return definition;
    }

#if UNITY_EDITOR
    /// <summary>
    /// 편집기 — 트리 글 (트리 JSON)을 컴파일해 그 Id의 정의로 둔다: 있으면 바꾸고, 실패해 기억한 Id면 기억을 지운다. 그 트리의 변형 정의는 새 기반으로 바로 다시 만든다 (RebuildVariants).
    /// 나중에 스폰하는 몬스터가 이 정의를 쓴다. 변형 Id ('/'가 있는 Id)는 받지 않는다 (null).
    /// 컴파일에 실패하면 아무것도 바꾸지 않고 null (문제는 messages — 먼저 비운다). O(트리 크기 + 보관한 Id 수 + 변형 수 × 키 수)
    /// </summary>
    public BehaviorTreeDefinition ReplaceDefinition(string treeId, string json, List<BehaviorCompileMessage> messages)
    {
        messages.Clear();
        if (string.IsNullOrEmpty(treeId) || BehaviorTreeVariants.TrySplit(treeId, out _, out _))
            return null;
        BehaviorTreeDefinition definition = BehaviorTreeLoader.Load(json, BehaviorNodeRegistry.Default, messages);
        if (definition == null)
            return null;
        _definitions[treeId] = definition;
        _failedIds.Remove(treeId);
        RebuildVariants(treeId, definition);
        return definition;
    }

    /// <summary>
    /// 기반을 바꾼 뒤 — 이미 만든 그 트리의 변형 정의를 새 기반으로 다시 만든다. 만들지 못한 변형 (지웠거나 값이 맞지 않음)은 편집기 에러 뒤 이전 정의를 그대로 둔다 —
    /// 그 변형의 몬스터는 (지금 있든 나중에 스폰하든) 하던 트리 그대로 (Play를 다시 시작하면 그 변형은 실패 — 가만히 있는다).
    /// 실패를 기억한 그 트리의 변형은 잊는다 (다음 GetDefinition이 새 기반으로 다시 본다). O(보관한 Id 수 + 변형 수 × 키 수)
    /// </summary>
    private void RebuildVariants(string treeId, BehaviorTreeDefinition baseDefinition)
    {
        var variantIds = new List<string>();
        foreach (string id in _definitions.Keys)
        {
            if (id.Length > treeId.Length && BehaviorTreeVariants.BelongsTo(id, treeId))
                variantIds.Add(id);
        }
        for (int i = 0; i < variantIds.Count; i++)
        {
            string id = variantIds[i];
            BehaviorTreeVariants.TrySplit(id, out _, out string variantName);
            _messages.Clear();
            BehaviorTreeDefinition rebuilt = BehaviorTreeVariants.Build(baseDefinition, variantName, _messages);
            ReportMessages(id);
            if (rebuilt != null)
                _definitions[id] = rebuilt;
            else
                LogPrinter.LogError($"[BehaviorManager] 트리 '{id}' — 새 트리로 변형을 만들지 못함 — 이 변형의 몬스터는 이전 트리를 그대로 쓴다 (Play를 다시 시작하면 가만히 있는다)");
        }
        _failedIds.RemoveWhere(id => id.Length > treeId.Length && BehaviorTreeVariants.BelongsTo(id, treeId));
    }

    /// <summary>
    /// 편집기 — Play 중 저장한 트리를 지금 몬스터에 입힌다: 다시 컴파일해 정의를 바꾸고 (ReplaceDefinition — 변형도 새 기반으로), 그 트리 또는 그 트리의 변형을 쓰는 몬스터
    /// (ObjectManager.Monsters)를 자기 트리 Id의 새 정의로 다시 묶는다 (MonsterBase.ReloadBehaviorTree). 다시 묶은 몬스터 수, 컴파일에 실패하면 -1 (정의 · 몬스터 그대로).
    /// unchanged = 새 트리로 만들지 못한 변형의 몬스터 수 (하던 트리 그대로이거나 원래 가만히 있던 몬스터 — 까닭은 편집기 에러). O(트리 크기 + 몬스터 수)
    /// </summary>
    public int ReloadTree(string treeId, string json, List<BehaviorCompileMessage> messages, out int unchanged)
    {
        unchanged = 0;
        BehaviorTreeDefinition definition = ReplaceDefinition(treeId, json, messages);
        if (definition == null)
            return -1;
        int count = 0;
        foreach (MonsterBase monster in Managers.Object.Monsters)
        {
            string monsterTreeId = monster != null ? monster.BehaviorTreeId : null;
            if (BehaviorTreeVariants.BelongsTo(monsterTreeId, treeId) == false)
                continue;
            BehaviorTreeDefinition own = GetDefinition(monsterTreeId);
            if (own == null || ReferenceEquals(own.Asset, definition.Asset) == false)
            {
                unchanged++; // 새 기반으로 만들지 못한 변형 — 이전 정의를 두었거나 원래 실패
                continue;
            }
            if (monster.ReloadBehaviorTree(monsterTreeId, own))
                count++;
        }
        return count;
    }

    /// <summary>ReloadTree와 같다 — 새 트리로 만들지 못한 변형의 몬스터 수는 받지 않는다</summary>
    public int ReloadTree(string treeId, string json, List<BehaviorCompileMessage> messages)
    {
        return ReloadTree(treeId, json, messages, out _);
    }

    /// <summary>편집기 — 그 Id의 트리를 읽거나 만들지 못해 기억했는가 (디버거가 그 몬스터에 트리가 없는 까닭을 가린다). O(1)</summary>
    public bool HasFailed(string treeId)
    {
        return string.IsNullOrEmpty(treeId) == false && _failedIds.Contains(treeId);
    }
#endif

    /// <summary>컴파일 문제를 한 줄씩 — 편집기에서만</summary>
    [Conditional("UNITY_EDITOR")]
    private void ReportMessages(string treeId)
    {
        for (int i = 0; i < _messages.Count; i++)
        {
            BehaviorCompileMessage message = _messages[i];
            if (message.IsError)
                LogPrinter.LogError($"[BehaviorManager] 트리 '{treeId}' — {message}");
            else
                LogPrinter.LogWarning($"[BehaviorManager] 트리 '{treeId}' — {message}");
        }
    }
}

/// <summary>게임의 트리 밖 상태 — Managers의 지금 것 (조종 캐릭터). 상태가 없어 하나를 함께 쓴다</summary>
public sealed class BehaviorGameWorld : IBehaviorWorld
{
    public static readonly BehaviorGameWorld Instance = new BehaviorGameWorld();

    private BehaviorGameWorld() { }

    public CreatureBase PossessedTarget { get { return Managers.Object.PossessedTarget; } }
}
