using UnityEditor;
using UnityEditor.AddressableAssets.Settings;

/// <summary>
/// 에디터 툴 공용 — Addressables 수정 이벤트 횟수. Addressable 색인을 가진 쪽(Interaction Editor의 등록 색인 · 게임이 읽는 상태 파일 목록 StateGameFileList)이
/// 만든 때의 값과 비교해 낡았는지 안다. 창마다 구독 · 해제하지 않는다.
/// 이벤트는 설정 변경(항목 · label · 그룹)과 직접 등록한 에셋의 임포트 · 삭제 · 이동에 온다 — 폴더째 등록한 곳 안의 파일이 생기거나 바뀔 때는 오지 않는다.
/// </summary>
public static class EditorAddressables
{
    /// <summary>Addressables 수정 이벤트(AddressableAssetSettings.OnModificationGlobal) 횟수. 도메인 리로드마다 0부터.</summary>
    public static int Version { get; private set; }

    /// <summary>스크립트를 읽을 때마다 한 번 (도메인 리로드가 이벤트 구독도 비우므로 겹치지 않는다).</summary>
    [InitializeOnLoadMethod]
    private static void ListenForModifications()
    {
        AddressableAssetSettings.OnModificationGlobal += (settings, modificationEvent, eventData) => Version++;
    }
}