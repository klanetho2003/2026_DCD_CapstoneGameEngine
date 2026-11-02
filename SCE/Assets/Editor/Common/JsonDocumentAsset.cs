using System;
using UnityEngine;

/// <summary>
/// 에디터 툴 공용 — JSON 파일 하나의 편집 사본 바탕. DontSave: 에셋으로 저장되지 않고, JSON이 유일한 영속 형식이다.
/// 편집할 내용은 파생의 직렬화 필드다 (InteractionSetAsset.Set) — SerializedObject 바인딩 · Undo · SerializeReference 편집을 Unity에 맡긴다.
/// 툴 상태(경로 · 미저장 여부 등)는 [NonSerialized]로 둬서 Undo가 바꾸지 않게 한다 — 직렬화 필드였을 때는 Ctrl+Z가 미저장 표시와 파일 경로까지 이전 값으로 바꿨다.
/// 미저장 판정은 "불러온(저장한) 때의 JSON과 지금 저장될 JSON 비교"다 — 저장용 JSON은 파생이 정한다 (SaveJson).
/// 사본은 창이 꺼질 때 지운다 — 창을 다시 만들 때는 저장하지 않은 편집을 UnsavedEdit으로 담아 넘긴다 (JsonDocumentList).
/// 저장용 JSON 밖에 따로 저장하는 편집기 상태가 있는 문서 (Behavior Tree Editor의 배치 파일)는 미저장 판정 · 담기 · 입히기를 이어받아 넓힌다 (RefreshDirty · CaptureUnsaved · TryApplyUnsaved).
/// </summary>
public abstract class JsonDocumentAsset : ScriptableObject
{
    /// <summary>
    /// 창을 다시 만들 때(도메인 리로드 · 다른 창 최대화) 넘길 저장하지 않은 편집 하나 — 창이 직렬화 전에 담고(CaptureUnsaved) 다시 읽은 뒤 입힌다(TryApplyUnsaved · 파생의 CreateUnsaved).
    /// 창의 [SerializeField] 목록에 담겨 넘어간다 (사본은 지워지므로 값만 옮긴다). 필드 이름이 직렬화 이름이다.
    /// </summary>
    [Serializable]
    public sealed class UnsavedEdit
    {
        public string FilePath; // 원본 JSON 경로. 새 문서는 ""
        public string SetId;    // 되살리지 못했을 때 알릴 이름 (DocumentId — Interaction Editor는 Set Id)
        public string Json;     // 담은 때의 저장용 JSON — 저장용 JSON 밖의 상태만 바꾼 문서는 비운다 (Behavior Tree Editor: 배치만 바꾼 트리 — 되살릴 때 다시 읽은 파일 그대로)
        public string DiskText; // 담은 사본의 DiskText (불러오거나 저장한 때의 파일 내용) — 그 사이 밖에서 바뀌었으면 저장할 때 '밖에서 바뀜' 대화가 뜨게
        public bool HasBom;
        public string EditorJson;     // 저장용 JSON 밖의 편집기 상태 (Behavior Tree Editor의 배치 파일 내용) — 그 상태를 바꾸지 않았거나 그런 상태가 없는 툴은 비워 둔다
        public string EditorDiskText; // 담은 사본이 그 상태를 불러오거나 저장한 때의 파일 내용 — 파일이 없었으면 비움
    }

    [NonSerialized] public string FilePath;   // 원본 JSON 경로. 새 문서는 null — 첫 저장 때 정해진다
    [NonSerialized] public string DiskText;   // 불러오거나 저장했을 때의 파일 내용 — 밖에서 바뀌었는지 비교용
    [NonSerialized] public bool HasBom;       // 원본의 BOM 유무를 그대로 유지 (불필요한 diff 방지)
    [NonSerialized] public string Baseline;   // 불러오거나 저장했을 때의 저장용 JSON — 미저장 판정 기준. 새 문서는 null
    [NonSerialized] public bool IsDirty;      // RefreshDirty가 갱신하는 캐시
    [NonSerialized] public int ErrorCount;    // 마지막 검사 결과 (목록 표시·저장 거부용)
    [NonSerialized] public int WarningCount;
    [NonSerialized] private string _loadFillJson; // 불러온 직후 툴이 채운 값의 저장용 JSON (MarkLoadFill · HasOnlyLoadFill). 채우지 않았거나 저장했으면 null

    /// <summary>"지금 저장하면 파일에 쓰일 JSON" — 미저장 판정 · 담기 · 저장이 모두 이것을 쓴다. O(내용 크기)</summary>
    public abstract string SaveJson();

    /// <summary>되살리지 못했을 때 알릴 이름 (Interaction Editor는 Set Id). 없으면 null.</summary>
    protected abstract string DocumentId { get; }

    /// <summary>담아 둔 저장용 JSON을 내용으로 한다 — 불러올 때와 같은 표현으로 (Unity 직렬화를 한 번 거친 값, CreateNormalizedCopy). 지금 코드로 읽을 수 없으면 그대로 두고 false와 이유.</summary>
    protected abstract bool ApplyJson(string json, out string error);

    /// <summary>
    /// "지금 저장하면 파일에 쓰일 JSON"이 기준과 다른지 다시 판정한다. 판정이 바뀌었으면 true.
    /// 값을 원래대로 고치거나 Undo하면 미저장 표시도 함께 꺼진다 — 플래그를 켜기만 하는 방식은 이것을 못 한다.
    /// 비용: SaveJson 한 번 (Interaction Set은 O(노드 수)). 값이 바뀔 때마다 한 번, 에디터에서만 든다.
    /// </summary>
    public virtual bool RefreshDirty()
    {
        bool dirty = Baseline == null || SaveJson() != Baseline;
        bool changed = dirty != IsDirty;
        IsDirty = dirty;
        return changed;
    }

    /// <summary>저장 직후: 방금 쓴 내용이 새 기준이 된다. 불러올 때 채운 값도 이제 파일에 있다.</summary>
    public void MarkSaved(string filePath, string text)
    {
        FilePath = filePath;
        DiskText = text;
        Baseline = text;
        _loadFillJson = null;
        IsDirty = false;
    }

    /// <summary>
    /// 불러온 직후 툴이 채운 값(Interaction Set의 빈 Set Id 등)을 저장하지 않은 편집으로 둔다 — 기준은 파일 그대로 (`*`). 채운 직후에 부른다.
    /// 채운 그대로는 버려도 잃는 것이 없다 — 다시 읽으면 또 채운다 (HasOnlyLoadFill).
    /// </summary>
    protected void MarkLoadFill()
    {
        _loadFillJson = SaveJson();
        RefreshDirty();
    }

    /// <summary>
    /// 저장하지 않은 편집이 불러올 때 채운 값뿐인가 — 지금 저장될 JSON이 채운 직후와 같으면 true (다른 편집을 원래대로 고친 경우 포함).
    /// 버려도 다시 읽으면 또 채우므로 잃는 것이 없다 — 창의 '버릴까요' 대화 · 다시 읽기 판단이 이것을 빼고 센다 (JsonDocumentList.CountDirty).
    /// `*` · [모두 저장] · 창을 닫을 때의 대화는 그대로 센다 (채운 값은 아직 파일에 없다). O(내용 크기) — 채운 사본만 JSON을 만든다.
    /// </summary>
    public bool HasOnlyLoadFill()
    {
        return IsDirty && _loadFillJson != null && SaveJson() == _loadFillJson;
    }

    #region 창을 다시 만들 때 (저장하지 않은 편집)
    /// <summary>
    /// 창을 직렬화하기 전: 저장하지 않은 편집이 있으면 담는다. O(내용 크기)
    /// 지금 저장될 JSON이 기준과 같으면(원래대로 고친 편집 포함) null. 불러올 때 채운 그대로여도 null — 다시 읽으면 또 채운다.
    /// </summary>
    public virtual UnsavedEdit CaptureUnsaved()
    {
        string json = SaveJson();
        if ((Baseline != null && json == Baseline) || json == _loadFillJson)
            return null;

        return NewUnsavedEdit(json);
    }

    /// <summary>이 사본의 담을 편집 — 경로 · 이름 · 파일 내용 · BOM은 지금 사본의 것, 내용은 json. 편집기 상태 칸은 비어 있다 (넓히는 파생이 채운다)</summary>
    protected UnsavedEdit NewUnsavedEdit(string json)
    {
        return new UnsavedEdit { FilePath = FilePath ?? "", SetId = DocumentId ?? "", Json = json, DiskText = DiskText ?? "", HasBom = HasBom };
    }

    /// <summary>
    /// 다시 만든 뒤: 다시 읽은 이 사본(같은 파일)에 담아 둔 편집을 입힌다. 기준 · BOM은 지금 파일 그대로 — `*`는 "지금 파일과 다름"이다.
    /// 파일 내용(DiskText)은 담은 사본의 것(불러오거나 저장한 때)으로 둔다 — 그 사이 밖에서 바뀌었으면(버전 관리로 스크립트와 함께 받음 등) 저장할 때 '밖에서 바뀜' 대화가 뜬다.
    /// changed: 사본을 바꿨나 — 다시 읽은 사본이 이미 그 편집과 같으면(그 사이 파일이 그 편집과 같아짐 — 불러올 때 채운 값 포함) 그대로 두고 false (파일 내용도 지금 것).
    /// 지금 코드로 읽을 수 없는 JSON(노드 코드가 바뀜 등)이면 그대로 두고 false와 이유. O(내용 크기)
    /// 넓히는 파생은 일부만 입히고 true와 이유를 줄 수 있다 — 입히지 못한 나머지는 되살리지 못한 것으로 알린다 (JsonDocumentList.RestoreUnsaved).
    /// </summary>
    public virtual bool TryApplyUnsaved(UnsavedEdit edit, out bool changed, out string error)
    {
        changed = false;
        error = null;
        if (SaveJson() == edit.Json)
            return true;

        if (ApplyJson(edit.Json, out error) == false)
            return false;

        DiskText = edit.DiskText;
        RefreshDirty();
        changed = true;
        return true;
    }
    #endregion

    /// <summary>
    /// Unity 직렬화를 한 번 거친 사본을 만든다 (Instantiate가 직렬화로 복사한다) — fill이 내용을 넣은 원본은 지운다. 이름(name)은 부르는 쪽이 정한다.
    /// Unity 직렬화는 null 문자열을 ""로 바꾸는 식으로 값을 정규화하는데, 이 변환은 첫 편집이나 Undo 때 어차피 일어난다.
    /// 처음부터 정규화된 상태로 시작하면 "기준을 만든 때"와 "편집 뒤"의 표현이 같아져, 바꾸지 않은 값 때문에 미저장으로 판정되지 않는다.
    /// </summary>
    protected static T CreateNormalizedCopy<T>(Action<T> fill) where T : JsonDocumentAsset
    {
        T raw = CreateInstance<T>();
        raw.hideFlags = HideFlags.DontSave;
        fill(raw);

        T asset = Instantiate(raw);
        DestroyImmediate(raw);

        // HideAndDontSave는 NotEditable을 포함해 바인딩된 필드가 전부 읽기 전용이 된다. 저장 제외 목적만 남기고 편집은 허용한다.
        asset.hideFlags = HideFlags.DontSave;
        return asset;
    }
}