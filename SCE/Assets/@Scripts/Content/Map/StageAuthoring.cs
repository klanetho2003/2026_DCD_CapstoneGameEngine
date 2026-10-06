using System.Collections.Generic;
using Data;
using UnityEngine;

/// <summary>
/// 스테이지 루트 GameObject에 부착하는 저작 데이터. MapExporter가 읽어 StageData로 기록한다.
/// 런타임 동작은 없다. GameObject 이름은 규약과 무관한 자유 라벨.
/// </summary>
[DisallowMultipleComponent]
public sealed class StageAuthoring : MonoBehaviour
{
    [SerializeField]
    private int _stageKey;

    [SerializeField]
    private StageSettingsData _settings = new();

    public int StageKey { get { return _stageKey; } }
    public StageSettingsData Settings { get { return _settings; } }

#if UNITY_EDITOR
    /// <summary>같은 맵 루트 아래에서 사용되지 않은 가장 작은 Key를 할당한다.</summary>
    [ContextMenu("다음 빈 Key 할당")]
    private void AssignNextFreeKey()
    {
        var used = new HashSet<int>();
        if (transform.parent != null)
        {
            foreach (Transform sibling in transform.parent)
            {
                if (sibling == transform) continue;
                var other = sibling.GetComponent<StageAuthoring>();
                if (other != null) used.Add(other._stageKey);
            }
        }

        int next = 0;
        while (used.Contains(next)) next++;

        UnityEditor.Undo.RecordObject(this, "Assign Stage Key");
        _stageKey = next;
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}