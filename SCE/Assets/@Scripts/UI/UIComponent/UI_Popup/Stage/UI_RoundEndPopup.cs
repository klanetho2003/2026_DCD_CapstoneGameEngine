using System;
using static Define;

/// <summary>라운드 종료 UI Popup. 버튼을 누르면 닫히고 다음 라운드로 넘어간다 (무엇이 일어날지는 StageManager가 정한다).</summary>
public class UI_RoundEndPopup : UI_ConfirmPopup
{
    /// <summary>프리팹에서 연결할 결과 제목 텍스트의 이름이다.</summary>
    private enum Texts
    {
        /// <summary>라운드 종료와 점령 점수를 표시할 제목 텍스트다.</summary>
        Text_Title,
    }

    /// <summary>기본 확인 팝업을 초기화하고 결과 제목을 연결한다.</summary>
    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        BindTexts(typeof(Texts));
        return true;
    }

    /// <summary>이번 라운드의 소유 타일 수와 누적 점수를 기존 결과 팝업에 표시한다.</summary>
    /// <param name="endedRound">정산을 마친 라운드다.</param>
    /// <param name="onConfirmed">다음 라운드 진행을 요청할 확인 콜백이다.</param>
    /// <param name="roundPlayerTiles">라운드 종료 순간 플레이어의 소유 타일 수다.</param>
    /// <param name="totalPlayerTiles">정산한 라운드들의 누적 플레이어 점수다.</param>
    public void SetInfo(ERound endedRound, Action onConfirmed,
        int roundPlayerTiles = 0, int totalPlayerTiles = 0)
    {
        Init();

        // title은 기존 팝업의 정해진 영역 안에서 점수를 함께 표시할 텍스트다.
        TMPro.TMP_Text title = GetText(Texts.Text_Title);
        title.enableAutoSizing = true;
        title.fontSizeMin = 18f;
        title.fontSizeMax = 36f;
        title.text = $"라운드 {endedRound} 종료\n점령 {roundPlayerTiles}칸\n누적 {totalPlayerTiles}점";
        Open(onConfirmed);
    }
}
