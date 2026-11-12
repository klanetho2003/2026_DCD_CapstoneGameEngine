using Data;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Stage → 행성 번호. 행성 선택 화면과 같은 규칙: StageCatalog 목록 순서 = 수성, 금성, 지구, 화성, 목성, 토성.
/// HUD(왼쪽 위 아이콘·이름), 스테이지 시작 화면·CLEAR(고양이 아래 큰 행성)가 같이 쓴다.
/// </summary>
public static class StagePlanet
{
    public const int Count = 6;

    /// <summary>목록에서 이 Stage 의 순서. 못 찾으면 -1.</summary>
    public static int IndexOf(StageInfoData info)
    {
        if (info == null)
        {
            LogPrinter.LogWarning("[StagePlanet] Stage 정보 없음");
            return -1;
        }

        var catalog = new StageCatalog();
        if (catalog.Load() == false || catalog.Stages == null)
        {
            LogPrinter.LogWarning("[StagePlanet] Stage 목록을 불러오지 못함");
            return -1;
        }

        for (int i = 0; i < catalog.Stages.Count; i++)
        {
            if (catalog.Stages[i].StageId == info.StageId)
                return i;
        }

        LogPrinter.LogWarning($"[StagePlanet] 목록에 없는 Stage {info.StageId}");
        return -1;
    }

    /// <summary>지금 진행 중인 Stage 의 행성 번호</summary>
    public static int Current()
    {
        return IndexOf(Managers.Stage != null ? Managers.Stage.CurrentStageInfo : null);
    }

    /// <summary>
    /// image 를 sprites[index] 로 바꾸고 도트 1배 크기로. 바꿨으면 true.
    /// 못 바꾸면 이유를 경고로 남긴다 (어디서 막혔는지 콘솔에서 바로 보이게).
    /// </summary>
    public static bool Apply(Image image, Sprite[] sprites, int index, string label)
    {
        if (image == null) { LogPrinter.LogWarning($"[StagePlanet] {label}: Image 없음"); return false; }
        if (sprites == null || sprites.Length == 0) { LogPrinter.LogWarning($"[StagePlanet] {label}: 이미지 목록이 비어 있음"); return false; }
        if (index < 0 || index >= sprites.Length) { LogPrinter.LogWarning($"[StagePlanet] {label}: 행성 번호 {index} 가 목록({sprites.Length}) 밖"); return false; }
        if (sprites[index] == null) { LogPrinter.LogWarning($"[StagePlanet] {label}: {index}번 이미지가 비어 있음"); return false; }

        image.sprite = sprites[index];
        image.SetNativeSize();
        return true;
    }
}
