using System;
using System.IO;
using System.Text;

/// <summary>
/// 에디터 툴 공용 — JSON 파일 하나 읽기 · 쓰기 (UTF-8 — UTF-16 · UTF-32 파일 판정과 그 까닭 문장도), 파일 이름으로 쓸 수 있는 글, 고른 폴더의 경로. 상태를 갖지 않는다.
/// 읽기 · 쓰기는 BOM 유무를 이어 간다 — 원본대로 써서 불필요한 diff를 만들지 않게.
/// </summary>
public static class EditorJsonFile
{
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    /// <summary>UTF-8로 읽고 BOM 유무를 반환한다 — 저장할 때 WriteText에 그대로 넘긴다. 읽지 못하면 예외 (IOException 등).</summary>
    public static string ReadText(string path, out bool hasBom)
    {
        byte[] bytes = File.ReadAllBytes(path);
        hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        return hasBom ? Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3) : Encoding.UTF8.GetString(bytes);
    }

    /// <summary>UTF-8로 읽는 툴이 파싱하지 못한 UTF-16 · UTF-32 파일의 까닭 — 상태 파일(StateDefinitionCatalog.ReadFile) · Set 편집 읽기(InteractionSetFile.ReadForEdit)가 같은 문장.</summary>
    public const string WideBomReason = "UTF-16 · UTF-32로 저장된 파일 — 툴은 UTF-8만 읽는다 (게임의 TextAsset은 BOM으로 가려 읽는다). UTF-8로 다시 저장할 것";

    /// <summary>UTF-16 · UTF-32의 BOM으로 시작하는 파일인가 (FF FE · FE FF · 00 00 FE FF) — 앞 4바이트만 읽는다. IO · 권한 문제로 읽지 못하면 false. 부르는 쪽(상태 파일 · Set 편집 읽기)은 파싱에 실패한 파일만 묻는다.</summary>
    public static bool StartsWithWideBom(string path)
    {
        try
        {
            using (FileStream stream = File.OpenRead(path))
            {
                var head = new byte[4];
                int count = stream.Read(head, 0, head.Length);
                if (count >= 2 && ((head[0] == 0xFF && head[1] == 0xFE) || (head[0] == 0xFE && head[1] == 0xFF)))
                    return true;
                return count >= 4 && head[0] == 0 && head[1] == 0 && head[2] == 0xFE && head[3] == 0xFF;
            }
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>파일 내용이 expected와 다른가 — 불러오거나 저장한 뒤 밖에서 바뀌었는지. 읽지 못하면(IOException · 읽기 권한 없음) 바뀐 것으로 본다 — 부르는 쪽이 사용자에게 묻는다.</summary>
    public static bool DiskChanged(string path, string expected)
    {
        try
        {
            return ReadText(path, out _) != expected;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// 파일 이름(확장자 앞)으로 쓸 수 있는 글인가 — 이 플랫폼의 파일 이름 금지 문자(Path.GetInvalidFileNameChars — '/'는 모든 플랫폼, Windows는 '\' · ':' · '*' 등)가 없다.
    /// '/' · '\'가 있으면 하위 폴더에, '..' 마디를 섞으면 폴더 밖에 써진다. 비었는지는 부르는 쪽 규칙 (null · ""는 true). O(글 길이)
    /// Interaction Editor의 Set Id 규칙과 State Definition Editor의 새 파일 이름이 쓴다.
    /// </summary>
    public static bool CanBeFileName(string name)
    {
        return name == null || name.IndexOfAny(InvalidFileNameChars) < 0;
    }

    /// <summary>UTF-8로 쓴다 — hasBom이면 BOM을 붙인다 (ReadText가 읽은 원본의 것). 폴더는 부르는 쪽이 만든다. 실패하면 예외.</summary>
    public static void WriteText(string path, string text, bool hasBom)
    {
        File.WriteAllText(path, text, new UTF8Encoding(hasBom));
    }

    /// <summary>
    /// 폴더 고르기 창(EditorUtility.OpenFolderPanel)이 준 절대 경로 → "Assets/…" — 프로젝트 Assets 폴더(dataPath = Application.dataPath)이거나 그 아래일 때만. 아니면 false.
    /// 이름이 Assets로 시작하는 옆 폴더(…/AssetsBackup)는 아래가 아니다. 대소문자는 무시하고(Windows · macOS의 기본 파일 시스템) '\'는 '/'로 본다.
    /// </summary>
    public static bool TryAssetsFolder(string absolute, string dataPath, out string folder)
    {
        string path = absolute.Replace('\\', '/');
        string data = dataPath.Replace('\\', '/');
        bool inside = path.StartsWith(data, StringComparison.OrdinalIgnoreCase)
                      && (path.Length == data.Length || path[data.Length] == '/'); // 바로 뒤가 경로 구분이어야 그 폴더 아래
        folder = inside ? "Assets" + path.Substring(data.Length) : null;
        return inside;
    }
}