using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

/// <summary>불러오기 · 새 파일 · 저장.</summary>
public sealed partial class StateDefinitionEditorWindow
{
    #region 불러오기
    /// <summary>
    /// 폴더 바로 아래의 JSON을 읽는다 (하위 폴더는 보지 않음).
    /// 상태 정의 → 편집용 사본 / 상태 정의인데 깨진 파일 → 파싱 실패 항목 / 다른 종류의 JSON → 건너뜀
    /// </summary>
    private void LoadFolder()
    {
        DisposeSelection();
        _selected = null;
        _selectedBroken = null;
        DestroyAssets();
        _brokenFiles.Clear();
        _skippedOtherJson = 0;

        if (Directory.Exists(_folder) == false)
            return;

        string[] files = Directory.GetFiles(_folder, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < files.Length; i++)
        {
            string path = files[i].Replace('\\', '/');
            string text;
            bool hasBom;
            try
            {
                text = ReadText(path, out hasBom);
            }
            catch (Exception e)
            {
                _brokenFiles.Add(new BrokenFile { FilePath = path, Error = $"읽기 실패 >> {e.Message}" });
                continue;
            }

            StateDefinitionSet set = StateDefinitionLoader.ParseRaw(text, out string error);
            if (set != null)
                _assets.Add(StateDefinitionAsset.Create(set, path, text, hasBom));
            else if (StateDefinitionCatalog.LooksLikeStateDefinition(text))
                _brokenFiles.Add(new BrokenFile { FilePath = path, Error = error });
            else
                _skippedOtherJson++;
        }
    }

    private string LoadSummary()
    {
        if (Directory.Exists(_folder) == false)
            return $"폴더가 없습니다: {_folder} — [폴더]로 지정하세요";

        string summary = $"상태 정의 {_assets.Count}개";
        if (_brokenFiles.Count > 0)
            summary += $", 파싱 실패 {_brokenFiles.Count}개";
        if (_skippedOtherJson > 0)
            summary += $", 다른 JSON {_skippedOtherJson}개 건너뜀";
        if (_assets.Count == 0 && _brokenFiles.Count == 0)
            summary += " — 상태 JSON이 있는 폴더를 [폴더]로 지정하세요";
        return summary;
    }

    /// <summary>UTF-8로 읽되 BOM 유무를 기억한다 — 저장할 때 원래대로 써서 불필요한 diff를 만들지 않기 위해.</summary>
    private static string ReadText(string path, out bool hasBom)
    {
        byte[] bytes = File.ReadAllBytes(path);
        hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        return hasBom ? Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3) : Encoding.UTF8.GetString(bytes);
    }

    private void DestroyAssets()
    {
        for (int i = 0; i < _assets.Count; i++)
        {
            if (_assets[i] == null)
                continue;
            Undo.ClearUndo(_assets[i]); // 사라질 사본을 가리키는 Undo 기록 정리
            DestroyImmediate(_assets[i]);
        }
        _assets.Clear();
    }
    #endregion

    #region 새 파일
    private void CreateNewFile()
    {
        string id = "new";
        for (int n = 2; IdTaken(id); n++)
            id = "new" + n;

        var set = new StateDefinitionSet
        {
            Id = id,
            Keys = Array.Empty<StateKeyDefinition>(),
            Rules = Array.Empty<StateRuleDefinition>(),
        };
        StateDefinitionAsset asset = StateDefinitionAsset.CreateNew(set);
        _assets.Add(asset);

        UpdateUnsavedFlag();
        RunValidationNow(); // 목록 순번이 바뀌었으므로 검사 결과를 새로 맞춘다
        SelectAsset(asset);
        RefreshFileList();
        SetStatus($"새 파일 '{id}' — 저장하면 {NewFilePath(id)}에 만들어집니다. 게임이 읽게 하려면 기존 상태 파일과 같은 방식으로 등록해야 합니다");
    }

    private bool IdTaken(string id)
    {
        for (int i = 0; i < _assets.Count; i++)
        {
            if (_assets[i].Set.Id == id)
                return true;
        }
        return File.Exists(NewFilePath(id));
    }

    private string NewFilePath(string id)
    {
        return $"{_folder}/state_{id}.json";
    }
    #endregion

    #region 저장
    private void SaveSelected()
    {
        if (_selected == null)
        {
            SetStatus("선택된 파일이 없습니다", true);
            return;
        }
        if (SaveAsset(_selected))
            SetStatus($"'{FileLabel(_selected)}' 저장 완료 — {Path.GetFileName(_selected.FilePath)}");
    }

    private void SaveAll()
    {
        int saved = 0, failed = 0;
        for (int i = 0; i < _assets.Count; i++)
        {
            if (_assets[i].IsDirty == false)
                continue;
            if (SaveAsset(_assets[i]))
                saved++;
            else
                failed++;
        }

        if (saved == 0 && failed == 0)
            SetStatus("저장할 변경이 없습니다");
        else if (failed > 0)
            SetStatus($"{saved}개 저장, {failed}개 저장 안 함 — 검증 목록과 상태 표시 확인", true);
        else
            SetStatus($"{saved}개 저장 완료");
    }

    /// <summary>
    /// 순서: 검사(에러 있으면 거부) → 저장 경로 결정 → 디스크 변경 확인 → 쓰기 → 임포트.
    /// 에러가 있는 파일을 저장하지 않는 이유: 게임은 에러 난 키·규칙을 빼고 로드하므로, 저장하면 조용히 다르게 동작한다.
    /// </summary>
    private bool SaveAsset(StateDefinitionAsset asset)
    {
        RunValidationNow();
        if (asset.ErrorCount > 0)
        {
            SetStatus($"'{FileLabel(asset)}': 에러 {asset.ErrorCount}건이 있어 저장하지 않았습니다 — 아래 검증 목록 확인", true);
            return false;
        }

        bool isNew = string.IsNullOrEmpty(asset.FilePath);
        string path = isNew ? NewFilePath(asset.Set.Id) : asset.FilePath;

        if (isNew)
        {
            if (File.Exists(path))
            {
                SetStatus($"같은 이름의 파일이 이미 있습니다: {path} — 파일 id를 바꾸세요", true);
                return false;
            }
        }
        else if (File.Exists(path) && DiskChanged(path, asset.DiskText))
        {
            bool overwrite = EditorUtility.DisplayDialog("파일이 밖에서 바뀌었습니다",
                $"{path}\n\n툴이 불러온 뒤 다른 곳(텍스트 편집기, 버전 관리 등)에서 수정되었습니다.\n덮어쓰면 그 수정은 사라집니다.",
                "덮어쓰기", "취소");
            if (overwrite == false)
            {
                SetStatus("저장 취소 — [새로고침]으로 디스크 내용을 다시 불러올 수 있습니다", true);
                return false;
            }
        }

        string text = StateDefinitionLoader.Serialize(asset.Set);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text, new UTF8Encoding(asset.HasBom));
        }
        catch (Exception e)
        {
            SetStatus($"저장 실패: {e.Message}", true);
            return false;
        }

        asset.MarkSaved(path, text);
        AssetDatabase.ImportAsset(path); // TextAsset 갱신. JSON 임포트는 스크립트 컴파일(도메인 리로드)을 일으키지 않는다

        if (isNew && asset == _selected)
        {
            _selectedPath = path;
            RebuildCenter(); // 머리글의 파일 이름 갱신
            ApplyIssuesToViews();
        }
        _fileList?.RefreshItems();
        UpdateUnsavedFlag();
        return true;
    }

    private static bool DiskChanged(string path, string expected)
    {
        try
        {
            return ReadText(path, out _) != expected;
        }
        catch (IOException)
        {
            return true; // 읽지 못하면 바뀐 것으로 보고 사용자에게 묻는다
        }
    }

    /// <summary>창을 닫을 때 Unity의 "저장할까요?" 대화 상자에서 [저장]을 누르면 호출된다.</summary>
    public override void SaveChanges()
    {
        SaveAll();
        if (hasUnsavedChanges == false)
            base.SaveChanges(); // 에러 때문에 저장하지 못한 파일이 남으면 창을 닫지 않는다
    }

    public override void DiscardChanges()
    {
        LoadFolder();
        RefreshFileList();
        hasUnsavedChanges = false;
        base.DiscardChanges();
    }
    #endregion
}