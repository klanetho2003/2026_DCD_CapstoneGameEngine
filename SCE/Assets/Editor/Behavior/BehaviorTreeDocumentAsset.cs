using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Behavior Tree Editor의 편집 사본 — 트리 JSON 파일 하나와 그 배치 파일 (BehaviorTreeLayoutFile). 트리 데이터 (BehaviorTreeAsset)를 그대로 담아 Undo · SerializeReference 편집을 Unity에 맡긴다.
/// 트리 밖의 편집 상태 (배치)도 같이 직렬화한다 — Undo가 트리와 함께 맞춘다:
///  - LooseNodes: 트리에 이어지지 않은 부분 트리의 맨 위 노드 (Unreal처럼 그래프에만 있다)
///  - Placements · RootCardPosition: 카드 자리 (노드 이름표 NodeId → 왼쪽 위, 그래프 좌표)
///  - Memos: 노드 메모 (노드 이름표 → 글)
/// 저장은 둘로 — 트리 JSON (SaveJson — 게임이 읽음)과 배치 파일 (LayoutJson — 편집기만). 미저장 판정도 둘: 트리 JSON (IsDirty — 카드를 옮겨 자식 차례가 바뀌면 트리 JSON도 바뀐다) · 배치 (IsLayoutDirty).
/// 불러올 때 비었거나 겹치는 노드 이름표를 채운다 (BehaviorNodeIds.FillMissing) — 기준은 파일 그대로, 채운 값은 불러올 때 채운 편집 (HasOnlyLoadFill).
/// 채운 이름표는 목록 `*` · [모두 저장]의 대상이다 (NeedsSave — 디버거가 Play 중 노드를 이름표로 찾는다), 닫을 때 · [새로고침] 묻기의 대상은 아니다 (HasLosableEdits — 다시 읽으면 또 채운다).
/// 창을 다시 만들 때는 트리 JSON과 배치 가운데 바꾼 쪽을 담아 넘긴다 (CaptureUnsaved · TryApplyUnsaved — 바꾸지 않은 쪽은 다시 읽은 파일 그대로).
/// </summary>
public sealed class BehaviorTreeDocumentAsset : JsonDocumentAsset
{
    /// <summary>저장할 수 없는 값 (유한하지 않은 수 · 등록되지 않은 노드)이 있을 때 SaveJson · LayoutJson이 반환하는 글의 머리 — 뒤에 까닭</summary>
    public const string UnsavableMark = "(저장할 수 없음) ";

    public BehaviorTreeAsset Tree;

    /// <summary>트리에 이어지지 않은 부분 트리의 맨 위 노드 — 이은 차례 (끊거나 더한 노드가 끝에)</summary>
    [SerializeReference] public List<BehaviorMainNode> LooseNodes = new List<BehaviorMainNode>();

    /// <summary>Main 노드 카드의 자리 — 노드 이름표마다 하나</summary>
    public List<BehaviorCardPlacement> Placements = new List<BehaviorCardPlacement>();

    /// <summary>ROOT 카드의 왼쪽 위</summary>
    public Vector2 RootCardPosition;

    /// <summary>노드 메모 (편집기만) — 노드 이름표마다 하나 (Main · Decorator · Service)</summary>
    public List<BehaviorNodeMemo> Memos = new List<BehaviorNodeMemo>();

    /// <summary>노드 키 ↔ 형식 (저장 · 노드 만들기 · 이름표 머리). 만들 때 정한다</summary>
    [NonSerialized] public BehaviorNodeRegistry Registry;

    /// <summary>배치의 기준 — 불러오거나 저장한 때 (또는 처음 보일 때의 정리 배치 뒤)의 배치 글 (LayoutJson). 새 트리는 null</summary>
    [NonSerialized] public string LayoutBaseline;

    /// <summary>배치 파일을 불러오거나 저장한 때의 파일 내용 — 밖에서 바뀌었는지 비교용. 파일이 없었으면 null</summary>
    [NonSerialized] public string LayoutDiskText;

    /// <summary>배치 파일을 읽지 못한 까닭 — 그 파일 없이 열었다 (저장할 때 덮어쓸지 묻는다). 읽었거나 파일이 없으면 null</summary>
    [NonSerialized] public string LayoutError;

    /// <summary>배치가 기준과 다른가 — RefreshDirty가 맞추는 캐시</summary>
    [NonSerialized] public bool IsLayoutDirty;

    [NonSerialized] private bool _savedNodeIdsChecked;    // 아래를 한 번이라도 읽었다
    [NonSerialized] private string _savedNodeIdsOf;       // 아래를 읽은 기준 (Baseline) — 기준이 바뀌면 다시 읽는다
    [NonSerialized] private bool _savedNodeIdsRead;       // 그 기준을 읽었다 (트리로 읽지 못했으면 false)
    [NonSerialized] private bool _savedFileMissesNodeIds; // 기준에 이름표가 비었거나 겹치는 노드가 있다
    [NonSerialized] private readonly HashSet<string> _savedNodeIds = new HashSet<string>(StringComparer.Ordinal); // 기준의 노드 이름표 (빈 이름표는 빼고)

    /// <summary>
    /// 디스크에서 불러온 트리 — 기준은 파일 그대로. 비었거나 겹치는 노드 이름표는 채운다 (불러올 때 채운 편집).
    /// 배치는 빈 채 (배치 파일 없음)가 기준 — 배치 파일이 있으면 부르는 쪽이 ApplyLoadedLayout · MarkLayoutUnreadable을 부른다. O(노드 수)
    /// </summary>
    public static BehaviorTreeDocumentAsset Create(BehaviorTreeAsset tree, BehaviorNodeRegistry registry, string filePath, string diskText, bool hasBom)
    {
        BehaviorTreeDocumentAsset asset = CreateNormalized(tree, registry);
        asset.FilePath = filePath;
        asset.DiskText = diskText;
        asset.HasBom = hasBom;
        asset.Baseline = asset.SaveJson();
        asset.MarkLayoutBaseline();
        if (BehaviorNodeIds.FillMissing(asset) > 0)
            asset.MarkLoadFill();
        return asset;
    }

    /// <summary>툴에서 새로 만든 트리 — 기준이 없으므로 처음부터 미저장 (트리 JSON · 배치 모두, 파일 없음). 비었거나 겹치는 이름표는 채운다. O(노드 수)</summary>
    public static BehaviorTreeDocumentAsset CreateNew(BehaviorTreeAsset tree, BehaviorNodeRegistry registry)
    {
        BehaviorTreeDocumentAsset asset = CreateNormalized(tree, registry);
        BehaviorNodeIds.FillMissing(asset);
        asset.IsDirty = true;
        asset.IsLayoutDirty = true;
        return asset;
    }

    /// <summary>
    /// 창을 다시 만든 뒤: 같은 파일의 사본이 없는 편집 (새 트리 · 그 사이 파일이 없어지거나 읽을 수 없게 됨) — 저장 전 새 트리로 (담아 둔 배치도 입힌다).
    /// 트리 JSON을 담지 않은 편집 (배치만 바꾼 트리)이면 만들지 않는다 — 없어진 트리를 되살리지 않는다. 트리 JSON을 지금 코드로 읽을 수 없거나
    /// 담을 때 저장할 수 없는 값이 있었으면 null과 까닭. 배치를 (모두) 입히지 못하면 그 까닭을 준다 (일부만 되살림).
    /// </summary>
    public static BehaviorTreeDocumentAsset CreateUnsaved(UnsavedEdit edit, BehaviorNodeRegistry registry, out string error)
    {
        if (string.IsNullOrEmpty(edit.Json))
        {
            error = "트리 파일이 없어짐 — 배치만 바꾼 편집이라 트리를 만들지 않음";
            return null;
        }
        error = UnsavableTreeReason(edit.Json);
        if (error != null)
            return null;
        BehaviorTreeAsset tree = BehaviorTreeLoader.ParseAsset(edit.Json, registry, out error);
        if (tree == null)
            return null;

        BehaviorTreeDocumentAsset asset = CreateNew(tree, registry);
        asset.HasBom = edit.HasBom; // 새로 쓸 파일도 원본의 BOM을 따른다
        if (string.IsNullOrEmpty(edit.EditorJson) == false)
        {
            BehaviorTreeLayoutData layout = ReadCapturedLayout(edit.EditorJson, registry, out error);
            if (layout != null)
                asset.ApplyLayout(layout);
        }
        return asset;
    }

    /// <summary>저장용 JSON — 게임이 읽는 형식 그대로 (BehaviorTreeLoader.Serialize). 저장할 수 없는 값이 있으면 UnsavableMark + 까닭 (기준과 달라 미저장으로 보인다). O(노드 수)</summary>
    public override string SaveJson()
    {
        try
        {
            return BehaviorTreeLoader.Serialize(Tree, Registry);
        }
        catch (JsonException exception)
        {
            return UnsavableMark + exception.Message;
        }
    }

    protected override string DocumentId => Tree?.Id;

    /// <summary>
    /// 다시 읽으면 잃는 편집이 있는가 — 트리 JSON이 기준과 다르거나 (불러올 때 채운 이름표뿐이면 아니다 — 다시 읽으면 또 채운다), 배치가 기준과 다르다.
    /// 미저장 판정 (IsDirty · IsLayoutDirty)은 부르는 쪽이 먼저 맞춘다 (RefreshDirty). 닫을 때 · [새로고침] 묻기가 이것을 센다 (목록 `*` · [모두 저장]은 NeedsSave). O(노드 수)
    /// </summary>
    public bool HasLosableEdits()
    {
        return (IsDirty && HasOnlyLoadFill() == false) || IsLayoutDirty;
    }

    /// <summary>
    /// 저장할 것이 있는가 — 트리 JSON이나 배치가 파일과 다르다: 다시 읽으면 잃는 편집 (HasLosableEdits)과 불러올 때 채운 노드 이름표 (아직 파일에 없다 — 디버거가 Play 중 노드를 이름표로 찾는다).
    /// 목록의 `*` · [모두 저장]이 이것을 센다. 미저장 판정은 부르는 쪽이 먼저 맞춘다 (RefreshDirty). O(1)
    /// </summary>
    public bool NeedsSave()
    {
        return IsDirty || IsLayoutDirty;
    }

    /// <summary>
    /// 파일 (기준 — 불러오거나 저장한 때)에 이 노드 이름표가 있는가 — 불러올 때 채운 이름표 · 저장하지 않은 이름표 · 저장 전 새 트리면 false. 기준을 트리로 읽지 못하면 true (알리지 않는다).
    /// 기준이 바뀔 때만 다시 읽는다 — O(1), 다시 읽을 때 O(노드 수)
    /// </summary>
    public bool SavedFileHasNodeId(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId))
            return false;
        return ReadSavedNodeIds() == false || _savedNodeIds.Contains(nodeId);
    }

    /// <summary>파일 (기준)에 이름표가 비었거나 겹치는 노드가 있는가 — 불러올 때 이름표를 채운 트리. 저장 전 새 트리 · 기준을 트리로 읽지 못하면 false. SavedFileHasNodeId와 같은 읽기</summary>
    public bool SavedFileMissesNodeIds()
    {
        return ReadSavedNodeIds() && _savedFileMissesNodeIds;
    }

    /// <summary>기준의 노드 이름표 (트리 JSON — 루트 부분 트리)를 읽어 둔다. 기준이 그대로면 읽어 둔 것. 트리로 읽었으면 (파일이 없는 새 트리 · 루트가 없는 파일 포함) true</summary>
    private bool ReadSavedNodeIds()
    {
        string baseline = Baseline;
        if (_savedNodeIdsChecked && ReferenceEquals(baseline, _savedNodeIdsOf))
            return _savedNodeIdsRead;
        _savedNodeIdsChecked = true;
        _savedNodeIdsOf = baseline;
        _savedNodeIds.Clear();
        _savedFileMissesNodeIds = false;
        _savedNodeIdsRead = true;
        if (baseline == null)
            return true; // 저장 전 새 트리 — 파일에 이름표가 없다
        BehaviorTreeAsset tree = BehaviorTreeLoader.ParseAsset(baseline, Registry, out _);
        if (tree == null)
        {
            _savedNodeIdsRead = false;
            return false;
        }
        if (tree.Root == null)
            return true; // 루트가 없는 파일 — 이름표가 없다
        var nodes = new List<BehaviorNode>();
        BehaviorTreeEditing.CollectSubtreeNodes(tree.Root, nodes);
        for (int i = 0; i < nodes.Count; i++)
        {
            string id = nodes[i].NodeId;
            if (string.IsNullOrEmpty(id) || _savedNodeIds.Add(id) == false)
                _savedFileMissesNodeIds = true;
        }
        return true;
    }

    /// <summary>
    /// 담아 둔 JSON으로 트리를 바꾼다 — 불러올 때와 같은 표현 (CreateNormalized). 이어지지 않은 노드는 그대로 둔다 (트리 JSON 밖 — 배치를 함께 담았으면 TryApplyUnsaved가 이어서 바꾼다).
    /// 카드 자리 · 메모는 남은 노드의 것만 둔다 (이름표가 같으면 그 자리 · 메모 — 없어진 노드의 것이 새 이름표를 받은 노드로 가지 않게). 읽을 수 없으면 그대로.
    /// </summary>
    protected override bool ApplyJson(string json, out string error)
    {
        BehaviorTreeAsset tree = BehaviorTreeLoader.ParseAsset(json, Registry, out error);
        if (tree == null)
            return false;

        BehaviorTreeDocumentAsset normalized = CreateNormalized(tree, Registry);
        Tree = normalized.Tree; // 불러올 때와 같은 표현 — Unity 직렬화를 한 번 거친 값
        DestroyImmediate(normalized);
        KeepEditorDataOfExistingNodes();
        BehaviorNodeIds.FillMissing(this);
        return true;
    }

    #region 배치 (배치 파일 · 미저장 판정 · 창을 다시 만들 때)
    /// <summary>지금 배치의 글 (BehaviorTreeLayoutFile.Write — 배치 파일에 쓰는 글). 저장할 수 없는 값 (이어지지 않은 노드)이 있으면 UnsavableMark + 까닭. O(노드 수)</summary>
    public string LayoutJson()
    {
        try
        {
            return BehaviorTreeLayoutFile.Write(this);
        }
        catch (JsonException exception)
        {
            return UnsavableMark + exception.Message;
        }
    }

    /// <summary>미저장 판정 — 트리 JSON (바탕: IsDirty)과 배치 (IsLayoutDirty). 어느 하나라도 판정이 바뀌었으면 true. O(노드 수)</summary>
    public override bool RefreshDirty()
    {
        bool changed = base.RefreshDirty();
        bool layoutDirty = LayoutBaseline == null || LayoutJson() != LayoutBaseline;
        changed |= layoutDirty != IsLayoutDirty;
        IsLayoutDirty = layoutDirty;
        return changed;
    }

    /// <summary>지금 배치를 기준으로 (파일 내용은 그대로) — 불러올 때, 그리고 처음 보일 때의 정리 배치는 편집이 아니므로 그 뒤에 (그 전에 배치 편집이 없었을 때만 — IsLayoutAtBaseline)</summary>
    public void MarkLayoutBaseline()
    {
        LayoutBaseline = LayoutJson();
        IsLayoutDirty = false;
    }

    /// <summary>지금 배치가 기준과 같은가 — 새로 판정한다 (캐시를 쓰지 않는다). O(노드 수)</summary>
    public bool IsLayoutAtBaseline()
    {
        return LayoutBaseline != null && LayoutJson() == LayoutBaseline;
    }

    /// <summary>배치 파일에서 읽은 배치를 입히고 기준으로 한다 (불러온 직후 — diskText = 그 파일 내용). 이어지지 않은 노드의 이름표가 트리와 겹치면 채운다. O(노드 수)</summary>
    public void ApplyLoadedLayout(BehaviorTreeLayoutData layout, string diskText)
    {
        ApplyLayout(layout);
        MarkLayoutBaseline();
        LayoutDiskText = diskText;
        LayoutError = null;
    }

    /// <summary>배치 파일을 읽지 못했다 — 배치 없이 연 채로 두고 까닭을 남긴다 (검사 목록 경고 · 저장할 때 덮어쓸지 묻기). diskText = 읽은 내용 (읽지도 못했으면 null)</summary>
    public void MarkLayoutUnreadable(string error, string diskText)
    {
        LayoutError = error;
        LayoutDiskText = diskText;
    }

    /// <summary>배치 파일에 쓴 직후 — 쓴 글이 새 기준이자 파일 내용</summary>
    public void MarkLayoutSaved(string text)
    {
        LayoutBaseline = text;
        LayoutDiskText = text;
        LayoutError = null;
        IsLayoutDirty = LayoutJson() != text;
    }

    /// <summary>
    /// 창을 직렬화하기 전: 바꾼 쪽만 담는다 — 트리 JSON (Json — 트리를 바꾸지 않았으면 비움)과 배치 (EditorJson · 배치 파일 내용 EditorDiskText — 배치를 바꾸지 않았으면 비움).
    /// 담지 않은 쪽은 되살릴 때 다시 읽은 파일 그대로 둔다 — 그 사이 밖에서 바뀐 파일 (버전 관리로 스크립트와 함께 받음 등)을 담은 때의 내용으로 덮지 않게.
    /// 배치에 저장할 수 없는 값 (이어지지 않은 노드)이 있으면 그 노드를 뺀 나머지를 담는다 (CapturableLayout). 둘 다 기준과 같으면 (불러올 때 채운 이름표뿐이어도) null. O(노드 수)
    /// </summary>
    public override UnsavedEdit CaptureUnsaved()
    {
        UnsavedEdit edit = base.CaptureUnsaved();
        string layout = LayoutJson();
        if (LayoutBaseline != null && layout == LayoutBaseline)
            return edit;

        edit = edit ?? NewUnsavedEdit("");
        edit.EditorJson = layout.StartsWith(UnsavableMark, StringComparison.Ordinal) ? CapturableLayout() : layout;
        edit.EditorDiskText = LayoutDiskText ?? "";
        return edit;
    }

    /// <summary>
    /// 저장할 수 없는 값이 있는 배치를 담는 글 — 첫 줄은 UnsavableMark + 뺀 이어지지 않은 노드 ("이름표: 까닭", " · "로 이음), 그다음 줄부터 그 노드를 뺀 배치 글.
    /// 되살릴 때 나머지를 입히고 일부만 되살렸다고 알린다 (ReadCapturedLayout). O(노드 수)
    /// </summary>
    private string CapturableLayout()
    {
        var skipped = new List<string>();
        string rest = BehaviorTreeLayoutFile.WriteSkippingUnsavable(this, skipped);
        return UnsavableMark + string.Join(" · ", skipped).Replace('\r', ' ').Replace('\n', ' ') + "\n" + rest; // 첫 줄은 한 줄 — 되살릴 때 첫 줄바꿈에서 나눈다
    }

    /// <summary>
    /// 다시 만든 뒤: 다시 읽은 이 사본 (같은 파일)에 담아 둔 쪽만 입힌다 — 트리 JSON (바탕 — 파일 내용 DiskText도 담은 사본의 것으로)과 배치 (배치 파일 내용 LayoutDiskText도 담은 사본의 것으로).
    /// 그 사이 밖에서 바뀐 파일은 저장할 때 묻는다. 담지 않은 쪽은 다시 읽은 그대로 (트리 JSON만 입혀도 이어지지 않은 노드 · 남은 노드의 카드 자리 · 메모는 그대로).
    /// 다시 읽은 사본이 이미 같으면 그대로 두고 changed = false. 트리 JSON을 지금 코드로 읽을 수 없거나 담을 때 저장할 수 없는 값이 있었으면 아무것도 바꾸지 않고 false와 까닭
    /// (배치도 입히지 않는다 — 다른 트리에 입힌 배치는 같은 노드를 둘로 만들 수 있다). 배치를 (모두) 입히지 못하면 트리 JSON은 입히고 true와 까닭 (일부만 되살림) —
    /// 입힐 트리 JSON도 배치도 없었으면 false와 까닭. O(노드 수)
    /// </summary>
    public override bool TryApplyUnsaved(UnsavedEdit edit, out bool changed, out string error)
    {
        changed = false;
        error = null;
        bool treeApplied = false;
        if (string.IsNullOrEmpty(edit.Json) == false)
        {
            error = UnsavableTreeReason(edit.Json);
            if (error != null || base.TryApplyUnsaved(edit, out changed, out error) == false)
                return false;
            treeApplied = true;
        }
        if (string.IsNullOrEmpty(edit.EditorJson))
            return true;

        BehaviorTreeLayoutData layout = ReadCapturedLayout(edit.EditorJson, Registry, out error);
        if (layout == null)
            return treeApplied;
        if (LayoutJson() == edit.EditorJson)
            return true; // 다시 읽은 배치가 이미 같다

        ApplyLayout(layout);
        LayoutDiskText = string.IsNullOrEmpty(edit.EditorDiskText) ? null : edit.EditorDiskText;
        RefreshDirty();
        changed = true;
        return true;
    }

    /// <summary>담을 때 트리 JSON에 저장할 수 없는 값이 있었으면 (UnsavableMark) 그 까닭, 아니면 null</summary>
    private static string UnsavableTreeReason(string json)
    {
        return json.StartsWith(UnsavableMark, StringComparison.Ordinal) ? "트리 JSON을 되살리지 못함 — 담을 때 저장할 수 없는 값이 있었음: " + json.Substring(UnsavableMark.Length) : null;
    }

    /// <summary>
    /// 담아 둔 배치 글을 읽는다 — 읽지 못하면 null. 담을 때 저장할 수 없는 값이 있던 배치 (CapturableLayout — 첫 줄이 UnsavableMark)는 뺀 노드를 뺀 나머지를 읽고 그 까닭을 error에 (일부만 되살림).
    /// 지금 코드로 읽을 수 없으면 null과 "배치를 되살리지 못함 — 까닭". 다 읽었으면 error = null
    /// </summary>
    private static BehaviorTreeLayoutData ReadCapturedLayout(string text, BehaviorNodeRegistry registry, out string error)
    {
        string skipped = null;
        if (text.StartsWith(UnsavableMark, StringComparison.Ordinal))
        {
            int lineEnd = text.IndexOf('\n');
            skipped = lineEnd < 0 ? text.Substring(UnsavableMark.Length) : text.Substring(UnsavableMark.Length, lineEnd - UnsavableMark.Length);
            if (lineEnd < 0)
            {
                error = "배치를 되살리지 못함 — 담을 때 저장할 수 없는 값이 있었음: " + skipped;
                return null;
            }
            text = text.Substring(lineEnd + 1);
        }
        if (BehaviorTreeLayoutFile.TryRead(text, registry, out BehaviorTreeLayoutData layout, out error) == false)
        {
            error = "배치를 되살리지 못함 — " + error;
            return null;
        }
        error = skipped == null ? null : "이어지지 않은 노드 일부를 되살리지 못함 — 담을 때 저장할 수 없는 값이 있었음: " + skipped;
        return layout;
    }

    /// <summary>
    /// 배치를 문서에 입힌다 — ROOT 자리 · 이어지지 않은 노드 · 카드 자리 · 메모를 바꾸고, 겹치는 이름표를 채운 뒤 (트리 노드가 지킨다) 문서에 없는 이름표의 자리 · 메모를 지운다.
    /// 이어지지 않은 노드는 Unity 직렬화를 한 번 거친 표현으로 둔다 (트리와 같게 — 바꾸지 않은 값이 미저장으로 보이지 않게).
    /// </summary>
    private void ApplyLayout(BehaviorTreeLayoutData layout)
    {
        RootCardPosition = layout.RootCardPosition;
        LooseNodes.Clear();
        LooseNodes.AddRange(Normalized(layout.LooseNodes));
        Placements.Clear();
        Placements.AddRange(layout.Placements);
        Memos.Clear();
        Memos.AddRange(layout.Memos);
        BehaviorNodeIds.FillMissing(this);
        KeepEditorDataOfExistingNodes();
    }

    /// <summary>노드들을 Unity 직렬화를 한 번 거친 사본으로 (CreateNormalizedCopy — 불러올 때의 트리와 같은 표현)</summary>
    private static List<BehaviorMainNode> Normalized(List<BehaviorMainNode> nodes)
    {
        BehaviorTreeDocumentAsset copy = CreateNormalizedCopy<BehaviorTreeDocumentAsset>(raw => raw.LooseNodes.AddRange(nodes));
        var result = new List<BehaviorMainNode>(copy.LooseNodes);
        DestroyImmediate(copy);
        return result;
    }
    #endregion

    #region 카드 자리
    /// <summary>그 이름표의 카드 자리. 없으면 false. O(카드 수)</summary>
    public bool TryGetPosition(string nodeId, out Vector2 position)
    {
        int index = IndexOfPlacement(nodeId);
        position = index >= 0 ? Placements[index].Position : default;
        return index >= 0;
    }

    /// <summary>그 이름표의 카드 자리를 정한다 (없으면 더한다). 빈 이름표는 무시. O(카드 수)</summary>
    public void SetPosition(string nodeId, Vector2 position)
    {
        if (string.IsNullOrEmpty(nodeId))
            return;
        int index = IndexOfPlacement(nodeId);
        if (index >= 0)
            Placements[index] = new BehaviorCardPlacement(nodeId, position);
        else
            Placements.Add(new BehaviorCardPlacement(nodeId, position));
    }

    /// <summary>그 이름표의 카드 자리를 지운다. 없으면 false. O(카드 수)</summary>
    public bool RemovePosition(string nodeId)
    {
        int index = IndexOfPlacement(nodeId);
        if (index < 0)
            return false;
        Placements.RemoveAt(index);
        return true;
    }

    /// <summary>문서에 없는 이름표의 카드 자리 (Main 노드) · 메모 (모든 노드)를 지운다. O(노드 수 + 카드 수 + 메모 수)</summary>
    private void KeepEditorDataOfExistingNodes()
    {
        var mains = new List<BehaviorMainNode>();
        BehaviorTreeEditing.CollectMainNodes(this, mains);
        var mainIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < mains.Count; i++)
        {
            if (string.IsNullOrEmpty(mains[i].NodeId) == false)
                mainIds.Add(mains[i].NodeId);
        }
        Placements.RemoveAll(placement => mainIds.Contains(placement.NodeId ?? "") == false);

        var nodes = new List<BehaviorNode>();
        BehaviorTreeEditing.CollectAllNodes(this, nodes);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < nodes.Count; i++)
        {
            if (string.IsNullOrEmpty(nodes[i].NodeId) == false)
                ids.Add(nodes[i].NodeId);
        }
        Memos.RemoveAll(memo => ids.Contains(memo.NodeId ?? "") == false);
    }

    private int IndexOfPlacement(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId))
            return -1;
        for (int i = 0; i < Placements.Count; i++)
        {
            if (string.Equals(Placements[i].NodeId, nodeId, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }
    #endregion

    #region 메모
    /// <summary>그 이름표의 메모. 없으면 false. O(메모 수)</summary>
    public bool TryGetMemo(string nodeId, out string text)
    {
        int index = IndexOfMemo(nodeId);
        text = index >= 0 ? Memos[index].Text : null;
        return index >= 0;
    }

    /// <summary>그 이름표의 메모를 정한다 — 빈 글이면 지운다. 빈 이름표는 무시. O(메모 수)</summary>
    public void SetMemo(string nodeId, string text)
    {
        if (string.IsNullOrEmpty(nodeId))
            return;
        int index = IndexOfMemo(nodeId);
        if (string.IsNullOrEmpty(text))
        {
            if (index >= 0)
                Memos.RemoveAt(index);
            return;
        }
        if (index >= 0)
            Memos[index] = new BehaviorNodeMemo(nodeId, text);
        else
            Memos.Add(new BehaviorNodeMemo(nodeId, text));
    }

    /// <summary>그 이름표의 메모를 지운다. 없으면 false. O(메모 수)</summary>
    public bool RemoveMemo(string nodeId)
    {
        int index = IndexOfMemo(nodeId);
        if (index < 0)
            return false;
        Memos.RemoveAt(index);
        return true;
    }

    private int IndexOfMemo(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId))
            return -1;
        for (int i = 0; i < Memos.Count; i++)
        {
            if (string.Equals(Memos[i].NodeId, nodeId, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }
    #endregion

    /// <summary>Unity 직렬화를 한 번 거친 사본 (CreateNormalizedCopy — 미저장 판정의 표현을 처음부터 맞춘다). 이름은 트리 Id.</summary>
    private static BehaviorTreeDocumentAsset CreateNormalized(BehaviorTreeAsset tree, BehaviorNodeRegistry registry)
    {
        BehaviorTreeDocumentAsset asset = CreateNormalizedCopy<BehaviorTreeDocumentAsset>(raw => raw.Tree = tree);
        asset.Registry = registry;
        asset.name = tree.Id ?? ""; // 만들 때 한 번만 정한다 — 트리 Id를 따라 바꾸면 Undo가 취소하지 못하는 값이 생긴다
        return asset;
    }
}

/// <summary>노드 메모 하나 — 노드 이름표 (NodeId)와 글 (편집기만)</summary>
[Serializable]
public struct BehaviorNodeMemo
{
    public string NodeId;
    public string Text;

    public BehaviorNodeMemo(string nodeId, string text)
    {
        NodeId = nodeId;
        Text = text;
    }
}

/// <summary>카드 자리 하나 — 노드 이름표 (NodeId)와 카드의 왼쪽 위 (그래프 좌표)</summary>
[Serializable]
public struct BehaviorCardPlacement
{
    public string NodeId;
    public Vector2 Position;

    public BehaviorCardPlacement(string nodeId, Vector2 position)
    {
        NodeId = nodeId;
        Position = position;
    }
}