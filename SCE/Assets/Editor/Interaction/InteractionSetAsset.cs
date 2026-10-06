using System;
using UnityEngine;

/// <summary>
/// 에디터 전용 미러. 런타임 DTO(InteractionSetDefinition)를 그대로 담아
/// SerializedObject 바인딩·Undo·SerializeReference 편집을 Unity에 맡긴다.
/// DontSave: 에셋으로 저장되지 않고, JSON이 유일한 영속 형식이다.
///
/// 직렬화되는 것은 Set 하나뿐이다. 툴 상태(경로·미저장 여부 등)는 [NonSerialized]로 둬서 Undo가 되돌리지 않게 한다 —
/// 직렬화 필드였을 때는 Ctrl+Z가 미저장 표시와 파일 경로까지 되돌렸다.
/// </summary>
public sealed class InteractionSetAsset : ScriptableObject
{
    public InteractionSetDefinition Set;

    [NonSerialized] public string FilePath;   // 원본 JSON 경로. 새 Set은 null — 첫 저장 때 {폴더}/{Set Id}.json으로 정해진다
    [NonSerialized] public string DiskText;   // 불러오거나 저장했을 때의 파일 내용 — 밖에서 바뀌었는지 비교용
    [NonSerialized] public bool HasBom;       // 원본의 BOM 유무를 그대로 유지 (불필요한 diff 방지)
    [NonSerialized] public string Baseline;   // 불러오거나 저장했을 때의 저장용 JSON — 미저장 판정 기준. 새 Set은 null
    [NonSerialized] public bool IsDirty;      // RefreshDirty가 갱신하는 캐시
    [NonSerialized] public int ErrorCount;    // 마지막 검사 결과 (목록 표시·저장 거부용)
    [NonSerialized] public int WarningCount;

    /// <summary>디스크에서 불러온 Set.</summary>
    public static InteractionSetAsset Create(InteractionSetDefinition set, string filePath, string diskText, bool hasBom)
    {
        InteractionSetAsset asset = CreateNormalized(set);
        asset.FilePath = filePath;
        asset.DiskText = diskText;
        asset.HasBom = hasBom;
        asset.Baseline = InteractionLoader.Serialize(asset.Set);
        return asset;
    }

    /// <summary>툴에서 새로 만든 Set — 기준이 없으므로 처음부터 미저장.</summary>
    public static InteractionSetAsset CreateNew(InteractionSetDefinition set)
    {
        InteractionSetAsset asset = CreateNormalized(set);
        asset.IsDirty = true;
        return asset;
    }

    /// <summary>
    /// "지금 저장하면 파일에 쓰일 JSON"이 기준과 다른지 다시 판정한다. 판정이 바뀌었으면 true.
    /// 값을 원래대로 되돌리거나 Undo하면 미저장 표시도 함께 꺼진다 — 플래그를 켜기만 하는 방식은 이것을 못 한다.
    /// 비용: Set 하나를 JSON으로 만드는 O(노드 수). 값이 바뀔 때마다 한 번, 에디터에서만 든다.
    /// </summary>
    public bool RefreshDirty()
    {
        bool dirty = Baseline == null || InteractionLoader.Serialize(Set) != Baseline;
        bool changed = dirty != IsDirty;
        IsDirty = dirty;
        return changed;
    }

    /// <summary>저장 직후: 방금 쓴 내용이 새 기준이 된다.</summary>
    public void MarkSaved(string filePath, string text)
    {
        FilePath = filePath;
        DiskText = text;
        Baseline = text;
        IsDirty = false;
    }

    /// <summary>
    /// Unity 직렬화를 한 번 거친 사본을 만든다 (Instantiate가 직렬화로 복사한다).
    /// Unity 직렬화는 null 문자열을 ""로 바꾸는 식으로 값을 정규화하는데, 이 변환은 첫 편집이나 Undo 때 어차피 일어난다.
    /// 처음부터 정규화된 상태로 시작하면 "기준을 만든 때"와 "편집 뒤"의 표현이 같아져, 바꾸지 않은 값 때문에 미저장으로 판정되지 않는다.
    /// </summary>
    private static InteractionSetAsset CreateNormalized(InteractionSetDefinition set)
    {
        InteractionSetAsset raw = CreateInstance<InteractionSetAsset>();
        raw.hideFlags = HideFlags.DontSave;
        raw.Set = set;

        InteractionSetAsset asset = Instantiate(raw);
        DestroyImmediate(raw);

        // HideAndDontSave는 NotEditable을 포함해 바인딩된 필드가 전부 읽기 전용이 된다. 저장 제외 목적만 남기고 편집은 허용한다.
        asset.hideFlags = HideFlags.DontSave;
        asset.name = set.Id ?? ""; // 만들 때 한 번만 정한다 — Set Id를 따라 바꾸면 Undo로 되돌아가지 않는 값이 생긴다
        return asset;
    }
}