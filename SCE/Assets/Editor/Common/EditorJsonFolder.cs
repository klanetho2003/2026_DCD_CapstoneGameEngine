using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// 에디터 툴 공용 — JSON 파일 목록과 서명: 폴더 바로 아래 JSON 목록(툴들이 같은 목록 · 순서로 읽는다), 파일들이 바뀌었는지 싸게 알아보는 값.
/// 다른 툴이 관리하는 파일을 읽기만 하는 쪽이, 창이 포커스를 받을 때마다 서명을 비교해 바뀐 경우에만 다시 읽는 데 쓴다.
/// </summary>
public static class EditorJsonFolder
{
    /// <summary>
    /// 폴더 바로 아래 *.json의 경로 — 구분자는 '/', 경로 이름순(대소문자 무시). 하위 폴더는 보지 않는다. 폴더가 없으면 빈 배열.
    /// State Definition Editor · Interaction Editor의 폴더 읽기와 상호작용 참조 수집이 쓴다. O(파일 수 · log 파일 수)
    /// </summary>
    public static string[] ListJsonFiles(string folder)
    {
        if (string.IsNullOrEmpty(folder) || Directory.Exists(folder) == false)
            return Array.Empty<string>();

        string[] files = Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly);
        for (int i = 0; i < files.Length; i++)
            files[i] = files[i].Replace('\\', '/');
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        return files;
    }

    /// <summary>폴더 바로 아래 *.json의 서명 (ListJsonFiles의 FilesSignature). 파일을 열지 않는다 — O(파일 수). 폴더가 없으면 -1.</summary>
    public static long Signature(string folder)
    {
        if (string.IsNullOrEmpty(folder) || Directory.Exists(folder) == false)
            return -1;
        return FilesSignature(ListJsonFiles(folder));
    }

    /// <summary>
    /// 파일들의 경로 · 수정 시각 · 크기로 만든 값 — 파일을 열지 않는다. O(파일 수). 지워진 파일도 값에 들어간다.
    /// 같은 목록으로 만든 값끼리만 견준다. 목록 밖에서 바뀐 것(주소 등록 · 폴더 설정)은 잡지 못한다 — 그때는 부르는 쪽이 목록을 다시 만든다.
    /// </summary>
    public static long FilesSignature(IReadOnlyList<string> files)
    {
        long signature = 17;
        unchecked
        {
            for (int i = 0; i < files.Count; i++)
            {
                var info = new FileInfo(files[i]);
                signature = signature * 31 + StringComparer.Ordinal.GetHashCode(files[i]);
                signature = signature * 31 + (info.Exists ? info.LastWriteTimeUtc.Ticks : -1);
                signature = signature * 31 + (info.Exists ? info.Length : -1);
            }
        }
        return signature;
    }
}