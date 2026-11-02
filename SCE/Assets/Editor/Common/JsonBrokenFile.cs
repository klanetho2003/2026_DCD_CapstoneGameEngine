/// <summary>
/// 에디터 툴 공용 — 파싱에 실패한 JSON 파일 하나 (Interaction Editor · State Definition Editor의 파일 목록 항목). 목록에 보여 주기만 하고 편집할 수 없다.
/// 직렬화하지 않는다 — 창을 다시 만들면 폴더를 다시 읽어 새로 만든다.
/// </summary>
public sealed class JsonBrokenFile
{
    public string FilePath; // 파일 경로
    public string Error;    // 읽기 · 파싱에 실패한 까닭 — 목록 툴팁과 보기(EditorPaneUI.BrokenFileView)에 그대로 보인다
}