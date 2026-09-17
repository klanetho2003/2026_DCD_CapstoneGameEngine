using UnityEngine;

/// <summary>
/// 에디터 전용 미러. 런타임 DTO(InteractionSetDefinition)를 그대로 담아
/// SerializedObject 바인딩·Undo·SerializeReference 편집을 Unity에 맡긴다.
/// HideAndDontSave: 에셋으로 저장되지 않고, JSON이 유일한 영속 형식이다.
/// </summary>
public sealed class InteractionSetAsset : ScriptableObject
{
    public InteractionSetDefinition Set;
    public string FilePath;   // 원본 JSON 경로. 새 Set은 폴더/{Id}.json 으로 저장 시 확정
    public bool IsDirty;

    public static InteractionSetAsset Create(InteractionSetDefinition set, string filePath)
    {
        InteractionSetAsset asset = CreateInstance<InteractionSetAsset>();

        // HideAndDontSave는 NotEditable을 포함해 바인딩된 필드가 전부 읽기 전용이 된다.
        // 저장 제외 목적만 남기고 편집은 허용한다.
        asset.hideFlags = HideFlags.DontSave;

        asset.Set = set;
        asset.FilePath = filePath;
        asset.name = set.Id;
        return asset;
    }
}