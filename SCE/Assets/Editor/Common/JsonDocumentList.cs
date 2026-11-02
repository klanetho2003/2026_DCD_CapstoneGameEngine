using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 에디터 툴 공용 — 창이 가진 편집 사본 목록(JsonDocumentAsset)의 일: 미저장 수 · 다시 판정 · 지우기 · 창을 다시 만들 때 넘기는 편집(담기 · 입히기).
/// 상태를 갖지 않는다 — 목록은 창의 필드다 (사본 형식이 창마다 다르다).
/// </summary>
public static class JsonDocumentList
{
    /// <summary>같은 파일의 사본이 없는 편집으로 새 사본을 만든다 (저장 전 새 문서 — 있던 파일의 경로를 이어 갈지는 만드는 쪽이 정한다). 읽을 수 없으면 null과 이유.</summary>
    public delegate T UnsavedFactory<T>(JsonDocumentAsset.UnsavedEdit edit, out string error) where T : JsonDocumentAsset;

    /// <summary>
    /// 저장하지 않은 사본 수. losableOnly면 버리면 잃는 편집만 — 불러올 때 채운 값뿐인 사본(HasOnlyLoadFill)은 빼고 센다 ('버릴까요' 대화 · 다시 읽기 판단).
    /// `*` · 창을 닫을 때의 대화(hasUnsavedChanges)는 모두 센다. O(사본 수) — losableOnly면 채운 사본마다 JSON을 한 번 만든다.
    /// </summary>
    public static int CountDirty(IReadOnlyList<JsonDocumentAsset> assets, bool losableOnly)
    {
        int count = 0;
        for (int i = 0; i < assets.Count; i++)
        {
            JsonDocumentAsset asset = assets[i];
            if (asset != null && asset.IsDirty && (losableOnly == false || asset.HasOnlyLoadFill() == false))
                count++;
        }
        return count;
    }

    /// <summary>
    /// 사본마다 미저장 판정을 다시 한다. 판정이 바뀐 사본이 있으면 true — 부르는 쪽이 창의 미저장 표시를 맞춘다.
    /// 캐시(IsDirty)는 값 추적 알림 때 바뀌는데, 누르는 순간 확정된 값의 알림은 아직 오지 않았을 수 있다 — [모두 저장]이 거르기 전에 부른다. O(모든 사본의 내용 크기)
    /// </summary>
    public static bool RefreshDirty(IReadOnlyList<JsonDocumentAsset> assets)
    {
        bool changed = false;
        for (int i = 0; i < assets.Count; i++)
        {
            if (assets[i] != null)
                changed |= assets[i].RefreshDirty();
        }
        return changed;
    }

    /// <summary>사본을 모두 지우고 목록을 비운다.</summary>
    public static void DestroyAll<T>(List<T> assets) where T : JsonDocumentAsset
    {
        for (int i = 0; i < assets.Count; i++)
        {
            if (assets[i] == null)
                continue;
            Undo.ClearUndo(assets[i]); // 사라질 사본을 가리키는 Undo 기록 정리
            UnityEngine.Object.DestroyImmediate(assets[i]);
        }
        assets.Clear();
    }

    /// <summary>
    /// 저장하지 않은 편집이 있는 사본마다 담는다 (edits는 먼저 비운다). 한 사본에서 예외가 나도 나머지는 담는다 — 예외는 Console에 남기고 밖으로 내지 않는다 (직렬화 콜백 · 리로드가 멈추지 않게).
    /// O(모든 사본의 내용 크기) — 사본마다 JSON을 한 번 만든다.
    /// </summary>
    public static void CaptureUnsaved(IReadOnlyList<JsonDocumentAsset> assets, List<JsonDocumentAsset.UnsavedEdit> edits)
    {
        edits.Clear();
        for (int i = 0; i < assets.Count; i++)
        {
            if (assets[i] == null)
                continue;
            try
            {
                JsonDocumentAsset.UnsavedEdit edit = assets[i].CaptureUnsaved();
                if (edit != null)
                    edits.Add(edit);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }

    /// <summary>
    /// 다시 만든 뒤 (다시 읽은 다음): 담아 둔 편집을 입히고 edits를 비운다. 되살린 수를 반환한다. 저장하기 전까지 디스크는 건드리지 않는다.
    /// 같은 파일의 사본이 있으면 그 사본에(TryApplyUnsaved), 없으면(새 문서 · 그 사이 파일이 없어지거나 읽을 수 없게 됨) create로 만들어 목록 끝에 더한다.
    /// 다시 읽은 사본이 이미 같은 편집은 세지 않는다 (그 사이 파일이 그 편집과 같아짐 등).
    /// 지금 코드로 읽을 수 없는 편집은 이름(UnsavedEdit.SetId)을 unrestored에 더하고, 내용(JSON · 편집기 상태)을 Console에 남긴다 — 손으로 옮길 수 있게 (머리는 [toolName]).
    /// 일부만 입힌 편집 (입히고 이유도 준 편집 — Behavior Tree Editor의 배치를 읽지 못함 등)은 되살린 수에 세고, 이름 뒤에 " (일부)"를 붙여 unrestored에도 더한다 (내용은 Console).
    /// O(편집 수 × 사본 수 + 입힌 내용 크기)
    /// </summary>
    public static int RestoreUnsaved<T>(List<T> assets, List<JsonDocumentAsset.UnsavedEdit> edits, UnsavedFactory<T> create, List<string> unrestored, string toolName)
        where T : JsonDocumentAsset
    {
        int restoredCount = 0;
        for (int i = 0; i < edits.Count; i++)
        {
            JsonDocumentAsset.UnsavedEdit edit = edits[i];
            T loaded = string.IsNullOrEmpty(edit.FilePath) ? null : assets.Find(a => a.FilePath == edit.FilePath);

            string error;
            bool restored;
            bool changed = true;
            if (loaded != null)
            {
                restored = loaded.TryApplyUnsaved(edit, out changed, out error);
            }
            else
            {
                T created = create(edit, out error);
                restored = created != null;
                if (restored)
                    assets.Add(created);
            }

            if (restored && changed)
                restoredCount++; // 다시 읽은 사본이 이미 같았으면 세지 않는다 — 되살린 것이 아니다
            if (restored && error == null)
                continue;
            unrestored.Add(restored ? edit.SetId + " (일부)" : edit.SetId);
            string editorState = string.IsNullOrEmpty(edit.EditorJson) ? "" : $"\n담아 둔 편집기 상태:\n{edit.EditorJson}";
            Debug.LogWarning($"[{toolName}] 저장하지 않은 편집을 되살리지 못함 — '{edit.SetId}' ({error}). 담아 둔 내용:\n{edit.Json}{editorState}");
        }
        edits.Clear();
        return restoredCount;
    }
}