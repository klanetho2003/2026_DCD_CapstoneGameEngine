using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine.UIElements;

/// <summary>
/// 트리를 쓰는 몬스터 — 몬스터 데이터 (크리처 표)의 BehaviorTreeId로 찾는다 (BehaviorTreeUsers). 변형을 가리키는 Id는 "트리 Id/변형 이름".
///  - 트리 설정 (ROOT 카드)에 쓰는 몬스터를 보인다 (UsersText — 변형을 쓰는 몬스터 수도, 변형마다는 [변형] 탭)
///  - 검사 (경고): 저장된 Id (파일 이름 — 게임이 지금 찾는 이름)를 쓰는 몬스터가 있는데 트리 Id를 바꿨다 — 그대로 저장하면 그 몬스터들은 트리를 찾지 못한다.
///    트리 Id와 대소문자만 다른 Id를 가리키는 몬스터 — 게임은 대소문자까지 같아야 찾는다. 툴은 몬스터 데이터를 고치지 않는다 (데이터 표는 기획 · 데이터 길로 고친다)
///  - 파일 없는 트리: 몬스터가 가리키는데 열린 트리 · 파일이 없는 트리 Id — 목록 위에 수 (툴팁에 Id · 몬스터), [+ 새 트리]가 그중에서 고르게 한다.
///    대소문자만 다른 트리가 있는 Id는 따로 센다 (그 Id로는 만들 수 없다 — 몬스터 데이터를 고칠 것). 트리는 있는데 그 변형이 없는 Id도 따로 센다 (없는 변형)
/// 데이터 표는 게임과 같은 주소로 읽고 (EditorGameData), 마지막으로 읽은 파일이 바뀌었을 때만 다시 읽는다 (창 포커스마다 서명 비교). 주소 등록이 바뀐 것은 [새로고침]이 다시 읽는다.
/// </summary>
public sealed partial class BehaviorTreeEditorWindow
{
    private const int MaxUsersShown = 4;          // 이름으로 적는 몬스터 수 (넘으면 "외 n개")
    private const int MissingShownInTooltip = 15; // 파일 없는 트리 툴팁에 적는 수

    private BehaviorTreeUsers _users;                                  // 첫 읽기 전에는 null
    private readonly List<string> _dataProblems = new List<string>();  // 데이터 표 읽기 문제 — 파일 없는 트리 툴팁
    private readonly List<string> _dataFiles = new List<string>();     // 마지막으로 읽은 데이터 파일 — 바뀌었는지 비교용
    private long _dataSignature;
    private readonly List<string> _missingTreeIds = new List<string>();
    private readonly List<string> _caseOnlyTreeIds = new List<string>();  // 몬스터가 가리키는데 대소문자만 다른 트리만 있는 Id
    private readonly List<string> _missingVariantIds = new List<string>(); // 몬스터가 가리키는데 트리는 있고 그 변형이 없는 Id ("트리 Id/변형 이름")
    private readonly HashSet<string> _unreadTreeIds = new HashSet<string>(StringComparer.Ordinal); // 파싱 실패 파일의 이름 — 변형을 모른다
    private readonly List<string> _caseVariants = new List<string>();     // 검사 — 한 트리의 대소문자만 다른 Id
    private readonly HashSet<string> _knownTreeIds = new HashSet<string>(StringComparer.Ordinal);
    private Label _missingLabel;

    #region 데이터 표 읽기
    /// <summary>데이터 표를 다시 읽는다. force가 아니면 마지막으로 읽은 파일이 바뀌었을 때만 — 창이 포커스를 받을 때마다 불린다. 다시 읽었으면 모든 트리를 다시 검사하고 패널을 다시 만든다</summary>
    private void RescanUsers(bool force)
    {
        if (force == false && _users != null && EditorJsonFolder.FilesSignature(_dataFiles) == _dataSignature)
            return;
        ScanUsers();
        ScheduleChecks(all: true);
        SchedulePanel();
    }

    /// <summary>결과를 쓰기 전에 한 번은 읽혀 있도록 한다</summary>
    private void EnsureUsers()
    {
        if (_users == null)
            ScanUsers();
    }

    private void ScanUsers()
    {
        _dataProblems.Clear();
        Dictionary<string, AddressableAssetEntry> addresses = EditorGameData.BuildAddressIndex();
        MergedCreatureTable creatures = EditorGameData.ReadCreatureTable(addresses, _dataProblems);
        _users = BehaviorTreeUsers.Build(creatures);
        EditorGameData.CollectDataFiles(addresses, _dataFiles);
        _dataSignature = EditorJsonFolder.FilesSignature(_dataFiles);
    }
    #endregion

    #region 트리 설정 · 검사
    /// <summary>트리 설정에 보일 쓰는 몬스터 — 글과 툴팁 (모두 — 변형을 쓰는 몬스터는 변형마다)</summary>
    private string UsersText(BehaviorTreeDocumentAsset document, out string tooltip)
    {
        EnsureUsers();
        tooltip = null;
        if (_users.CreatureTableRead == false)
        {
            tooltip = string.Join("\n", _dataProblems);
            return "쓰는 몬스터: 알 수 없음 — 크리처 표를 읽지 못함";
        }
        IReadOnlyList<BehaviorTreeUser> users = _users.Get(document.Tree.Id);
        var lines = new List<string>();
        if (users.Count > 0)
            lines.Add(BehaviorTreeUsers.Describe(users, int.MaxValue));
        int variantUsers = CountVariantUsers(document, document.Tree.Id, lines);
        if (users.Count == 0 && variantUsers == 0)
            return "쓰는 몬스터: 없음 — 몬스터 데이터의 BehaviorTreeId가 이 Id를 가리키지 않음";
        tooltip = string.Join("\n", lines);
        string text = users.Count > 0 ? $"쓰는 몬스터 {users.Count}: {BehaviorTreeUsers.Describe(users, MaxUsersShown)}" : "쓰는 몬스터: 변형만";
        return variantUsers > 0 ? text + $" · 변형을 쓰는 몬스터 {variantUsers} ([변형] 탭)" : text;
    }

    /// <summary>문서의 변형 ("treeId/변형 이름")을 가리키는 몬스터 수 — lines에 변형마다 "변형 이름: 몬스터 …"를 더한다 (없으면 null). O(변형 수)</summary>
    private int CountVariantUsers(BehaviorTreeDocumentAsset document, string treeId, List<string> lines)
    {
        BehaviorTreeVariant[] variants = document.Tree.Variants;
        if (string.IsNullOrEmpty(treeId) || variants == null)
            return 0;
        int count = 0;
        for (int i = 0; i < variants.Length; i++)
        {
            if (variants[i] == null)
                continue;
            IReadOnlyList<BehaviorTreeUser> users = _users.Get(BehaviorTreeVariants.FullId(treeId, variants[i].Name));
            if (users.Count == 0)
                continue;
            count += users.Count;
            lines?.Add($"변형 {variants[i].Name}: {BehaviorTreeUsers.Describe(users, int.MaxValue)}");
        }
        return count;
    }

    /// <summary>
    /// 검사 줄 (경고 — 트리 자리 · id 칸). 크리처 표를 읽지 못했으면 보지 않는다:
    ///  - 저장된 Id (파일 이름)를 쓰는 몬스터가 있는데 트리 Id가 다르다 — 이 Id로 저장하면 그 몬스터들은 트리를 찾지 못한다
    ///  - 트리 Id와 대소문자만 다른 Id를 가리키는 몬스터가 있다 — 게임은 대소문자까지 같아야 찾는다
    /// </summary>
    private void AddUserItems(BehaviorTreeDocumentAsset document, List<BehaviorTreeCheckItem> items)
    {
        EnsureUsers();
        string savedId = string.IsNullOrEmpty(document.FilePath) ? null : Path.GetFileNameWithoutExtension(document.FilePath);
        if (savedId != null && string.Equals(savedId, document.Tree.Id, StringComparison.Ordinal) == false)
        {
            IReadOnlyList<BehaviorTreeUser> users = _users.Get(savedId);
            int variantUsers = CountVariantUsers(document, savedId, null);
            if (users.Count > 0)
                items.Add(new BehaviorTreeCheckItem(EBehaviorMessageSeverity.Warning, EBehaviorCheckPlace.Tree, null, -1, "id",
                    $"저장된 Id '{savedId}' — 몬스터 {users.Count}개가 씀 ({BehaviorTreeUsers.Describe(users, MaxUsersShown)}). " +
                    $"지금 Id로 저장하면 그 몬스터들은 이 트리를 찾지 못함 — 몬스터 데이터의 BehaviorTreeId도 '{document.Tree.Id}'로 바꿀 것"));
            if (variantUsers > 0)
                items.Add(new BehaviorTreeCheckItem(EBehaviorMessageSeverity.Warning, EBehaviorCheckPlace.Tree, null, -1, "id",
                    $"저장된 Id '{savedId}'의 변형을 몬스터 {variantUsers}개가 씀 ('{savedId}{BehaviorTreeVariants.Separator}변형 이름'). " +
                    $"지금 Id로 저장하면 그 몬스터들은 변형을 찾지 못함 — 몬스터 데이터의 BehaviorTreeId도 '{document.Tree.Id}{BehaviorTreeVariants.Separator}변형 이름'으로 바꿀 것"));
        }

        _users.CollectCaseVariants(document.Tree.Id, _caseVariants);
        for (int i = 0; i < _caseVariants.Count; i++)
        {
            if (string.Equals(_caseVariants[i], savedId, StringComparison.Ordinal))
                continue; // 위의 경고가 알린다 (저장된 Id만 대소문자가 다른 이름 바꾸기)
            IReadOnlyList<BehaviorTreeUser> users = _users.Get(_caseVariants[i]);
            items.Add(new BehaviorTreeCheckItem(EBehaviorMessageSeverity.Warning, EBehaviorCheckPlace.Tree, null, -1, "id",
                $"몬스터 {users.Count}개가 대소문자만 다른 Id '{_caseVariants[i]}'를 가리킴 ({BehaviorTreeUsers.Describe(users, MaxUsersShown)}) — " +
                $"게임은 대소문자까지 같아야 찾으므로 그 몬스터들은 이 트리를 찾지 못함. 몬스터 데이터의 BehaviorTreeId를 '{document.Tree.Id}'로 바꿀 것"));
        }
        _caseVariants.Clear();
    }
    #endregion

    #region 파일 없는 트리
    /// <summary>
    /// 몬스터가 가리키는데 트리가 없는 Id → _missingTreeIds (변형 Id면 기반 Id), 그 가운데 대소문자만 다른 트리 · 변형이 있는 Id → _caseOnlyTreeIds,
    /// 트리는 있는데 그 변형이 없는 Id → _missingVariantIds (Id 순).
    /// "있음" = 열린 트리의 Id (저장 전 포함) + 디스크의 트리 파일 이름 (열린 트리의 파일 · 파싱 실패 파일)과 열린 트리의 변형 ("Id/변형 이름" · "파일 이름/변형 이름").
    /// 파싱 실패 파일의 변형은 모르므로 있는 것으로 본다. 파일 이름도 보는 까닭 — Id를 바꾸고 아직 저장하지 않았으면 디스크에는 옛 이름의 파일이 그대로 있고 게임도 그것을 읽는다.
    /// 데이터 표를 읽지 못했으면 비어 있다. O(트리 수 × 변형 수 + 가리키는 Id 수)
    /// </summary>
    private void CollectMissingTreeIds()
    {
        _missingTreeIds.Clear();
        _caseOnlyTreeIds.Clear();
        _missingVariantIds.Clear();
        if (_users == null)
            return;

        _knownTreeIds.Clear();
        _unreadTreeIds.Clear();
        for (int i = 0; i < _documents.Count; i++)
        {
            BehaviorTreeDocumentAsset document = _documents[i];
            AddKnownTree(document, document.Tree.Id ?? "");
            if (string.IsNullOrEmpty(document.FilePath) == false)
                AddKnownTree(document, Path.GetFileNameWithoutExtension(document.FilePath));
        }
        for (int i = 0; i < _brokenFiles.Count; i++)
        {
            string name = Path.GetFileNameWithoutExtension(_brokenFiles[i].FilePath);
            _knownTreeIds.Add(name);
            _unreadTreeIds.Add(name);
        }
        _users.CollectMissing(_knownTreeIds, _unreadTreeIds, _missingTreeIds, _caseOnlyTreeIds, _missingVariantIds);
    }

    /// <summary>있는 트리 Id와 그 문서의 변형 Id ("treeId/변형 이름")를 _knownTreeIds에</summary>
    private void AddKnownTree(BehaviorTreeDocumentAsset document, string treeId)
    {
        _knownTreeIds.Add(treeId);
        BehaviorTreeVariant[] variants = document.Tree.Variants;
        for (int v = 0; variants != null && v < variants.Length; v++)
        {
            if (variants[v] != null)
                _knownTreeIds.Add(BehaviorTreeVariants.FullId(treeId, variants[v].Name ?? ""));
        }
    }

    /// <summary>
    /// 목록 위 알림 — "파일 없는 트리 n개" · "대소문자만 다른 트리 Id m개" · "없는 변형 k개" (툴팁에 Id와 쓰는 몬스터, 묶음마다 앞의 MissingShownInTooltip개). 모두 없으면 숨김
    /// </summary>
    private void UpdateMissingLabel()
    {
        if (_missingLabel == null)
            return;
        EnsureUsers();
        CollectMissingTreeIds();
        if (_missingTreeIds.Count == 0 && _caseOnlyTreeIds.Count == 0 && _missingVariantIds.Count == 0)
        {
            _missingLabel.style.display = DisplayStyle.None;
            return;
        }

        var text = new StringBuilder();
        var tooltip = new StringBuilder();
        if (_missingTreeIds.Count > 0)
        {
            text.Append("파일 없는 트리 ").Append(_missingTreeIds.Count).Append("개 — [+ 새 트리]로 만들기");
            AppendTreeIds(tooltip, "몬스터 데이터가 가리키는데 파일이 없는 트리 (그 몬스터는 가만히 있음):", _missingTreeIds, withVariants: true);
        }
        if (_caseOnlyTreeIds.Count > 0)
        {
            text.Append(text.Length > 0 ? " · " : "").Append("대소문자만 다른 트리 Id ").Append(_caseOnlyTreeIds.Count).Append("개 — 몬스터 데이터 확인");
            AppendTreeIds(tooltip, "몬스터 데이터가 대소문자만 다른 트리 Id를 가리킴 (게임은 대소문자까지 같아야 찾음 — 그 몬스터는 가만히 있음. 몬스터 데이터의 BehaviorTreeId를 고칠 것):", _caseOnlyTreeIds, withVariants: false);
        }
        if (_missingVariantIds.Count > 0)
        {
            text.Append(text.Length > 0 ? " · " : "").Append("없는 변형 ").Append(_missingVariantIds.Count).Append("개 — [변형] 탭 · 몬스터 데이터 확인");
            AppendTreeIds(tooltip, "몬스터 데이터가 가리키는데 트리에 없는 변형 (그 몬스터는 가만히 있음 — 그 트리의 [변형] 탭에서 만들거나 몬스터 데이터를 고칠 것):", _missingVariantIds, withVariants: false);
        }
        _missingLabel.text = text.ToString();
        _missingLabel.tooltip = tooltip.ToString();
        _missingLabel.style.display = DisplayStyle.Flex;
    }

    /// <summary>툴팁 묶음 하나 — 머리 + Id마다 쓰는 몬스터 (앞의 MissingShownInTooltip개). withVariants면 그 트리의 변형을 가리키는 몬스터도 (파일 없는 트리 — 변형 Id는 기반 Id로 모은다)</summary>
    private void AppendTreeIds(StringBuilder tooltip, string header, List<string> treeIds, bool withVariants)
    {
        if (tooltip.Length > 0)
            tooltip.Append("\n\n");
        tooltip.Append(header);
        int shown = Math.Min(treeIds.Count, MissingShownInTooltip);
        for (int i = 0; i < shown; i++)
        {
            int variantUsers = 0;
            IReadOnlyList<BehaviorTreeUser> users = withVariants ? _users.GetWithVariants(treeIds[i], out variantUsers) : _users.Get(treeIds[i]);
            tooltip.Append("\n  ").Append(treeIds[i]).Append(" — ").Append(BehaviorTreeUsers.Describe(users, MaxUsersShown));
            if (variantUsers > 0)
                tooltip.Append(" (변형을 가리키는 몬스터 ").Append(variantUsers).Append(')');
        }
        if (treeIds.Count > shown)
            tooltip.Append("\n  … 외 ").Append(treeIds.Count - shown).Append("개");
    }

    private Label BuildMissingLabel()
    {
        _missingLabel = new Label();
        _missingLabel.style.color = EditorStatusColors.Warning;
        _missingLabel.style.fontSize = 10;
        _missingLabel.style.paddingLeft = 4;
        _missingLabel.style.whiteSpace = WhiteSpace.Normal;
        _missingLabel.style.display = DisplayStyle.None;
        return _missingLabel;
    }
    #endregion
}