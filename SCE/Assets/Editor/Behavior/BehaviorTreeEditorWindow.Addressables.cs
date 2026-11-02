using System;
using System.Collections.Generic;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine.UIElements;

/// <summary>
/// Addressable 등록 — 게임은 트리를 주소 = 트리 Id, PreLoad label로 찾는다 (BehaviorManager.GetDefinition → ResourceManager.Load — 미리 읽은 것만).
///  - 저장할 때 맞춘다: 새 트리는 등록, 트리 Id를 바꿨으면 주소도, label이 없으면 label도. 이미 맞으면 손대지 않는다
///  - 새로 등록할 그룹: 다른 트리가 가장 많이 든 그룹, 없으면 기본 그룹 — 저장 · 맞추기 한 번에 한 번 고른다
///  - 검사 줄 (경고 — 트리 · id 칸): 저장된 그대로인 트리의 어긋남 + 검사 머리줄 [Addressable 맞추기 (n)]. 고친 트리 · 새 트리는 저장할 때 맞추므로 주소 겹침만 본다
///  - 손대지 않는 것: 주소 겹침 · 폴더째 등록한 곳 안 · Addressable 설정 없음 → 경고만. 등급은 경고 — 저장을 막지 않는다
///  - 배치 파일은 등록하지 않는다 (Editor 폴더 — 게임이 읽지 않음)
/// 판정은 JsonAddressRule (공용 — 문장의 낱말은 AddressWords), Addressables 읽기 · 쓰기는 JsonAddressBook (Interaction Editor와 같은 공용). 이 파일은 창과 이어 주기만 한다.
/// 색인은 Addressable이 바뀌었을 때만 다시 만든다 — 수정 이벤트 횟수 (EditorAddressables.Version) · 폴더 다시 읽기 · 여기서 맞춘 뒤.
/// </summary>
public sealed partial class BehaviorTreeEditorWindow
{
    /// <summary>등록 판정 문장의 낱말 — 트리</summary>
    internal static readonly JsonAddressWords AddressWords = new JsonAddressWords("트리 Id", "트리", "이 트리나", "이 트리를", "트리를", "이 트리를 쓰는 몬스터는 가만히 있음");

    private JsonAddressBook _addressBook;      // 처음 쓸 때 만든다
    private int _addressBookVersion;           // 만든 때의 수정 이벤트 횟수 — 지금과 다르면 낡은 색인
    private readonly List<string> _addressProblems = new List<string>();
    private readonly List<string> _treeFilePaths = new List<string>(); // 그룹 고르기용 — 저장된 트리 파일
    private readonly HashSet<AddressableAssetGroup> _touchedGroups = new HashSet<AddressableAssetGroup>();
    private AddressableAssetGroup _batchGroup; // 이번 저장 · 맞추기에서 새로 등록할 그룹 (없을 수도 있다)
    private bool _batchGroupPicked;            // _batchGroup을 이번에 이미 골랐는가
    private int _fixableRegistrations;         // 마지막 검사에서 [Addressable 맞추기]가 고칠 트리 수
    private Button _fixRegistrationsButton;

    /// <summary>저장 한 번 (하나 · 모두) 또는 [Addressable 맞추기] 한 번의 등록 결과 — 상태 줄에 한 번에 쓴다</summary>
    private struct RegistrationTally
    {
        public int Changed;
        public int Blocked;
        public string FirstChangeTreeId;
        public string FirstChange;  // "등록 — 그룹 Behavior, 주소 Behavior_Boss, label PreLoad" 등
        public string FirstBlockedTreeId;
        public string FirstBlocked; // 막힌 까닭 (판정 문장)
    }

    #region 색인
    /// <summary>등록 색인 — 처음 쓸 때, 그리고 Addressable이 바뀐 뒤 (수정 이벤트) 다시 만든다. 다시 만들 때 O(등록된 에셋 수), 아니면 O(1)</summary>
    private JsonAddressBook AddressBook()
    {
        if (_addressBook == null || _addressBookVersion != EditorAddressables.Version)
        {
            _addressBookVersion = EditorAddressables.Version;
            _addressBook = JsonAddressBook.Build();
        }
        return _addressBook;
    }

    /// <summary>폴더를 다시 읽을 때 (LoadFolder) — 수정 이벤트가 오지 않는 변경 (버전 관리로 그룹 파일이 바뀜)까지 다시 보게 한다</summary>
    private void ForgetAddressBook()
    {
        _addressBook = null;
    }

    /// <summary>창이 포커스를 받을 때 — 다른 곳 (Addressables Groups 창 등)에서 등록이 바뀌었으면 모든 트리를 다시 검사한다</summary>
    private void RescanAddressables()
    {
        if (_addressBook != null && _addressBookVersion != EditorAddressables.Version)
            ScheduleChecks(all: true);
    }
    #endregion

    #region 검사 (CheckInto가 부른다)
    /// <summary>
    /// 트리 하나의 등록 경고 (판정 = JsonAddressRule) — 트리 자리 · id 칸. O(1)
    /// 트리 Id를 쓸 수 없으면 (빈 Id · 앞뒤 공백 · 파일 이름 불가 · 다른 트리와 겹침) 보지 않는다 — 다른 에러가 이미 알리고, 그런 Id로 맞추면 몬스터가 찾지 못하는 주소나 겹침을 만든다.
    /// Addressable 설정이 없으면 트리마다 알리지 않고 검사 머리줄에 한 번 (AddressNotice).
    /// </summary>
    private void AddRegistrationItems(BehaviorTreeDocumentAsset document, List<BehaviorTreeCheckItem> items)
    {
        string id = document.Tree.Id;
        if (IdUsable(document) == false)
            return;
        JsonAddressBook book = AddressBook();
        if (book.HasSettings == false)
            return;

        _addressProblems.Clear();
        EJsonAddressState state = JsonAddressRule.Judge(book.Read(document.FilePath, id), id, ResourceManager.PreLoadLabel, PendingSave(document), AddressWords, _addressProblems);
        string remedy = state == EJsonAddressState.Fixable ? " — 저장하거나 [Addressable 맞추기]로 맞춤" : "";
        for (int i = 0; i < _addressProblems.Count; i++)
            items.Add(new BehaviorTreeCheckItem(EBehaviorMessageSeverity.Warning, EBehaviorCheckPlace.Tree, null, -1, "id", _addressProblems[i] + remedy));
        _addressProblems.Clear();
    }

    /// <summary>검사 머리줄 알림 — Addressable 설정이 없으면 등록 확인을 건너뛴다는 것</summary>
    private string AddressNotice()
    {
        return _addressBook != null && _addressBook.HasSettings == false ? " · Addressable 설정 없음 — 등록 확인 건너뜀" : "";
    }

    /// <summary>저장할 때 등록을 맞출 트리 — 새 트리 · 트리 JSON을 고친 트리 (불러올 때 채운 이름표뿐이면 아니다 — 저장된 그대로처럼 본다)</summary>
    private static bool PendingSave(BehaviorTreeDocumentAsset document)
    {
        return string.IsNullOrEmpty(document.FilePath) || (document.IsDirty && document.HasOnlyLoadFill() == false);
    }

    /// <summary>등록을 볼 수 있는 트리 Id — 트리 Id 규칙 (빈 Id · 앞뒤 공백 · 파일 이름 불가 — BehaviorTreeEditing.TreeIdError)을 지키고, 다른 트리와 겹치지 않는다 (대소문자 무시 — 파일 이름). O(트리 수)</summary>
    private bool IdUsable(BehaviorTreeDocumentAsset document)
    {
        string id = document.Tree.Id;
        if (BehaviorTreeEditing.TreeIdError(id, Array.Empty<string>()) != null)
            return false;
        for (int i = 0; i < _documents.Count; i++)
        {
            if (_documents[i] != document && string.Equals(_documents[i].Tree.Id, id, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    /// <summary>[Addressable 맞추기]가 고칠 트리 수를 센다 — 저장된 그대로인 트리 가운데 판정이 Fixable인 것. 버튼을 맞춘다. O(트리 수²) — 트리 수가 작다</summary>
    private void CountFixableRegistrations()
    {
        _fixableRegistrations = 0;
        JsonAddressBook book = _documents.Count > 0 ? AddressBook() : null;
        if (book != null && book.HasSettings)
        {
            for (int i = 0; i < _documents.Count; i++)
            {
                BehaviorTreeDocumentAsset document = _documents[i];
                if (PendingSave(document) || IdUsable(document) == false)
                    continue;
                _addressProblems.Clear();
                if (JsonAddressRule.Judge(book.Read(document.FilePath, document.Tree.Id), document.Tree.Id, ResourceManager.PreLoadLabel, false, AddressWords, _addressProblems) == EJsonAddressState.Fixable)
                    _fixableRegistrations++;
            }
            _addressProblems.Clear();
        }
        if (_fixRegistrationsButton == null)
            return;
        _fixRegistrationsButton.text = $"Addressable 맞추기 ({_fixableRegistrations})";
        _fixRegistrationsButton.style.display = _fixableRegistrations > 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    /// <summary>검사 머리줄의 [Addressable 맞추기 (n)] — 맞출 트리가 있을 때만 보인다 (CountFixableRegistrations)</summary>
    private Button BuildFixRegistrationsButton()
    {
        _fixRegistrationsButton = new Button(FixAllRegistrations)
        {
            text = "Addressable 맞추기",
            tooltip = $"저장된 그대로인 트리의 Addressable 등록을 게임이 찾는 모양으로 맞춥니다 — 주소 = 트리 Id, label {ResourceManager.PreLoadLabel}. " +
                      "등록 안 된 트리는 다른 트리가 가장 많이 든 그룹에 넣습니다. 주소 겹침 · 폴더째 등록은 건드리지 않습니다. 고친 트리는 저장할 때 맞춥니다",
        };
        _fixRegistrationsButton.style.display = DisplayStyle.None;
        return _fixRegistrationsButton;
    }
    #endregion

    #region 저장 때 맞추기 (Save.cs가 부른다)
    /// <summary>
    /// 방금 저장한 트리의 등록을 맞춘다 — 판정이 Fixable일 때만. 겹침 · 폴더째 등록 · 설정 없음은 손대지 않고 tally에 남긴다.
    /// 그룹 파일 쓰기는 CommitRegistrations가 한 번에 한다. O(1), 그룹을 처음 고를 때만 O(트리 수)
    /// </summary>
    private void RegisterSaved(BehaviorTreeDocumentAsset document, ref RegistrationTally tally)
    {
        string id = document.Tree.Id;
        JsonAddressBook book = AddressBook();
        if (book.HasSettings == false)
        {
            Block(ref tally, id, "Addressable 설정 없음 — 등록하지 못함");
            return;
        }

        JsonAddressFacts facts = book.Read(document.FilePath, id);
        _addressProblems.Clear();
        EJsonAddressState state = JsonAddressRule.Judge(facts, id, ResourceManager.PreLoadLabel, false, AddressWords, _addressProblems);
        string firstProblem = _addressProblems.Count > 0 ? _addressProblems[0] : "";
        _addressProblems.Clear();

        if (state == EJsonAddressState.Ok)
            return;
        if (state == EJsonAddressState.Blocked)
        {
            Block(ref tally, id, firstProblem);
            return;
        }

        AddressableAssetGroup group = facts.Registered ? null : BatchGroup(book); // 그룹은 새로 등록할 때만 쓴다
        string change = book.Fix(document.FilePath, id, group, _touchedGroups, out string error);
        if (change == null)
        {
            Block(ref tally, id, error);
            return;
        }
        if (change.Length == 0)
            return; // 이미 맞았다 (색인이 낡았던 경우)

        tally.Changed++;
        if (tally.FirstChange == null)
        {
            tally.FirstChangeTreeId = id;
            tally.FirstChange = change;
        }
    }

    private static void Block(ref RegistrationTally tally, string id, string reason)
    {
        tally.Blocked++;
        if (tally.FirstBlocked == null)
        {
            tally.FirstBlockedTreeId = id;
            tally.FirstBlocked = reason;
        }
    }

    /// <summary>새로 등록할 그룹 — 저장 · 맞추기 한 번에 한 번만 고른다 (다른 트리 파일이 가장 많이 든 그룹). O(트리 수)</summary>
    private AddressableAssetGroup BatchGroup(JsonAddressBook book)
    {
        if (_batchGroupPicked)
            return _batchGroup;

        _treeFilePaths.Clear();
        for (int i = 0; i < _documents.Count; i++)
        {
            if (string.IsNullOrEmpty(_documents[i].FilePath) == false)
                _treeFilePaths.Add(_documents[i].FilePath);
        }
        _batchGroup = book.PickGroup(_treeFilePaths);
        _batchGroupPicked = true;
        _treeFilePaths.Clear();
        return _batchGroup;
    }

    /// <summary>
    /// 저장 · 맞추기 한 번이 끝나면: 바뀐 그룹 파일을 쓰고 (수정 이벤트 한 번), 색인을 다시 만들게 하고, 모든 트리의 검사를 다시 예약한다
    /// (저장한 트리는 "고친 트리" (주소 겹침만 봄)에서 "저장된 트리" (모두 봄)가 되어 경고 기준이 바뀐다 — Id 겹침 · 파일 이름도).
    /// </summary>
    private void CommitRegistrations()
    {
        _batchGroup = null;
        _batchGroupPicked = false;
        if (_touchedGroups.Count > 0)
        {
            JsonAddressBook.Commit(_touchedGroups);
            _touchedGroups.Clear();
            _addressBook = null;
        }
        ScheduleChecks(all: true);
    }

    /// <summary>상태 줄 꼬리. single = 트리 하나를 저장했을 때 (트리 Id를 다시 적지 않는다). 막힌 것이 있으면 isError</summary>
    private static string RegistrationStatus(in RegistrationTally tally, bool single, out bool isError)
    {
        isError = tally.Blocked > 0;
        string text = "";
        if (tally.Changed == 1)
            text += single ? $" · Addressable {tally.FirstChange}" : $" · Addressable {tally.FirstChangeTreeId}: {tally.FirstChange}";
        else if (tally.Changed > 1)
            text += $" · Addressable {tally.Changed}개 맞춤";

        if (tally.Blocked == 1)
            text += single ? $" · Addressable 등록 안 함 — {tally.FirstBlocked}" : $" · Addressable 등록 안 함 — {tally.FirstBlockedTreeId}: {tally.FirstBlocked}";
        else if (tally.Blocked > 1)
            text += $" · Addressable 등록 안 함 {tally.Blocked}개 — 검사 목록 확인";
        return text;
    }
    #endregion

    #region [Addressable 맞추기]
    /// <summary>저장된 그대로인 트리 가운데 판정이 Fixable인 것을 모두 맞춘다 — 검사 줄과 같은 기준 (고친 트리는 저장할 때 맞추므로 건드리지 않는다). O(트리 수) + 바뀐 그룹 파일 쓰기</summary>
    private void FixAllRegistrations()
    {
        RunAllChecksNow(); // 트리 Id 겹침 · 미저장 판정을 지금 값으로
        JsonAddressBook book = AddressBook();
        if (book.HasSettings == false)
        {
            SetStatus("Addressable 설정이 없어 맞출 수 없습니다", true);
            return;
        }

        var tally = new RegistrationTally();
        for (int i = 0; i < _documents.Count; i++)
        {
            BehaviorTreeDocumentAsset document = _documents[i];
            if (PendingSave(document) || IdUsable(document) == false)
                continue;
            RegisterSaved(document, ref tally); // 맞는 트리는 그대로 두고, 막힌 트리는 세기만 한다
        }
        CommitRegistrations();

        if (tally.Changed == 0 && tally.Blocked == 0)
            SetStatus("맞출 트리가 없습니다");
        else
        {
            string blocked = tally.Blocked > 0 ? $" · 손으로 풀 것 {tally.Blocked}개 — 검사 목록 확인" : "";
            SetStatus($"Addressable {tally.Changed}개 맞춤 — 주소 = 트리 Id, label {ResourceManager.PreLoadLabel}{blocked}", tally.Blocked > 0);
        }
    }
    #endregion
}