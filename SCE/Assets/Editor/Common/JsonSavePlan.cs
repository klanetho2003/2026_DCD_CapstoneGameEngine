using System;
using System.IO;

/// <summary>
/// 에디터 툴 공용 — 저장 계획 (순수): 파일 이름 = 문서 Id 규칙으로 어느 파일에 쓰는가. Interaction Editor (Set Id) · Behavior Tree Editor (트리 Id)의 저장과 목록의 파일 이름 표시가 같은 판정을 쓴다.
/// 대소문자만 다른 경로는 같은 파일로 본다 — Windows · macOS의 기본 파일 시스템은 대소문자를 구분하지 않는다 (이름을 바꾸지 않고 지금 파일에 쓴다).
/// 파일 이름의 대소문자까지 Id와 같아야 하는 툴은 CaseOnlyRename을 따로 본다.
/// </summary>
public readonly struct JsonSavePlan
{
    public readonly bool IsNew;          // 저장한 적 없는 문서 — TargetPath에 새로 쓴다
    public readonly bool Rename;         // Id가 바뀌었다 — 지금 파일의 이름을 TargetPath로 바꾸고 쓴다
    public readonly bool CaseOnlyRename; // 지금 파일 이름이 Id와 대소문자만 다르다 — Rename이 아니라 지금 파일에 쓴다 (대소문자를 맞추려면 부르는 쪽이 이름을 바꾼다)
    public readonly string TargetPath;   // 규칙의 경로 {폴더}/{Id}.json
    public readonly string WritePath;    // 쓸 경로 — 새 문서 · 이름 바꿈이면 TargetPath, 아니면 지금 파일 (대소문자 그대로)

    private JsonSavePlan(bool isNew, bool rename, bool caseOnlyRename, string targetPath, string writePath)
    {
        IsNew = isNew;
        Rename = rename;
        CaseOnlyRename = caseOnlyRename;
        TargetPath = targetPath;
        WritePath = writePath;
    }

    /// <summary>TargetPath에 파일이 새로 생기는가 (새 문서 · 이름 바꿈) — 그 자리에 이미 파일이 있으면 저장을 막는다 (다른 문서의 파일을 덮어쓰지 않게).</summary>
    public bool WritesNewFile => IsNew || Rename;

    /// <summary>파일 이름 = Id 규칙의 경로.</summary>
    public static string PathFor(string folder, string id)
    {
        return $"{folder}/{id}.json";
    }

    /// <summary>filePath: 지금 파일 (새 문서는 null). O(경로 길이)</summary>
    public static JsonSavePlan Decide(string folder, string filePath, string id)
    {
        string target = PathFor(folder, id);
        bool isNew = string.IsNullOrEmpty(filePath);
        bool rename = isNew == false && string.Equals(filePath, target, StringComparison.OrdinalIgnoreCase) == false;
        bool caseOnlyRename = isNew == false && rename == false
                              && string.Equals(Path.GetFileName(filePath), Path.GetFileName(target), StringComparison.Ordinal) == false;
        return new JsonSavePlan(isNew, rename, caseOnlyRename, target, isNew || rename ? target : filePath);
    }
}