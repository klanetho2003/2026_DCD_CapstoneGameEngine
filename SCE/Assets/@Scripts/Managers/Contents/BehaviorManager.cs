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
    private const string PreLoadLabel = "PreLoad"; // 트리 JSON의 Addressable label — 타이틀의 미리 읽기 (UI_TitleScene)와 같은 label

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
            LogPrinter.LogError($"[BehaviorManager] 트리 '{treeId}' 없음 — 트리 JSON의 Addressable 주소가 트리 Id와 같은지, {PreLoadLabel} label이 있는지 확인");
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
