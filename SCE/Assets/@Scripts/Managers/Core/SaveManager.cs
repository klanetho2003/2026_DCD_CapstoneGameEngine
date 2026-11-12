using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static Define;

/// <summary>
/// 로컬 JSON 저장을 맡는다. 파일 종류(ESaveFile)마다 파일 하나이고 파일 안은 구역(ISaveSection)마다 버전과 데이터를 담는다.
/// 저장 대상을 더할 때는 구역 클래스를 만들고 RegisterSections에 한 줄 더한다.
/// 쓰기는 임시 파일에 쓴 뒤 원자적으로 바꿔 쓰는 도중 꺼져도 이전 파일이 남고 이전 파일은 .bak으로 남긴다.
/// 읽기는 본 파일, 안 되면 .bak 순서이며 둘 다 안 되면 처음 상태로 시작하고 읽지 못한 파일은 .corrupt로 남겨 덮어쓰지 않는다.
/// 손상 검사는 구역 글의 SHA-256이며 편집기는 손으로 고친 파일을 경고만 하고 읽는다.
/// 저장된 구역 버전이 낮으면 구역의 Migrate로 한 단계씩 올린 뒤 입히고, 모르는 구역과 입히지 못한 구역은 보존해 그대로 다시 쓴다.
/// 없앤 구역(s_retiredKeys)은 보존하지 않고 버리며, 등록된 구역이 없는 파일은 읽지도 만들지도 쓰지도 않는다.
/// 파일과 백업이 모두 없으면 불러올 때 처음 상태로 새로 만든다. 이번 실행에서 불러오기 전에는 그 파일을 저장하지 않는다. Stage가 끝나면 진행 파일을 자동 저장한다.
/// Managers의 필드 초기화에서 만들어지므로 Unity API는 Init부터 쓴다. 매 프레임 비용은 없다.
/// </summary>
public class SaveManager
{
    private const int FORMAT_VERSION = 1;
    private const string FOLDER_NAME = "Save";
    private const string TEMP_SUFFIX = ".tmp";
    private const string BACKUP_SUFFIX = ".bak";
    private const string CORRUPT_SUFFIX = ".corrupt-";
    private const int WRITE_BUFFER_SIZE = 4096;

    private const string FORMAT_FIELD = "format";
    private const string HASH_FIELD = "hash";
    private const string SECTIONS_FIELD = "sections";
    private const string VERSION_FIELD = "v";
    private const string DATA_FIELD = "data";

    /// <summary>ESaveFile 순서의 파일 이름이다.</summary>
    private static readonly string[] s_fileNames = { "Settings.json", "Progress.json" };

    /// <summary>구역 데이터를 읽고 쓰는 공용 직렬화기이며 상태가 없어 하나를 함께 쓴다.</summary>
    private static readonly JsonSerializer s_serializer = JsonSerializer.Create(new JsonSerializerSettings
    {
        MissingMemberHandling = MissingMemberHandling.Ignore,
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Double,
    });

    /// <summary>같은 객체 안의 같은 칸 이름은 읽기 실패로 본다.</summary>
    private static readonly JsonLoadSettings s_loadSettings = new JsonLoadSettings
    {
        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
    };

    private static readonly UTF8Encoding s_utf8 = new UTF8Encoding(false);

    /// <summary>없앤 구역의 키이며 옛 파일에 남아 있어도 보존하지 않고 버린다. 구역을 없앨 때 그 키를 더하고, 이 키는 다시 쓰지 않는다.</summary>
    private static readonly string[] s_retiredKeys = { "gameState", "inputBindings" };

    /// <summary>파일 하나의 경로와 이번 실행의 상태다.</summary>
    private sealed class FileState
    {
        public readonly string Path;
        public readonly string TempPath;
        public readonly string BackupPath;

        /// <summary>이번 실행에서 불러왔거나 처음 상태로 정했는지 나타내며 거짓이면 저장하지 않는다.</summary>
        public bool Loaded;

        /// <summary>모르는 구역과 입히지 못한 구역의 저장 글이며 저장할 때 그대로 다시 쓴다.</summary>
        public readonly Dictionary<string, JToken> Preserved = new Dictionary<string, JToken>(StringComparer.Ordinal);

        public FileState(string path)
        {
            Path = path;
            TempPath = path + TEMP_SUFFIX;
            BackupPath = path + BACKUP_SUFFIX;
        }
    }

    private readonly List<ISaveSection> _sections = new List<ISaveSection>();
    private readonly FileState[] _files = new FileState[s_fileNames.Length];

    /// <summary>저장 폴더 경로다.</summary>
    public static string FolderPath => Path.Combine(Application.persistentDataPath, FOLDER_NAME);

    /// <summary>파일 종류의 본 파일 경로다.</summary>
    /// <param name="file">경로를 구할 파일 종류다.</param>
    public static string FilePathOf(ESaveFile file)
    {
        return Path.Combine(FolderPath, s_fileNames[(int)file]);
    }

    /// <summary>파일 경로를 정하고 구역을 등록한 뒤 자동 저장 시점을 구독한다.</summary>
    public void Init()
    {
        for (int i = 0; i < _files.Length; i++)
            _files[i] = new FileState(FilePathOf((ESaveFile)i));
        RegisterSections();
        Managers.Stage.PhaseChanged += OnStagePhaseChanged;
    }

    #region 구역
    /// <summary>저장 대상 목록이며 구역을 더할 때 여기에 한 줄 더한다.</summary>
    private void RegisterSections()
    {
        Register(new StageRecordSaveSection());
    }

    private void Register(ISaveSection section)
    {
        if (FindSection(section.SaveFile, section.Key) != null)
        {
            LogPrinter.LogError($"[SaveManager] {section.SaveFile} 파일에 구역 '{section.Key}'가 이미 있어 등록하지 않음");
            return;
        }
        _sections.Add(section);
    }

    private ISaveSection FindSection(ESaveFile file, string key)
    {
        for (int i = 0; i < _sections.Count; i++)
        {
            ISaveSection section = _sections[i];
            if (section.SaveFile == file && string.Equals(section.Key, key, StringComparison.Ordinal))
                return section;
        }
        return null;
    }

    /// <summary>파일에 등록된 구역이 있는지다. 없으면 그 파일은 읽지도 만들지도 쓰지도 않는다. O(구역 수)</summary>
    private bool HasSections(ESaveFile file)
    {
        for (int i = 0; i < _sections.Count; i++)
        {
            if (_sections[i].SaveFile == file)
                return true;
        }
        return false;
    }

    private static bool IsRetired(string key)
    {
        for (int i = 0; i < s_retiredKeys.Length; i++)
        {
            if (string.Equals(s_retiredKeys[i], key, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
    #endregion

    #region 불러오기
    /// <summary>
    /// 저장 경로에서 파일을 찾아 읽고 그 파일의 구역마다 입힌다. 파일과 백업이 모두 없으면 처음 상태로 새로 만든다.
    /// 없앤 구역이 들어 있었으면 바로 다시 써서 파일에서 지운다. 등록된 구역이 없는 파일은 손대지 않고 Unused다.
    /// 어떤 결과든 이후 그 파일을 저장할 수 있다. O(파일 크기 + 구역 수)
    /// </summary>
    /// <param name="file">불러올 파일 종류다.</param>
    public ESaveLoadResult Load(ESaveFile file)
    {
        if (HasSections(file) == false)
            return ESaveLoadResult.Unused;

        FileState state = _files[(int)file];
        state.Preserved.Clear();
        state.Loaded = true;

        if (TryReadFile(state.Path, out JObject sections, out string error))
        {
            if (Apply(file, sections))
                Save(file);
            LogPrinter.Log($"[SaveManager] {s_fileNames[(int)file]} 불러옴");
            return ESaveLoadResult.Loaded;
        }

        bool hasFile = File.Exists(state.Path);
        if (hasFile == false && File.Exists(state.BackupPath) == false)
        {
            // 처음 실행이다. 처음 상태로 두고 바로 파일을 만든다 — 만들지 못해도 Loaded라 다음 저장이 다시 만든다
            LogPrinter.Log($"[SaveManager] {s_fileNames[(int)file]} 없음 — 처음 상태로 새로 만듦 ({state.Path})");
            ResetSections(file);
            Save(file);
            return ESaveLoadResult.NoFile;
        }

        if (TryReadFile(state.BackupPath, out sections, out string backupError))
        {
            Debug.LogError($"[SaveManager] {s_fileNames[(int)file]} 읽기 실패 ({error}) — 백업으로 불러옴");
            KeepCorrupt(state.Path);
            if (Apply(file, sections))
                Save(file);
            return ESaveLoadResult.RestoredFromBackup;
        }

        Debug.LogError($"[SaveManager] {s_fileNames[(int)file]} 읽기 실패 ({error}), 백업도 실패 ({backupError}) — 처음 상태로 시작하고 읽지 못한 파일은 {CORRUPT_SUFFIX}로 남김");
        KeepCorrupt(state.Path);
        ResetSections(file);
        return ESaveLoadResult.Corrupted;
    }

    /// <summary>등록된 구역마다 저장 글을 입히고, 모르는 구역과 입히지 못한 구역은 보존한다. 없앤 구역을 버렸으면 true다.</summary>
    private bool Apply(ESaveFile file, JObject sections)
    {
        FileState state = _files[(int)file];
        bool droppedRetired = false;
        foreach (JProperty property in sections.Properties())
        {
            if (FindSection(file, property.Name) != null)
                continue;
            if (IsRetired(property.Name))
            {
                LogPrinter.Log($"[SaveManager] {s_fileNames[(int)file]}의 없앤 구역 '{property.Name}'을 버림");
                droppedRetired = true;
                continue;
            }
            state.Preserved[property.Name] = property.Value.DeepClone();
        }

        for (int i = 0; i < _sections.Count; i++)
        {
            ISaveSection section = _sections[i];
            if (section.SaveFile != file)
                continue;

            JToken entry = sections[section.Key];
            if (entry == null)
            {
                SafeReset(section);
                continue;
            }
            if (TryRestore(section, entry, out string error) == false)
            {
                state.Preserved[section.Key] = entry.DeepClone();
                SafeReset(section);
                Debug.LogError($"[SaveManager] 구역 '{section.Key}' 입히기 실패 ({error}) — 처음 상태로 두고 저장된 글은 보존함");
            }
        }
        return droppedRetired;
    }

    /// <summary>구역 글의 버전을 확인하고 필요하면 한 단계씩 이주한 뒤 입힌다.</summary>
    private static bool TryRestore(ISaveSection section, JToken entry, out string error)
    {
        try
        {
            if (entry is not JObject entryObject || entryObject[VERSION_FIELD] is not JValue versionToken || versionToken.Type != JTokenType.Integer)
            {
                error = "구역 형식이 아님";
                return false;
            }

            int version = versionToken.Value<int>();
            if (version < 1 || version > section.Version)
            {
                error = $"모르는 버전 v{version} (지금 v{section.Version})";
                return false;
            }

            JToken data = entryObject[DATA_FIELD] ?? JValue.CreateNull();
            if (version < section.Version)
                data = data.DeepClone();
            for (int from = version; from < section.Version; from++)
            {
                data = section.Migrate(from, data);
                if (data == null)
                {
                    error = $"v{from} → v{from + 1} 이주가 없음";
                    return false;
                }
            }
            section.Restore(data, s_serializer);
        }
        catch (Exception exception)
        {
            error = $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
        error = null;
        return true;
    }

    private void ResetSections(ESaveFile file)
    {
        for (int i = 0; i < _sections.Count; i++)
        {
            if (_sections[i].SaveFile == file)
                SafeReset(_sections[i]);
        }
    }

    private static void SafeReset(ISaveSection section)
    {
        try
        {
            section.ResetToDefault();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }
    #endregion

    #region 저장
    /// <summary>
    /// 파일의 구역마다 지금 상태를 모아 원자적으로 쓴다. 이번 실행에서 불러오기 전이거나 구역 하나라도 모으지 못하면 쓰지 않고 false다 (이전 파일 그대로).
    /// 등록된 구역이 없는 파일은 쓰지 않고 false다. O(구역 데이터 크기)
    /// </summary>
    /// <param name="file">저장할 파일 종류다.</param>
    public bool Save(ESaveFile file)
    {
        if (HasSections(file) == false)
            return false;

        FileState state = _files[(int)file];
        if (state.Loaded == false)
        {
            // 저장이 빠지는 것이라 빌드에서도 남긴다. 호출 스택으로 누가 Load 전에 저장했는지 본다
            Debug.LogWarning($"[SaveManager] {s_fileNames[(int)file]}는 이번 실행에서 불러오기 전이라 저장하지 않음 — Load({file})가 먼저 불려야 한다");
            return false;
        }

        var sections = new JObject();
        try
        {
            for (int i = 0; i < _sections.Count; i++)
            {
                ISaveSection section = _sections[i];
                if (section.SaveFile != file || state.Preserved.ContainsKey(section.Key))
                    continue;
                sections[section.Key] = new JObject
                {
                    [VERSION_FIELD] = section.Version,
                    [DATA_FIELD] = section.Capture(s_serializer),
                };
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"[SaveManager] {s_fileNames[(int)file]} 저장 중단 — 구역을 모으지 못함 ({exception.GetType().Name}: {exception.Message})");
            return false;
        }

        foreach (KeyValuePair<string, JToken> pair in state.Preserved)
            sections[pair.Key] = pair.Value.DeepClone();

        var root = new JObject
        {
            [FORMAT_FIELD] = FORMAT_VERSION,
            [HASH_FIELD] = HashOf(sections),
            [SECTIONS_FIELD] = sections,
        };
        if (TryWriteAtomic(state, root) == false)
            return false;
        LogPrinter.Log($"[SaveManager] {s_fileNames[(int)file]} 저장");
        return true;
    }

    /// <summary>새 게임 등 — 파일의 구역을 처음 상태로 돌리고 보존한 구역도 버린 뒤 바로 저장한다.</summary>
    /// <param name="file">처음 상태로 돌릴 파일 종류다.</param>
    public bool ResetFile(ESaveFile file)
    {
        if (HasSections(file) == false)
            return false;

        FileState state = _files[(int)file];
        state.Preserved.Clear();
        state.Loaded = true;
        ResetSections(file);
        return Save(file);
    }

    /// <summary>편집기 도구용 — 파일 종류의 본 파일 · 백업 · 임시 파일을 지운다 (읽지 못해 남긴 .corrupt 파일은 남긴다).</summary>
    /// <param name="file">지울 파일 종류다.</param>
    public static void DeleteFiles(ESaveFile file)
    {
        string path = FilePathOf(file);
        TryDelete(path);
        TryDelete(path + BACKUP_SUFFIX);
        TryDelete(path + TEMP_SUFFIX);
    }
    #endregion

    #region 자동 저장
    /// <summary>Stage가 끝나 게임 종료 팝업으로 바뀌면 진행 파일을 저장한다.</summary>
    private void OnStagePhaseChanged(EStagePhase previous, EStagePhase next)
    {
        if (next == EStagePhase.StageResult)
            Save(ESaveFile.Progress);
    }
    #endregion

    #region 파일
    /// <summary>파일을 읽어 형식과 손상 검사를 지난 구역 객체를 꺼낸다. 실패하면 false와 까닭이다.</summary>
    private static bool TryReadFile(string path, out JObject sections, out string error)
    {
        sections = null;
        if (File.Exists(path) == false)
        {
            error = "파일 없음";
            return false;
        }

        JObject root;
        try
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var text = new StreamReader(stream, s_utf8))
            using (var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double })
                root = JObject.Load(reader, s_loadSettings);
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
        {
            error = $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }

        if (root[FORMAT_FIELD] is not JValue format || format.Value is not long formatVersion || formatVersion < 1 || formatVersion > FORMAT_VERSION)
        {
            error = "모르는 파일 형식";
            return false;
        }
        if (root[SECTIONS_FIELD] is not JObject found)
        {
            error = "sections 없음";
            return false;
        }
        if (string.Equals((string)root[HASH_FIELD], HashOf(found), StringComparison.Ordinal) == false)
        {
#if UNITY_EDITOR
            LogPrinter.LogWarning($"[SaveManager] '{path}' 손상 검사 불일치 — 손으로 고친 파일로 보고 편집기에서는 읽음 (빌드에서는 읽지 않음)");
#else
            error = "손상 검사 불일치";
            return false;
#endif
        }

        sections = found;
        error = null;
        return true;
    }

    /// <summary>임시 파일에 쓰고 디스크에 내린 뒤 본 파일과 바꾼다. 이전 본 파일은 백업이 된다.</summary>
    private static bool TryWriteAtomic(FileState state, JObject root)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(state.Path));
            using (var stream = new FileStream(state.TempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var text = new StreamWriter(stream, s_utf8, WRITE_BUFFER_SIZE, true))
                using (var writer = new JsonTextWriter(text) { Formatting = Formatting.Indented })
                    root.WriteTo(writer);
                stream.Flush(true);
            }
            ReplaceFile(state);
            return true;
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is NotSupportedException)
        {
            Debug.LogError($"[SaveManager] '{state.Path}' 쓰기 실패 — 이전 파일 그대로 ({exception.GetType().Name}: {exception.Message})");
            TryDelete(state.TempPath);
            return false;
        }
    }

    /// <summary>임시 파일을 본 파일 자리로 옮긴다. 원자적 교체를 못 하는 플랫폼은 백업 복사 뒤 옮긴다.</summary>
    private static void ReplaceFile(FileState state)
    {
        if (File.Exists(state.Path) == false)
        {
            File.Move(state.TempPath, state.Path);
            return;
        }
        try
        {
            File.Replace(state.TempPath, state.Path, state.BackupPath, true);
        }
        catch (PlatformNotSupportedException)
        {
            File.Copy(state.Path, state.BackupPath, true);
            File.Delete(state.Path);
            File.Move(state.TempPath, state.Path);
        }
    }

    /// <summary>읽지 못한 본 파일을 시각을 붙인 이름으로 옮겨 다음 저장이 덮지 않게 한다.</summary>
    private static void KeepCorrupt(string path)
    {
        if (File.Exists(path) == false)
            return;
        try
        {
            File.Move(path, path + CORRUPT_SUFFIX + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            Debug.LogError($"[SaveManager] 읽지 못한 파일을 옮기지 못함 '{path}' ({exception.Message})");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            Debug.LogError($"[SaveManager] 지우지 못함 '{path}' ({exception.Message})");
        }
    }

    /// <summary>구역 객체를 한 줄 JSON으로 쓴 글의 SHA-256 (소문자 16진수)이다. 읽을 때도 같은 쓰기로 다시 만들어 비교한다.</summary>
    private static string HashOf(JObject sections)
    {
        byte[] bytes = s_utf8.GetBytes(sections.ToString(Formatting.None));
        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(bytes);
            var text = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
                text.Append(hash[i].ToString("x2"));
            return text.ToString();
        }
    }
    #endregion
}
