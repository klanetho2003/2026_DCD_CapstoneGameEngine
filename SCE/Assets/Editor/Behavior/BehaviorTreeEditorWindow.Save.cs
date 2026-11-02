using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 저장 · 새 트리 · 창을 다시 만들 때 넘기는 편집 · 창을 닫을 때 묻기.
///  - 저장 = 트리 JSON ({폴더}/{트리 Id}.json — 게임이 읽음) + 배치 파일 (BehaviorTreeLayoutFile — 편집기만), 파일 일은 BehaviorTreeFiles.
///    에러가 있으면 저장하지 않는다 (게임이 컴파일을 거부할 트리를 만들지 않게 — Interaction Editor와 같은 규칙). 트리 Id가 파일 이름과 다르면 파일 이름을 바꾼다.
///    저장한 뒤 Addressable 등록을 맞춘다 (.Addressables.cs). 목록의 `*` (저장할 것 — NeedsSave, 불러올 때 채운 노드 이름표 포함)인 트리가 [모두 저장]의 대상 —
///    채운 이름표뿐인 트리는 그대로 쓸 수 있을 때만 (에러 · 파일 이름 ≠ 트리 Id · 없어진 파일 · 밖에서 바뀐 파일이면 건너뜀). 닫을 때 묻기와 그 [저장]은 다시 읽으면 잃는 편집이 있는 트리만 (HasLosableEdits).
///    Play 중이면 저장이 끝난 뒤 그 트리를 쓰는 몬스터에 입힌다 — 트리 JSON이 바뀐 저장 · 밖에서 바뀌었거나 없어진 트리 파일을 다시 쓴 저장만 (.Debug.cs — 결과는 상태 줄).
///  - 새 트리: 몬스터 데이터가 가리키는데 파일이 없는 트리 Id가 있으면 그중에서 고르기 (.Users.cs), 없으면 Behavior_New. 뼈대는 BehaviorTreeEditing.NewTree.
///  - 창을 다시 만들 때: 저장하지 않은 편집 (트리 JSON · 배치 가운데 바꾼 쪽)을 창의 직렬화로 넘긴다 (Interaction Editor와 같은 방법 — 표식 JsonUnsavedStamp).
/// </summary>
public sealed partial class BehaviorTreeEditorWindow : ISerializationCallbackReceiver
{
    #region 창을 다시 만들 때 넘기는 편집 (도메인 리로드 · 다른 창 최대화)
    // 창을 다시 만들면 사본을 지우고(OnDisable → JsonDocumentList.DestroyAll) 디스크에서 다시 읽는다(OnEnable → LoadFolder). 저장하지 않은 편집은 창의 직렬화로 넘긴다 — 묻지 않고 사라지지 않게
    //  - 도메인 리로드 (스크립트 컴파일 · Play 진입): 리로드 직전(beforeAssemblyReload)에 담는다 — 창의 직렬화는 OnDisable(사본을 지움) 뒤라 그때는 담을 것이 없다
    //  - 다른 창 최대화 (Play 진입 때의 최대화 포함): Unity가 이 창을 파일에 적고 지웠다가 최대화를 풀 때 다시 만든다 — 직렬화 직전(OnBeforeSerialize), 사본이 살아 있을 때 담는다
    //  - 담은 때 표식 (JsonUnsavedStamp): 레이아웃 저장(끌 때 · 이름 붙인 레이아웃)도 창을 직렬화해 편집이 레이아웃 파일에 들어간다.
    //    다시 만든 창은 같은 편집기 실행 안에서, 담은 뒤 그 창의 횟수가 오르지 않았을 때만 입힌다.
    //    횟수는 창마다 — 편집 · 고른 트리 바꿈 · Undo(편집기 전체) · 새 트리 · 저장 · 다시 읽기 · 버리기가 올린다 (RenewStamp)
    private const string SessionKey = "BehaviorTreeEditor.Session";                  // SessionState — 이 편집기 실행의 번호. 편집기를 끄면 사라지고 도메인 리로드는 넘는다
    private const string GenerationKeyPrefix = "BehaviorTreeEditor.EditGeneration."; // SessionState — + 창 이름표: 그 창의 횟수 (RenewStamp가 올린다)

    [SerializeField] private List<JsonDocumentAsset.UnsavedEdit> _unsavedEdits = new List<JsonDocumentAsset.UnsavedEdit>(); // 창을 직렬화할 때마다 담고, 다시 만든 창이 입히고 비운다
    [SerializeField] private string _unsavedEditsStamp = "";            // 담은 때 표식 "{실행}:{횟수}" — 다시 만든 창의 지금 표식과 같을 때만 입힌다
    [SerializeField] private string _windowId = "";                     // 창 이름표 — 다시 만든 창도 같다 (LoadStamp가 처음 한 번 만듦). 횟수를 창마다 센다
    [NonSerialized] private string _session;                            // 지금 실행 번호 · 이 창의 횟수 — OnEnable에서 SessionState를 읽어 둔다 (직렬화 콜백 안에서는 읽지 않는다)
    [NonSerialized] private int _generation;
    [NonSerialized] private bool _reloading;                            // 리로드 직전에 담았다 — 그 뒤의 직렬화(사본을 지운 뒤)가 지우지 않게. 새 도메인에서는 다시 false
    [NonSerialized] private int _restoredEdits;                         // 되살린 수 — 창을 그릴 때 상태 줄에 알린다 (RestoreNotice)
    [NonSerialized] private int _skippedEdits;                          // 지난 편집기 실행의 것이라 입히지 않은 수 — 상태 줄에 알린다 (내용은 Console)
    private readonly List<string> _unrestoredEdits = new List<string>(); // 되살리지 못한 트리 Id

    /// <summary>리로드 직전 (AssemblyReloadEvents.beforeAssemblyReload — Unity는 이 뒤에 OnDisable, 그 뒤에 창을 직렬화한다): 입력 중인 글자 칸을 확정하고, 저장하지 않은 편집을 담는다.</summary>
    private void OnBeforeAssemblyReload()
    {
        try
        {
            EditorTextInput.CommitFocused(rootVisualElement); // 창이 포커스를 잃을 때(OnLostFocus)와 같게 — 친 글자가 확정 전에 리로드로 버려지지 않게
        }
        catch (Exception e)
        {
            Debug.LogException(e); // 확정하지 못해도 담기는 한다 — 확정 전 글자만 빠진다
        }
        KeepSelectedPath();
        CaptureUnsavedEdits();
        _reloading = true;
    }

    /// <summary>
    /// 창을 직렬화하기 직전 — 다른 창 최대화 · 레이아웃 저장 등. 사본이 살아 있을 때 저장하지 않은 편집을 담는다. 리로드 중이면 담지 않는다 — 리로드 직전에 담은 것을 지킨다 (그때는 사본이 이미 지워졌다).
    /// 이 창의 OnEnable 전 (새 도메인에서 아직 불러오기 전 — 실행 번호를 읽지 않음)에도 담지 않는다 — 넘겨받아 아직 입히지 않은 편집을 빈 목록으로 지우지 않게.
    /// </summary>
    void ISerializationCallbackReceiver.OnBeforeSerialize()
    {
        KeepSelectedPath();
        if (_reloading == false && _session != null)
            CaptureUnsavedEdits();
    }

    /// <summary>창을 다시 만들 때 고를 파일 — 고른 것이 있으면 그 경로 (저장하며 이름이 바뀌었을 수 있다), 없으면 그대로 (사본을 지운 뒤의 직렬화는 지운 트리를 고른 것으로 보지 않는다)</summary>
    private void KeepSelectedPath()
    {
        if (_selected != null)
            _selectedPath = _selected.FilePath;
        else if (_selectedBroken != null)
            _selectedPath = _selectedBroken.FilePath;
    }

    void ISerializationCallbackReceiver.OnAfterDeserialize()
    {
    }

    /// <summary>저장하지 않은 편집이 있는 트리마다 (트리 JSON · 배치 가운데 바꾼 쪽) 담고 담은 때 표식을 적는다. 한 트리의 예외는 Console에 남기고 나머지는 담는다. O(모든 트리의 노드 수)</summary>
    private void CaptureUnsavedEdits()
    {
        JsonDocumentList.CaptureUnsaved(_documents, _unsavedEdits);
        _unsavedEditsStamp = JsonUnsavedStamp.Make(_session, _generation);
    }

    /// <summary>지금 표식을 읽는다 (OnEnable — 되살리기 전에). 이 편집기 실행의 첫 창이면 실행 번호를, 새 창이면 창 이름표를 만든다.</summary>
    private void LoadStamp()
    {
        if (string.IsNullOrEmpty(_windowId))
            _windowId = Guid.NewGuid().ToString("N");
        _session = SessionState.GetString(SessionKey, "");
        if (_session.Length == 0)
        {
            _session = Guid.NewGuid().ToString("N");
            SessionState.SetString(SessionKey, _session);
        }
        _generation = SessionState.GetInt(GenerationKeyPrefix + _windowId, 0);
    }

    /// <summary>이 창의 사본이 바뀔 수 있는 일마다 횟수를 올린다. 그 전에 담아 둔 편집(레이아웃 파일 등)은 이제 입히지 않는다.</summary>
    private void RenewStamp()
    {
        string key = GenerationKeyPrefix + _windowId;
        _generation = SessionState.GetInt(key, _generation) + 1;
        SessionState.SetInt(key, _generation);
    }

    /// <summary>
    /// 다시 만든 뒤 (OnEnable — LoadFolder 다음): 담아 둔 편집을 다시 읽은 사본에 입힌다 (트리 JSON · 배치 가운데 바꾼 쪽 — BehaviorTreeDocumentAsset.TryApplyUnsaved). 저장하기 전까지 디스크는 건드리지 않는다.
    /// 같은 파일의 사본이 없으면 (새 트리 · 그 사이 파일이 없어지거나 읽을 수 없게 됨) 저장 전 새 트리로 (CreateUnsaved — 배치만 바꾼 트리는 만들지 않는다).
    /// 지금 코드로 읽을 수 없는 편집은 되살리지 못하고, 배치만 읽을 수 없으면 트리 JSON만 되살린다 — 상태 줄에 알리고 까닭 · 내용은 Console.
    /// 담은 때 표식이 지금과 다르면 입히지 않는다 (JsonUnsavedStamp.Match) — 지난 편집기 실행의 것은 내용을 Console에 남긴다. Undo 기록은 사본과 함께 지워진다.
    /// </summary>
    private void RestoreUnsavedEdits()
    {
        _restoredEdits = 0;
        _skippedEdits = 0;
        _unrestoredEdits.Clear();
        if (_unsavedEdits.Count == 0)
            return;

        JsonUnsavedStamp.EMatch match = JsonUnsavedStamp.Match(_unsavedEditsStamp, _session, _generation);
        if (match != JsonUnsavedStamp.EMatch.Apply)
        {
            if (match == JsonUnsavedStamp.EMatch.Skip)
            {
                _skippedEdits = _unsavedEdits.Count;
                for (int i = 0; i < _unsavedEdits.Count; i++)
                    Debug.Log($"[{ToolName}] 지난 편집기 실행의 저장하지 않은 편집 — '{_unsavedEdits[i].SetId}'. 입히지 않음. 담아 둔 트리 JSON:\n{CapturedOrNone(_unsavedEdits[i].Json)}\n배치:\n{CapturedOrNone(_unsavedEdits[i].EditorJson)}");
            }
            _unsavedEdits.Clear();
            return;
        }

        BehaviorNodeRegistry registry = BehaviorNodeRegistry.Default;
        _restoredEdits = JsonDocumentList.RestoreUnsaved(_documents, _unsavedEdits,
            (JsonDocumentAsset.UnsavedEdit edit, out string error) => BehaviorTreeDocumentAsset.CreateUnsaved(edit, registry, out error),
            _unrestoredEdits, ToolName);
        SortDocuments(); // 목록 끝에 더한 사본 (저장 전 새 트리)을 제자리로
        UpdateUnsavedFlag();
    }

    /// <summary>담아 둔 글 — 바꾸지 않아 담지 않았으면 그렇게 적는다</summary>
    private static string CapturedOrNone(string text)
    {
        return string.IsNullOrEmpty(text) ? "(바꾸지 않아 담지 않음)" : text;
    }

    /// <summary>상태 줄에 붙이는 되살림 결과 — 다시 만든 창을 그릴 때 (CreateGUI). 없으면 "".</summary>
    private string RestoreNotice()
    {
        string notice = _restoredEdits > 0 ? $" — 저장하지 않은 편집 {_restoredEdits}개를 되살림 (스크립트 컴파일 · Play 진입 · 다른 창 최대화 전의 편집)" : "";
        if (_unrestoredEdits.Count > 0)
            notice += $" — 되살리지 못한 편집: {string.Join(", ", _unrestoredEdits)} (지금 코드로 읽을 수 없거나 트리 파일이 없어짐 — 까닭 · 내용은 Console)";
        if (_skippedEdits > 0)
            notice += $" — 지난 편집기 실행의 편집 {_skippedEdits}개는 입히지 않음 (내용은 Console)";
        return notice;
    }
    #endregion

    #region 새 트리
    private const string BlankTreeId = "Behavior_New";

    /// <summary>툴바의 [+ 새 트리] — 누른 자리에 고르기를 띄우려고 버튼을 넘긴다</summary>
    private ToolbarButton BuildNewTreeButton()
    {
        var button = new ToolbarButton
        {
            text = "+ 새 트리",
            tooltip = "새 트리 만들기 (ROOT → Selector → Idle) — 몬스터 데이터가 가리키는데 파일이 없는 트리 Id가 있으면 그중에서 고를 수 있다. 저장하면 파일 · Addressable 등록이 생긴다",
        };
        button.clicked += () => OnNewTreeClicked(button);
        return button;
    }

    /// <summary>파일 없는 트리 Id가 있으면 고르기 (맨 위 빈 트리), 없으면 바로 빈 트리</summary>
    private void OnNewTreeClicked(VisualElement activator)
    {
        EnsureUsers();
        CollectMissingTreeIds();
        if (_missingTreeIds.Count == 0)
        {
            CreateTree(NextBlankTreeId());
            return;
        }

        var items = new List<EditorSearchPicker.Item>(_missingTreeIds.Count + 1)
        {
            new EditorSearchPicker.Item("빈 트리", $"{NextBlankTreeId()} — 트리 Id는 트리 설정 (ROOT 카드)에서 바꿈", null,
                detail: "아직 어느 몬스터도 쓰지 않는 트리. 몬스터 데이터의 BehaviorTreeId에 이 트리 Id를 넣으면 그 몬스터가 쓴다"),
        };
        for (int i = 0; i < _missingTreeIds.Count; i++)
        {
            string treeId = _missingTreeIds[i];
            IReadOnlyList<BehaviorTreeUser> users = _users.GetWithVariants(treeId, out int variantUsers);
            string variantNote = variantUsers > 0 ? $"\n그 가운데 변형을 가리키는 몬스터 {variantUsers}개는 그 변형도 만들어야 쓴다 ([변형] 탭)" : "";
            items.Add(new EditorSearchPicker.Item(treeId, BehaviorTreeUsers.Describe(users, MaxUsersShown), treeId, $"몬스터 {users.Count}",
                EditorSearchPicker.EBadgeTone.Muted,
                $"몬스터 데이터가 가리키는데 파일이 없는 트리 — 고르면 이 Id로 만든다 (저장하면 그 몬스터들이 쓴다).\n쓰는 몬스터: {BehaviorTreeUsers.Describe(users, int.MaxValue)}{variantNote}"));
        }
        string header = $"새 트리 — 몬스터 데이터가 가리키는데 파일이 없는 트리 {_missingTreeIds.Count}개. 고르면 그 Id로 만든다";
        EditorSearchPicker.Show(GUIUtility.GUIToScreenRect(activator.worldBound), header, items, null,
            item => CreateTree(item.Payload as string ?? NextBlankTreeId())); // 고르는 동안 다른 트리가 생겼을 수 있다 — 빈 트리 이름은 고른 순간에
    }

    /// <summary>빈 트리의 Id — Behavior_New, Behavior_New1, … 가운데 다른 트리의 Id · 파일 이름과 겹치지 않는 첫 이름</summary>
    private string NextBlankTreeId()
    {
        List<string> taken = TakenTreeNames(null);
        string id = BlankTreeId;
        for (int n = 1; BehaviorTreeEditing.TreeIdError(id, taken) != null || File.Exists(JsonSavePlan.PathFor(Folder, id)); n++)
            id = BlankTreeId + n;
        return id;
    }

    /// <summary>
    /// 그 Id로 새 트리를 만든다 (저장 전 — 저장하면 {폴더}/{Id}.json + 배치 파일 + Addressable 등록). 이미 열린 트리면 만들지 않고 고른다.
    /// Id를 쓸 수 없거나 (BehaviorTreeEditing.TreeIdError — 다른 트리와 대소문자만 다름 등) 같은 이름의 파일만 있으면 (창을 연 뒤 생김) 만들지 않는다.
    /// </summary>
    private void CreateTree(string id)
    {
        RenewStamp();
        BehaviorTreeDocumentAsset existing = _documents.Find(document => string.Equals(document.Tree.Id, id, StringComparison.Ordinal));
        if (existing != null)
        {
            Select(existing);
            RefreshTreeList();
            SetStatus($"'{id}' — 이미 있는 트리라 골랐습니다");
            return;
        }
        string error = BehaviorTreeEditing.TreeIdError(id, TakenTreeNames(null));
        if (error == null && File.Exists(JsonSavePlan.PathFor(Folder, id)))
            error = $"같은 이름의 파일이 이미 있습니다: {JsonSavePlan.PathFor(Folder, id)} — [새로고침]으로 읽으세요";
        if (error != null)
        {
            SetStatus($"새 트리 '{id}': {error}", true);
            return;
        }

        BehaviorTreeDocumentAsset created = BehaviorTreeDocumentAsset.CreateNew(BehaviorTreeEditing.NewTree(id), BehaviorNodeRegistry.Default);
        _documents.Add(created);
        SortDocuments();
        UpdateUnsavedFlag();
        Select(created);
        RefreshTreeList();
        ScheduleChecks(all: true); // 다른 트리의 Id 겹침 · 파일 없는 트리 수

        IReadOnlyList<BehaviorTreeUser> users = _users != null ? _users.Get(id) : null;
        SetStatus(users != null && users.Count > 0
            ? $"새 트리 '{id}' — 쓰는 몬스터: {BehaviorTreeUsers.Describe(users, MaxUsersShown)}. 저장하면 {JsonSavePlan.PathFor(Folder, id)}에 만들어집니다"
            : $"새 트리 '{id}' — 저장하면 {JsonSavePlan.PathFor(Folder, id)}에 만들어집니다");
    }

    /// <summary>다른 트리의 Id · 파일 이름 (확장자 없이) · 파싱 실패 파일 이름 — 트리 Id 규칙 (겹침)이 본다. except의 것은 뺀다 (null이면 모두)</summary>
    private List<string> TakenTreeNames(BehaviorTreeDocumentAsset except)
    {
        var names = new List<string>();
        for (int i = 0; i < _documents.Count; i++)
        {
            BehaviorTreeDocumentAsset document = _documents[i];
            if (document == except)
                continue;
            if (string.IsNullOrEmpty(document.Tree.Id) == false)
                names.Add(document.Tree.Id);
            if (string.IsNullOrEmpty(document.FilePath) == false)
                names.Add(Path.GetFileNameWithoutExtension(document.FilePath));
        }
        for (int i = 0; i < _brokenFiles.Count; i++)
            names.Add(Path.GetFileNameWithoutExtension(_brokenFiles[i].FilePath));
        return names;
    }
    #endregion

    #region 저장
    /// <summary>고른 트리 하나 — 바뀐 것이 없어도 다시 쓴다 (배치 파일이 없으면 생기고, Addressable 등록도 맞춘다)</summary>
    private void SaveSelected()
    {
        RenewStamp(); // 저장 — 그 전에 담아 둔 편집은 이제 지난 것
        if (_selected == null)
        {
            SetStatus("고른 트리가 없습니다", true);
            return;
        }

        RunChecksNow(); // 저장 거부 판정 (ErrorCount)을 지금 값으로 — 다른 트리와의 Id 겹침도 이 트리의 검사에 든다
        BehaviorTreeDocumentAsset document = _selected;
        var registration = new RegistrationTally();
        string notice = "";
        bool saved = SaveDocument(document, ref registration, ref notice);
        CommitRegistrations();
        AfterSaving();
        if (saved == false)
            return; // 까닭은 SaveDocument가 상태 줄에 썼다

        string tail = RegistrationStatus(registration, true, out bool isError);
        string play = ApplyPlayReloads(out bool playError);
        SetStatus($"'{LabelOf(document)}' 저장 완료 — {Path.GetFileName(document.FilePath)}{notice}{tail}{play}", isError || notice.Length > 0 || playError);
    }

    /// <summary>[모두 저장] — `*`인 트리 모두 (불러올 때 채운 이름표뿐인 트리는 그대로 쓸 수 있을 때만 — LoadFillSkipReason)</summary>
    private void SaveAll()
    {
        SaveTrees(losableOnly: false);
    }

    /// <summary>
    /// `*`인 트리 모두 — losableOnly면 다시 읽으면 잃는 편집이 있는 트리만 (창을 닫을 때의 [저장] — 묻기가 센 트리). 검사는 처음에 한 번 (다른 트리를 저장해도 저장 거부 판정은 그대로).
    /// 불러올 때 채운 이름표뿐인 트리 (손대지 않은 트리)는 묻거나 막히거나 파일 이름을 바꾸는 저장이면 건너뛰고 상태 줄에 까닭을 붙인다. Addressable 그룹 파일은 끝에 한 번 쓴다
    /// </summary>
    private void SaveTrees(bool losableOnly)
    {
        RenewStamp(); // 저장 — 그 전에 담아 둔 편집은 이제 지난 것 (창을 닫을 때의 [저장]도 여기로)
        RunAllChecksNow(); // 저장 거부 판정 (ErrorCount)을 지금 값으로
        JsonDocumentList.RefreshDirty(_documents); // 거르기 전에 다시 판정 — 누르는 순간 확정된 값의 알림 (값 추적)이 아직 오지 않았을 수 있다

        int saved = 0, failed = 0;
        string notice = "";
        var skipped = new List<string>();
        var registration = new RegistrationTally();
        BehaviorTreeDocumentAsset[] targets = _documents.FindAll(document => losableOnly ? document.HasLosableEdits() : document.NeedsSave()).ToArray();
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i].HasLosableEdits() == false)
            {
                string reason = LoadFillSkipReason(targets[i]);
                if (reason != null)
                {
                    skipped.Add($"'{LabelOf(targets[i])}' — {reason}");
                    continue;
                }
            }
            if (SaveDocument(targets[i], ref registration, ref notice))
                saved++;
            else
                failed++;
        }
        CommitRegistrations();
        AfterSaving();

        string tail = RegistrationStatus(registration, false, out bool registrationError);
        string play = ApplyPlayReloads(out bool playError);
        string skip = SkippedText(skipped);
        if (saved == 0 && failed == 0)
            SetStatus(skipped.Count > 0 ? "저장한 트리 없음" + skip : "저장할 변경이 없습니다", skipped.Count > 0);
        else if (failed > 0)
            SetStatus($"{saved}개 저장, {failed}개 저장 안 함 — 검사 목록 · 목록의 ✕ 확인{notice}{skip}{tail}{play}", true);
        else
            SetStatus($"{saved}개 저장 완료{notice}{skip}{tail}{play}", registrationError || notice.Length > 0 || playError || skipped.Count > 0);
    }

    /// <summary>건너뛴 트리의 상태 줄 글 — 앞 3개만 (나머지는 "외 n개", 모두는 Console). 없으면 ""</summary>
    private static string SkippedText(List<string> skipped)
    {
        if (skipped.Count == 0)
            return "";
        const int Shown = 3;
        if (skipped.Count <= Shown)
            return $" · 이름표만 채운 트리 {skipped.Count}개 건너뜀 ({string.Join(", ", skipped)})";
        Debug.Log($"[{ToolName}] [모두 저장]이 건너뛴 트리 (불러올 때 채운 이름표뿐) {skipped.Count}개:\n{string.Join("\n", skipped)}");
        return $" · 이름표만 채운 트리 {skipped.Count}개 건너뜀 ({string.Join(", ", skipped.GetRange(0, Shown))} 외 {skipped.Count - Shown}개 — Console)";
    }

    /// <summary>
    /// 불러올 때 채운 이름표뿐인 트리 (손대지 않은 트리)를 [모두 저장]이 건너뛸 까닭 — 에러 · 파일 이름 바꿈 · 없어진 파일 · 밖에서 바뀐 트리 파일이나 배치 파일 (덮어쓰기를 묻는 경우).
    /// 손대지 않은 트리에 묻거나 파일을 옮기지 않는다. 그대로 쓸 수 있으면 null
    /// </summary>
    private static string LoadFillSkipReason(BehaviorTreeDocumentAsset document)
    {
        if (document.ErrorCount > 0)
            return $"에러 {document.ErrorCount}건";
        JsonSavePlan plan = JsonSavePlan.Decide(Folder, document.FilePath, document.Tree.Id);
        if (plan.IsNew || plan.Rename || plan.CaseOnlyRename)
            return "파일 이름이 트리 Id와 다름";
        if (File.Exists(document.FilePath) == false)
            return "파일이 없어짐";
        if (BehaviorTreeFiles.OverwriteWarnings(document, plan).Count > 0)
            return "밖에서 바뀐 파일 — [새로고침] 뒤 저장";
        return null;
    }

    /// <summary>
    /// 트리 하나를 저장한다 — 검사는 부르는 쪽이 먼저 한다 (ErrorCount가 그 결과). 막히면 상태 줄에 까닭을 쓰고 false.
    /// 순서: 에러면 거부 → 저장 계획 (JsonSavePlan — 새 트리 · 이름 바꿈 · 대소문자만 바꿈) → 새 이름 자리의 다른 트리 파일이면 거부 → 덮어쓸 파일 묻기 → 쓰기 (BehaviorTreeFiles.Write) → 기준 · 임포트 · Addressable 등록.
    /// 배치 파일만 쓰지 못하면 트리는 저장된 것으로 하고 notice에 까닭 (배치는 `*` 그대로). 등록 결과는 registration에 더한다.
    /// </summary>
    private bool SaveDocument(BehaviorTreeDocumentAsset document, ref RegistrationTally registration, ref string notice)
    {
        string label = LabelOf(document);
        if (document.ErrorCount > 0)
        {
            SetStatus($"'{label}': 에러 {document.ErrorCount}건이 있어 저장하지 않았습니다 — 아래 검사 목록 확인", true);
            return false;
        }
        string treeText = document.SaveJson();
        string layoutText = document.LayoutJson();
        if (treeText.StartsWith(BehaviorTreeDocumentAsset.UnsavableMark, StringComparison.Ordinal)
            || layoutText.StartsWith(BehaviorTreeDocumentAsset.UnsavableMark, StringComparison.Ordinal))
        {
            SetStatus($"'{label}': 저장할 수 없는 값이 있습니다 — 아래 검사 목록 확인", true);
            return false;
        }

        JsonSavePlan plan = JsonSavePlan.Decide(Folder, document.FilePath, document.Tree.Id);
        if (plan.WritesNewFile && File.Exists(plan.TargetPath))
        {
            SetStatus($"같은 이름의 파일이 이미 있습니다: {plan.TargetPath} — 트리 Id를 바꾸세요", true);
            return false;
        }
        // 트리 파일이 불러오거나 저장한 뒤 밖에서 바뀌었거나 (덮어쓰기를 묻는 경우) 없어졌다 — Play 중이면 게임이 다른 판을 읽었거나 읽지 못했을 수 있다
        bool treeFileChanged = plan.IsNew == false && (File.Exists(document.FilePath) == false || EditorJsonFile.DiskChanged(document.FilePath, document.DiskText));
        List<string> warnings = BehaviorTreeFiles.OverwriteWarnings(document, plan);
        if (warnings.Count > 0 && EditorUtility.DisplayDialog("밖에서 바뀐 파일이 있습니다",
                $"'{label}' 트리를 저장하면 덮어씁니다:\n\n{string.Join("\n", warnings)}\n\n덮어쓰면 그 내용은 사라집니다.", "덮어쓰기", "취소") == false)
        {
            SetStatus("저장 취소 — [새로고침]으로 디스크 내용을 다시 불러올 수 있습니다", true);
            return false;
        }

        string error = BehaviorTreeFiles.Write(document, plan, treeText, layoutText, out string treePath, out string layoutPath, out string layoutError, out string moveNotice);
        if (error != null)
        {
            SetStatus($"'{label}': {error}", true);
            return false;
        }

        bool treeChanged = treeText != document.Baseline || treeFileChanged; // 게임이 쓰는 판과 다를 수 있다 — 배치만 바꾼 저장 · 그대로 다시 쓴 저장은 아니다
        document.MarkSaved(treePath, treeText);
        AssetDatabase.ImportAsset(treePath); // TextAsset 갱신. JSON 임포트는 스크립트 컴파일(도메인 리로드)을 일으키지 않는다
        if (layoutError == null)
        {
            document.MarkLayoutSaved(layoutText);
            AssetDatabase.ImportAsset(layoutPath);
        }
        RegisterSaved(document, ref registration); // 주소 = 트리 Id, PreLoad label (새 파일은 임포트 뒤에야 GUID가 있다)
        if (treeChanged)
            QueuePlayReload(document.Tree.Id, treeText); // Play 중이면 저장이 끝난 뒤 그 트리를 쓰는 몬스터에 입힌다 (.Debug.cs)
        notice += moveNotice + (layoutError != null ? $" · '{label}' 배치 파일 저장 실패 ({layoutError}) — 배치는 저장 안 됨" : "");
        return true;
    }

    /// <summary>저장 한 번 (하나 · 모두)이 끝난 뒤 — 고른 파일 경로 · 목록 (이름순 · 파일 이름) · 머리 · 닫을 때 묻기 · 패널 (파일 이름 · 쓰는 몬스터)</summary>
    private void AfterSaving()
    {
        KeepSelectedPath();
        SortDocuments();
        RefreshTreeList();
        UpdateTreeTitle();
        UpdateUnsavedFlag();
        SchedulePanel();
    }

    /// <summary>다시 읽으면 잃는 편집이 있는 트리가 있으면 창을 닫을 때 Unity가 묻는다 (불러올 때 채운 이름표뿐인 트리는 세지 않는다 — 다시 읽으면 또 채운다)</summary>
    private void UpdateUnsavedFlag()
    {
        bool unsaved = false;
        for (int i = 0; i < _documents.Count && unsaved == false; i++)
            unsaved = _documents[i] != null && _documents[i].HasLosableEdits();
        hasUnsavedChanges = unsaved;
        saveChangesMessage = "저장하지 않은 Behavior Tree 변경 (트리 · 배치)이 있습니다.";
    }

    /// <summary>
    /// 창을 닫을 때 Unity의 "저장할까요?" 대화 상자에서 [저장]을 누르면 호출된다 — 묻기가 센 트리 (다시 읽으면 잃는 편집)만 저장한다.
    /// 에러 때문에 저장하지 못한 트리가 남으면 창을 닫지 않는다
    /// </summary>
    public override void SaveChanges()
    {
        SaveTrees(losableOnly: true);
        if (hasUnsavedChanges == false)
            base.SaveChanges();
    }

    /// <summary>[버리기] — 담아 둔 편집도 버리고 디스크에서 다시 읽는다 (닫지 않고 남는 경우에도 화면이 디스크와 같게)</summary>
    public override void DiscardChanges()
    {
        _unsavedEdits.Clear(); // 버린 편집이 창을 다시 만들 때 되살아나지 않게 — 직렬화 때 담은 것이 남아 있을 수 있다
        RenewStamp();          // 레이아웃 파일에 담긴 것도
        string keepPath = _selectedPath;
        LoadFolder();
        SelectByPath(keepPath);
        RunAllChecksNow();
        hasUnsavedChanges = false;
        base.DiscardChanges();
    }
    #endregion
}