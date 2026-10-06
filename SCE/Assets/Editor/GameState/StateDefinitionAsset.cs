using System;
using UnityEngine;

/// <summary>
/// 상태 정의 파일 1개의 편집용 사본. SerializedObject 바인딩·Undo를 쓰기 위한 그릇이며 에셋으로 저장하지 않는다.
/// 직렬화 필드는 Set 하나뿐 — Undo는 Set만 되돌리고, 파일 경로·미저장 판정 기준 같은 툴 상태는 건드리지 않는다.
/// </summary>
public sealed class StateDefinitionAsset : ScriptableObject
{
    public StateDefinitionSet Set;

    [NonSerialized] public string FilePath;      // 원본 파일 경로. 새 파일은 저장 전까지 null
    [NonSerialized] public string DiskText;      // 불러오거나 저장했을 때의 파일 내용 — 밖에서 고쳐졌는지 비교용
    [NonSerialized] public bool HasBom;          // 원본의 BOM 유무를 그대로 유지 (불필요한 diff 방지)
    [NonSerialized] public string BaselineJson;  // 불러오거나 저장했을 때의 정규화 JSON — 미저장 판정 기준
    [NonSerialized] public bool IsDirty;         // RefreshDirty가 갱신하는 캐시
    [NonSerialized] public int ErrorCount;       // 마지막 검사 결과 (목록 표시·저장 거부용)
    [NonSerialized] public int WarningCount;

    /// <summary>디스크에서 불러온 파일.</summary>
    public static StateDefinitionAsset Create(StateDefinitionSet set, string filePath, string diskText, bool hasBom)
    {
        StateDefinitionAsset asset = CreateBase(set);
        asset.FilePath = filePath;
        asset.DiskText = diskText;
        asset.HasBom = hasBom;
        asset.BaselineJson = StateDefinitionLoader.Serialize(set);
        asset.IsDirty = false;
        return asset;
    }

    /// <summary>툴에서 새로 만든 파일. 비교 기준이 없으므로 저장 전까지 항상 미저장.</summary>
    public static StateDefinitionAsset CreateNew(StateDefinitionSet set)
    {
        StateDefinitionAsset asset = CreateBase(set);
        asset.IsDirty = true;
        return asset;
    }

    private static StateDefinitionAsset CreateBase(StateDefinitionSet set)
    {
        var asset = CreateInstance<StateDefinitionAsset>();
        asset.hideFlags = HideFlags.DontSave; // HideAndDontSave는 NotEditable을 포함해 바인딩된 칸이 읽기 전용이 된다
        asset.Set = set;
        return asset;
    }

    /// <summary>
    /// 지금 내용이 기준(불러온·저장한 때)과 다른지 다시 계산한다. 판정이 바뀌었으면 true.
    /// 플래그를 켜고 끄는 방식이 아니라 내용 비교라서, 값을 바꿨다가 되돌리거나 Undo로 돌아가면 미저장 표시도 사라진다.
    /// </summary>
    public bool RefreshDirty()
    {
        bool dirty = BaselineJson == null || StateDefinitionLoader.Serialize(Set) != BaselineJson;
        bool changed = dirty != IsDirty;
        IsDirty = dirty;
        return changed;
    }

    public void MarkSaved(string filePath, string savedText)
    {
        FilePath = filePath;
        DiskText = savedText;
        BaselineJson = savedText;
        IsDirty = false;
    }
}