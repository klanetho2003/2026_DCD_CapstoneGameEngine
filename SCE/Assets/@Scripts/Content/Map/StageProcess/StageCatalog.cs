using System.Collections.Generic;
using Data;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// 선택 가능한 Stage 목록. Stage 선택 UI가 읽는다.
/// 한 행 = Stage Prefab 1개. 목록의 행 수가 곧 선택 UI의 버튼 수다.
/// 표시 순서는 JSON에 적힌 순서를 따른다.
/// </summary>
public sealed class StageCatalog
{
    private const string CATALOG_KEY = "StageCatalog";

    private readonly List<StageInfoData> _stages = new();
    private readonly List<string> _errors = new();

    public IReadOnlyList<StageInfoData> Stages { get { return _stages; } }
    public bool IsLoaded { get; private set; }

    public bool Load()
    {
        IsLoaded = false;
        _stages.Clear();

        TextAsset json = Managers.Resource.Load<TextAsset>(CATALOG_KEY);
        if (json == null)
        {
            LogPrinter.LogError($"[StageCatalog] 목록 로드 실패: {CATALOG_KEY}. Addressable 등록 확인.");
            return false;
        }

        _errors.Clear();
        IsLoaded = TryParse(json.text, _stages, _errors);
        for (int i = 0; i < _errors.Count; i++)
            LogPrinter.LogError($"[StageCatalog] {_errors[i]}");
        _errors.Clear();

        if (IsLoaded)
            LogPrinter.Log($"[StageCatalog] Stage {_stages.Count}개 로드");
        return IsLoaded;
    }

    /// <summary>
    /// JSON 문자열 → 목록. Managers와 로그를 쓰지 않는다 — EditMode 테스트 대상.
    /// 잘못된 행은 errors에 사유를 남기고 제외한다. 유효한 행이 하나도 없으면 false.
    /// </summary>
    public static bool TryParse(string json, List<StageInfoData> into, List<string> errors)
    {
        into.Clear();

        if (string.IsNullOrWhiteSpace(json))
        {
            errors.Add("목록 JSON이 비어 있음");
            return false;
        }

        StageCatalogData data;
        try
        {
            data = JsonConvert.DeserializeObject<StageCatalogData>(json);
        }
        catch (JsonException e) // 형식 오류 — 예외를 밖으로 내보내지 않고 실패로 돌려준다
        {
            errors.Add($"목록 JSON 형식 오류: {e.Message}");
            return false;
        }

        if (data == null || data.Stages == null)
        {
            errors.Add("목록 JSON에 Stages가 없음");
            return false;
        }

        for (int i = 0; i < data.Stages.Count; i++)
        {
            StageInfoData info = data.Stages[i];
            if (info == null)
            {
                errors.Add($"Stages[{i}]: 빈 항목 — 제외");
                continue;
            }
            if (string.IsNullOrEmpty(info.MapPrefabKey) || string.IsNullOrEmpty(info.MapDataKey))
            {
                errors.Add($"Stages[{i}](StageId={info.StageId}): MapPrefabKey 또는 MapDataKey가 비어 있음 — 제외");
                continue;
            }
            if (ContainsStageId(into, info.StageId))
            {
                errors.Add($"Stages[{i}]: StageId {info.StageId} 중복 — 뒤의 것 제외");
                continue;
            }

            if (string.IsNullOrEmpty(info.DisplayName))
                info.DisplayName = info.MapDataKey;

            into.Add(info);
        }

        if (into.Count == 0)
        {
            errors.Add("유효한 Stage가 하나도 없음");
            return false;
        }
        return true;
    }

    /// <summary>선형 탐색. 전체로는 O(n²)이지만 n이 수십 개 수준이라 HashSet을 할당하는 것보다 싸다.</summary>
    private static bool ContainsStageId(List<StageInfoData> stages, int stageId)
    {
        for (int i = 0; i < stages.Count; i++)
        {
            if (stages[i].StageId == stageId)
                return true;
        }
        return false;
    }
}