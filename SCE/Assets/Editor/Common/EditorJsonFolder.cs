using System;
using System.IO;

/// <summary>
/// 에디터 툴 공용 — JSON 폴더의 내용이 바뀌었는지 싸게 알아본다.
/// 다른 툴이 관리하는 폴더를 읽기만 하는 쪽이, 창이 포커스를 받을 때마다 비교해 바뀐 경우에만 다시 읽는 데 쓴다.
/// </summary>
public static class EditorJsonFolder
{
    /// <summary>
    /// 폴더 바로 아래 *.json의 이름·수정 시각·크기로 만든 값. 파일을 열지 않는다 — O(파일 수).
    /// 폴더가 없으면 -1.
    /// </summary>
    public static long Signature(string folder)
    {
        if (string.IsNullOrEmpty(folder) || Directory.Exists(folder) == false)
            return -1;

        string[] files = Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        long signature = 17;
        unchecked
        {
            for (int i = 0; i < files.Length; i++)
            {
                var info = new FileInfo(files[i]);
                signature = signature * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(info.Name);
                signature = signature * 31 + info.LastWriteTimeUtc.Ticks;
                signature = signature * 31 + info.Length;
            }
        }
        return signature;
    }
}