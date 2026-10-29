using System;
using static Define;

/// <summary>게임 종료 UI Popup. 버튼을 누르면 닫히고 Stage 선택으로 돌아간다.</summary>
public class UI_GameEndPopup : UI_ConfirmPopup
{
    /// <summary>기존 스테이지 완료 판정에 따라 표시할 결과 제목이다.</summary>
    private const string TITLE_CLEARED = "게임 종료 — 클리어";
    /// <summary>플레이어 사망 등으로 스테이지가 실패했을 때 표시할 제목이다.</summary>
    private const string TITLE_FAILED = "게임 종료 — 실패";

    /// <summary>프리팹에서 연결할 최종 결과 제목 텍스트의 이름이다.</summary>
    private enum Texts
    {
        /// <summary>최종 결과와 누적 점령 점수를 표시할 제목 텍스트다.</summary>
        Text_Title,
    }

    /// <summary>기본 확인 팝업을 초기화하고 최종 결과 제목을 연결한다.</summary>
    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        BindTexts(typeof(Texts));
        return true;
    }

    /// <summary>기존 승패 결과와 정산 완료한 라운드의 누적 점령 점수를 표시한다.</summary>
    /// <param name="result">기존 스테이지 흐름에서 확정한 완료 또는 실패 결과다.</param>
    /// <param name="onConfirmed">스테이지 선택으로 돌아갈 확인 콜백이다.</param>
    /// <param name="totalPlayerTiles">정상 정산을 마친 라운드들의 누적 플레이어 점수다.</param>
    /// <param name="completedRoundCount">누적 점수에 반영한 정산 완료 라운드 수다.</param>
    public void SetInfo(EStageResult result, Action onConfirmed,
        int totalPlayerTiles = 0, int completedRoundCount = 0)
    {
        Init();

        // title은 기존 팝업의 영역 안에서 최종 점수를 함께 표시할 텍스트다.
        TMPro.TMP_Text title = GetText(Texts.Text_Title);
        title.enableAutoSizing = true;
        title.fontSizeMin = 18f;
        title.fontSizeMax = 36f;
        title.text = $"{(result == EStageResult.Cleared ? TITLE_CLEARED : TITLE_FAILED)}\n" +
            $"정산 완료 {completedRoundCount}라운드\n누적 {totalPlayerTiles}점";
        Open(onConfirmed);
    }
}
