using UnityEngine;

/// <summary>
/// 에디터 창들이 함께 쓰는 상태 색 — 같은 뜻에는 같은 색 (R8b-2b에서 한 곳으로 모음).
/// 쓰는 곳: Interaction Editor, State Definition Editor (창 · 규칙 카드), 공용 화면 조각(EditorPaneUI) · 검색 팝업(EditorSearchPicker) · 행 검사 표시(EditorIssueMarks — State Definition Editor의 키 표 · 규칙 카드), 맵 배치 창, Game State Monitor.
/// </summary>
public static class EditorStatusColors
{
    public static readonly Color Error = new Color(1f, 0.45f, 0.45f);  // 게임이 거부함 — 로드 안 됨 · 저장 거부
    public static readonly Color Warning = new Color(1f, 0.82f, 0.4f); // 동작은 하지만 의도와 다를 수 있음
    public static readonly Color Status = new Color(0.7f, 0.7f, 0.7f); // 상태 줄 · 참고
    public static readonly Color ErrorTint = new Color(1f, 0.3f, 0.3f, 0.16f);   // 문제가 있는 행의 배경 — 에러가 있음
    public static readonly Color WarningTint = new Color(1f, 0.8f, 0.3f, 0.12f); // 〃 — 경고만 있음
}