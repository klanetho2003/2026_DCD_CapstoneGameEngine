using System;

/// <summary>
/// 편집기 검색 칸의 '포함' 판정 하나 — 검색어가 비면 모두, 글자는 대소문자 무시 (Ordinal).
/// Game State Monitor 탭 다섯 (상태 · 변경 기록 · 이벤트 · 맵 NPC · 상호작용 기록), Interaction Editor · State Definition Editor 파일 목록, 검색 팝업(EditorSearchPicker)이 같은 판정을 쓴다.
/// </summary>
public static class EditorSearch
{
    /// <summary>text에 search가 들어 있는가 — search가 비었으면 true, text가 null이면 false. O(글자 수)</summary>
    public static bool Matches(string text, string search)
    {
        return string.IsNullOrEmpty(search) || (text != null && text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}