using UnityEngine;

/// <summary>
/// Tools/Debug 메뉴(맵 NPC · 상호작용 · 연출)의 확인 하나 — Play 중이고 게임이 Managers를 이미 만들었을 때만 쓴다.
/// Managers.Instance는 없으면 새로 만들기 때문에, 메뉴가 먼저 읽으면 게임 대신 메뉴가 Managers를 만든다 (Game State Monitor의 연결 확인과 같은 까닭).
/// </summary>
public static class DebugMenuGuard
{
    /// <summary>메뉴를 쓸 수 있는가 — 메뉴 검사(흐리게)용, 로그 없음. O(1)</summary>
    public static bool CanUse()
    {
        return Application.isPlaying && Managers.Initialized;
    }

    /// <summary>메뉴를 실행하기 전 — 쓸 수 없으면 까닭을 콘솔 경고로 남기고 false (tag = "[MapNpcDebug]" 등 메뉴의 로그 머리). O(1)</summary>
    public static bool Require(string tag)
    {
        if (Application.isPlaying == false)
        {
            Debug.LogWarning($"{tag} Play 중에만 쓸 수 있습니다");
            return false;
        }
        if (Managers.Initialized == false)
        {
            Debug.LogWarning($"{tag} Managers 초기화 전입니다 — 게임이 Managers를 만든 뒤에 쓰세요");
            return false;
        }
        return true;
    }
}