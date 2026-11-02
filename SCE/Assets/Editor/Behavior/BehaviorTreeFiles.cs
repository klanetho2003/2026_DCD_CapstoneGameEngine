using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

/// <summary>
/// 트리 파일 · 배치 파일 읽기 · 쓰기 — Behavior Tree Editor의 불러오기 · 저장에서 파일을 다루는 부분 (창은 확인 · 상태 줄 · 화면만). 상태를 갖지 않는다.
///  - 배치 파일: 트리 파일마다 하나 (BehaviorTreeLayoutFile.PathFor) — 불러올 때 읽어 입히고 (없으면 빈 배치), 저장할 때 트리 파일과 함께 쓴다
///  - 이름 바꾸기: 트리 Id가 파일 이름과 다르면 저장할 때 트리 파일 · 배치 파일을 옮긴다 (AssetDatabase.MoveAsset — GUID 그대로라 Addressable 항목이 따라온다).
///    대소문자만 다르면 다른 이름을 한 번 거친다 (대소문자를 가리지 않는 파일 시스템은 바로 바꾸면 같은 파일로 본다).
///    새 이름 자리에 이미 배치 파일이 있으면 (주인 트리 파일이 없는 옛 배치 — 저장 전에 덮어쓸지 묻는다. 트리 파일 자리는 부르는 쪽이 막는다) 그것을 휴지통으로 보내고 옮긴다.
///    트리 · 배치 파일을 함께 옮긴 뒤에 쓴다 — 쓰기가 실패해도 두 파일이 같은 이름에 짝으로 남게
/// </summary>
public static class BehaviorTreeFiles
{
    #region 불러오기
    /// <summary>
    /// 사본의 배치 파일을 읽어 입힌다 (불러온 직후). 없으면 빈 배치 그대로 — 처음 보일 때 정리 배치.
    /// 읽지 못하면 배치 없이 둔 채 까닭을 남긴다 (검사 목록 경고 · 저장할 때 덮어쓸지 묻기). O(배치 파일 크기)
    /// </summary>
    public static void LoadLayout(BehaviorTreeDocumentAsset document)
    {
        if (string.IsNullOrEmpty(document.FilePath))
            return;
        string path = BehaviorTreeLayoutFile.PathFor(document.FilePath);
        if (File.Exists(path) == false)
            return;

        string text;
        try
        {
            text = EditorJsonFile.ReadText(path, out _);
        }
        catch (Exception e) // IO · 권한 · 경로 형식 — 어느 것이든 읽기 실패
        {
            document.MarkLayoutUnreadable(e.Message, null);
            return;
        }

        if (BehaviorTreeLayoutFile.TryRead(text, document.Registry, out BehaviorTreeLayoutData layout, out string error))
            document.ApplyLoadedLayout(layout, text);
        else
            document.MarkLayoutUnreadable(error, text);
    }
    #endregion

    #region 저장
    /// <summary>
    /// 저장하면 덮어쓰게 되는 파일과 까닭 ("경로 — 까닭") — 불러온 뒤 밖에서 바뀐 트리 파일 · 배치 파일 (배치 파일은 생긴 것도), 읽지 못한 배치 파일, 새 이름 자리에 이미 있는 배치 파일.
    /// 없으면 빈 목록. 부르는 쪽이 묻는다. 트리 파일 자리의 다른 파일은 부르는 쪽이 먼저 막는다 (plan.WritesNewFile). O(파일 크기)
    /// </summary>
    public static List<string> OverwriteWarnings(BehaviorTreeDocumentAsset document, in JsonSavePlan plan)
    {
        var warnings = new List<string>();
        if (plan.IsNew == false && File.Exists(document.FilePath) && EditorJsonFile.DiskChanged(document.FilePath, document.DiskText))
            warnings.Add($"{document.FilePath} — 불러온 뒤 다른 곳 (텍스트 편집기 · 버전 관리 등)에서 바뀜");

        string layoutPath = BehaviorTreeLayoutFile.PathFor(plan.IsNew ? plan.TargetPath : document.FilePath);
        if (document.LayoutError != null && File.Exists(layoutPath))
            warnings.Add($"{layoutPath} — 읽지 못한 배치 파일 ({document.LayoutError})");
        else if (LayoutChangedOnDisk(document, layoutPath))
            warnings.Add($"{layoutPath} — 불러온 뒤 다른 곳에서 바뀌거나 생김");

        if (plan.WritesNewFile)
        {
            string targetLayout = BehaviorTreeLayoutFile.PathFor(plan.TargetPath);
            if (string.Equals(targetLayout, layoutPath, StringComparison.OrdinalIgnoreCase) == false && File.Exists(targetLayout))
                warnings.Add($"{targetLayout} — 새 이름 자리에 이미 있는 배치 파일 (트리 파일이 없는 옛 배치 — 휴지통으로)");
        }
        return warnings;
    }

    /// <summary>배치 파일이 불러온 (저장한) 뒤 밖에서 바뀌었거나, 없던 파일이 생겼다. 지워진 것은 바뀐 것으로 보지 않는다 (덮어쓸 것이 없다)</summary>
    private static bool LayoutChangedOnDisk(BehaviorTreeDocumentAsset document, string path)
    {
        if (File.Exists(path) == false)
            return false;
        return document.LayoutDiskText == null || EditorJsonFile.DiskChanged(path, document.LayoutDiskText);
    }

    /// <summary>
    /// 쓴다 — 이름 바꾸기 (트리 파일 → 배치 파일) → 트리 파일 → 배치 파일. 쓴 트리 파일 경로는 treePath, 배치 파일 경로는 layoutPath.
    /// 트리 파일을 쓰지 못하면 그 까닭을 반환한다 (이름 바꾸기 뒤 실패해도 사본의 경로는 파일이 있는 곳으로 둔다 — 다음 저장이 거기서 이어 간다).
    /// 배치 파일만 실패하면 layoutError에 까닭 (트리는 저장됨 — null 반환). 배치 파일 이름 바꾸기 문제는 notice에 (" · …", 없으면 "") — 새 이름으로 쓰므로 저장은 된다.
    /// </summary>
    public static string Write(BehaviorTreeDocumentAsset document, in JsonSavePlan plan, string treeText, string layoutText,
                               out string treePath, out string layoutPath, out string layoutError, out string notice)
    {
        layoutError = null;
        notice = "";
        string oldTreePath = document.FilePath;
        string oldLayoutPath = string.IsNullOrEmpty(oldTreePath) ? null : BehaviorTreeLayoutFile.PathFor(oldTreePath);
        treePath = plan.CaseOnlyRename ? plan.TargetPath : plan.WritePath;
        layoutPath = BehaviorTreeLayoutFile.PathFor(treePath);

        if ((plan.Rename || plan.CaseOnlyRename) && File.Exists(oldTreePath))
        {
            // 예전 파일을 지우고 새로 만들면 .meta의 GUID가 바뀌어 Addressables 항목과 참조가 끊긴다 — 이름만 바꾼다 (주소는 저장 뒤 등록 맞추기가 새 Id로)
            string moveError;
            string landed;
            if (plan.Rename)
            {
                moveError = AssetDatabase.MoveAsset(oldTreePath, plan.TargetPath);
                landed = string.IsNullOrEmpty(moveError) ? plan.TargetPath : oldTreePath;
            }
            else
            {
                moveError = MoveCaseOnly(oldTreePath, plan.TargetPath, out landed);
            }
            document.FilePath = landed; // 파일이 있는 곳 — 아래 쓰기가 실패해도 경로는 맞게 둔다
            if (string.IsNullOrEmpty(moveError) == false)
                return $"파일 이름 바꾸기 실패: {moveError}";
        }
        if (oldLayoutPath != null && string.Equals(oldLayoutPath, layoutPath, StringComparison.Ordinal) == false && File.Exists(oldLayoutPath))
            notice = MoveLayout(oldLayoutPath, layoutPath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(treePath) ?? "");
            EditorJsonFile.WriteText(treePath, treeText, document.HasBom);
        }
        catch (Exception e)
        {
            return $"저장 실패: {e.Message}";
        }

        try
        {
            EnsureFolder(Path.GetDirectoryName(layoutPath)?.Replace('\\', '/'));
            EditorJsonFile.WriteText(layoutPath, layoutText, false);
        }
        catch (Exception e)
        {
            layoutError = e.Message;
        }
        return null;
    }

    /// <summary>
    /// 배치 파일을 새 이름으로 (GUID 그대로) — 대소문자만 다르면 다른 이름을 거친다. 새 이름 자리에 파일이 있으면 (트리 파일이 없는 옛 배치 — 덮어쓸지는 저장 전에 물었다)
    /// 그것을 휴지통으로 보내고 옮긴다. 문제는 " · …" (없으면 "") — 옮기지 못해도 새 이름으로 쓰므로 저장은 된다.
    /// </summary>
    private static string MoveLayout(string from, string to)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            string caseError = MoveCaseOnly(from, to, out _);
            return string.IsNullOrEmpty(caseError) ? "" : $" · 배치 파일 이름 바꾸기 실패 ({caseError}) — 새 이름으로 씀";
        }
        if (File.Exists(to) && AssetDatabase.MoveAssetToTrash(to) == false)
            return $" · 새 이름 자리의 옛 배치 파일을 휴지통으로 옮기지 못함 ({to}) — 그 파일에 쓰고 옛 파일 {from}은 남음";

        string error = AssetDatabase.MoveAsset(from, to);
        return string.IsNullOrEmpty(error) ? "" : $" · 배치 파일 이름 바꾸기 실패 ({error}) — 새 이름으로 새로 쓰고 옛 파일 {from}은 남음";
    }

    /// <summary>
    /// 대소문자만 다른 이름으로 옮긴다 — 다른 이름 (같은 폴더 · 겹치지 않는 이름)을 한 번 거친다 (GUID 그대로). 성공하면 "", 실패하면 까닭 —
    /// 두 번째에서 실패하면 원래 이름으로 다시 옮긴다. landed = 끝난 뒤 파일이 있는 경로 (to · from, 원래 이름으로도 옮기지 못했으면 거친 이름).
    /// </summary>
    public static string MoveCaseOnly(string from, string to, out string landed)
    {
        landed = from;
        string folder = (Path.GetDirectoryName(to) ?? "").Replace('\\', '/');
        string between = $"{folder}/{Path.GetFileNameWithoutExtension(to)}_{Guid.NewGuid():N}{Path.GetExtension(to)}";
        string error = AssetDatabase.MoveAsset(from, between);
        if (string.IsNullOrEmpty(error) == false)
            return error;
        error = AssetDatabase.MoveAsset(between, to);
        if (string.IsNullOrEmpty(error))
        {
            landed = to;
            return "";
        }
        if (string.IsNullOrEmpty(AssetDatabase.MoveAsset(between, from)))
            return error;
        landed = between;
        return $"{error} — 파일이 {between}에 남음";
    }

    /// <summary>
    /// 폴더가 있게 한다 — Asset Database가 아는 폴더면 그대로, 디스크에 없고 위 폴더를 Asset Database가 알면 AssetDatabase.CreateFolder (meta와 함께),
    /// 아니면 디스크에만 만든다 (파일 임포트 · 다음 새로 고침 때 Unity가 안다)
    /// </summary>
    private static void EnsureFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
            return;
        string parent = (Path.GetDirectoryName(folder) ?? "").Replace('\\', '/');
        if (Directory.Exists(folder) == false && AssetDatabase.IsValidFolder(parent)
            && string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(folder))) == false)
            return;
        Directory.CreateDirectory(folder);
    }
    #endregion
}