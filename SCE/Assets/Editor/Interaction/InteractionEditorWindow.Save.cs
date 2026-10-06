using System;
using System.IO;
using System.Text;
using UnityEditor;

/// <summary>불러오기 · 새 Set · 저장.</summary>
public sealed partial class InteractionEditorWindow
{
    #region 불러오기
    /// <summary>
    /// 폴더 바로 아래의 JSON을 읽는다 (하위 폴더는 보지 않음).
    /// 읽지 못하거나 파싱에 실패한 파일은 숨기지 않고 "파싱 실패" 항목으로 보여 준다 — 게임에서도 그 Set은 로드되지 않기 때문.
    /// </summary>
    private void LoadFolder()
    {
        DisposeSelection();
        _selected = null;
        _selectedBroken = null;
        DestroyAssets();
        _brokenFiles.Clear();

        if (Directory.Exists(_folder) == false)
            return;

        string[] files = Directory.GetFiles(_folder, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < files.Length; i++)
        {
            string path = files[i].Replace('\\', '/');

            string text;
            bool hasBom;
            InteractionSetDefinition set;
            string error;
            try
            {
                text = ReadText(path, out hasBom);
                set = InteractionLoader.ParseRaw(text, out error);
            }
            catch (Exception e) // 읽기 실패, 또는 노드 변환기가 JsonException이 아닌 예외를 던진 경우
            {
                _brokenFiles.Add(new BrokenFile { FilePath = path, Error = e.Message });
                continue;
            }

            if (set == null)
            {
                _brokenFiles.Add(new BrokenFile { FilePath = path, Error = error ?? "빈 JSON" });
                continue;
            }

            if (string.IsNullOrEmpty(set.Id))
                set.Id = Path.GetFileNameWithoutExtension(path);

            _assets.Add(InteractionSetAsset.Create(set, path, text, hasBom));
        }
        _assets.Sort((a, b) => string.CompareOrdinal(a.Set.Id, b.Set.Id));
    }

    private string LoadSummary()
    {
        if (Directory.Exists(_folder) == false)
            return $"폴더가 없습니다: {_folder} — [폴더]로 지정하세요";

        string summary = $"Interaction Set {_assets.Count}개";
        if (_brokenFiles.Count > 0)
            summary += $", 파싱 실패 {_brokenFiles.Count}개";
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

    #region 새 Set
    private void CreateNewSet()
    {
        string id = "new_set";
        for (int n = 1; IdTaken(id); n++)
            id = $"new_set_{n}";

        var set = new InteractionSetDefinition { Id = id, Interactions = Array.Empty<InteractionDefinition>() };
        InteractionSetAsset asset = InteractionSetAsset.CreateNew(set);
        _assets.Add(asset);

        UpdateUnsavedFlag();
        RunValidationNow();
        SelectSet(asset);
        RefreshSetList();
        SetStatus($"새 Set '{id}' — 저장하면 {PathForId(id)}에 만들어집니다");
    }

    private bool IdTaken(string id)
    {
        for (int i = 0; i < _assets.Count; i++)
        {
            if (_assets[i].Set.Id == id)
                return true;
        }
        return File.Exists(PathForId(id));
    }

    /// <summary>파일 이름 = Set Id 규칙.</summary>
    private string PathForId(string id)
    {
        return $"{_folder}/{id}.json";
    }
    #endregion

    #region 저장
    private void SaveSelected()
    {
        if (_selected == null)
        {
            SetStatus("선택된 Set이 없습니다", true);
            return;
        }
        if (SaveAsset(_selected))
            SetStatus($"'{SetLabel(_selected)}' 저장 완료 — {Path.GetFileName(_selected.FilePath)}");
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
    /// 순서: 검사(에러 있으면 거부) → 저장 경로 결정 → 디스크 변경 확인 → (Set Id가 바뀌었으면) 파일 이름 변경 → 쓰기 → 임포트.
    /// 에러가 있으면 저장하지 않는다 — 런타임 Parse가 거부할 파일을 만들지 않기 위함.
    /// </summary>
    private bool SaveAsset(InteractionSetAsset asset)
    {
        RunValidationNow();
        if (asset.ErrorCount > 0)
        {
            SetStatus($"'{SetLabel(asset)}': 에러 {asset.ErrorCount}건이 있어 저장하지 않았습니다 — 아래 검증 목록 확인", true);
            return false;
        }

        string targetPath = PathForId(asset.Set.Id);
        bool isNew = string.IsNullOrEmpty(asset.FilePath);

        // Set Id를 바꿨으면 파일 이름도 따라 바꾼다.
        // 대소문자만 다른 경우는 같은 파일로 본다 — Windows·macOS의 기본 파일 시스템은 대소문자를 구분하지 않는다
        bool rename = isNew == false && string.Equals(asset.FilePath, targetPath, StringComparison.OrdinalIgnoreCase) == false;
        string path = isNew || rename ? targetPath : asset.FilePath;

        if ((isNew || rename) && File.Exists(targetPath))
        {
            SetStatus($"같은 이름의 파일이 이미 있습니다: {targetPath} — Set Id를 바꾸세요", true);
            return false;
        }

        if (isNew == false && File.Exists(asset.FilePath) && DiskChanged(asset.FilePath, asset.DiskText))
        {
            bool overwrite = EditorUtility.DisplayDialog("파일이 밖에서 바뀌었습니다",
                $"{asset.FilePath}\n\n툴이 불러온 뒤 다른 곳(텍스트 편집기, 버전 관리 등)에서 수정되었습니다.\n덮어쓰면 그 수정은 사라집니다.",
                "덮어쓰기", "취소");
            if (overwrite == false)
            {
                SetStatus("저장 취소 — [새로고침]으로 디스크 내용을 다시 불러올 수 있습니다", true);
                return false;
            }
        }

        if (rename && File.Exists(asset.FilePath))
        {
            // 예전 파일을 지우고 새로 만들면 .meta의 GUID가 바뀌어 Addressables 항목과 참조가 끊긴다 — 이름만 바꾼다
            string moveError = AssetDatabase.MoveAsset(asset.FilePath, targetPath);
            if (string.IsNullOrEmpty(moveError) == false)
            {
                SetStatus($"파일 이름 변경 실패: {moveError}", true);
                return false;
            }
            asset.FilePath = targetPath; // 이름은 이미 바뀌었다 — 아래 쓰기가 실패해도 경로는 맞게 둔다
        }

        string text;
        try
        {
            text = InteractionLoader.Serialize(asset.Set);
            Directory.CreateDirectory(_folder);
            File.WriteAllText(path, text, new UTF8Encoding(asset.HasBom));
        }
        catch (Exception e)
        {
            SetStatus($"저장 실패: {e.Message}", true);
            return false;
        }

        asset.MarkSaved(path, text);
        AssetDatabase.ImportAsset(path); // TextAsset 갱신. JSON 임포트는 스크립트 컴파일(도메인 리로드)을 일으키지 않는다

        if (asset == _selected)
        {
            _selectedPath = path;
            UpdateFileLabel();
        }
        _setList?.RefreshItems();
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
            base.SaveChanges(); // 에러 때문에 저장하지 못한 Set이 남으면 창을 닫지 않는다
    }

    public override void DiscardChanges()
    {
        LoadFolder();
        RefreshSetList();
        hasUnsavedChanges = false;
        base.DiscardChanges();
    }
    #endregion
}