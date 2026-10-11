using System;
using static Define;

/// <summary>라운드 종료 UI Popup. 버튼을 누르면 닫히고 다음 라운드로 넘어간다 (무엇이 일어날지는 StageManager가 정한다).</summary>
public class UI_RoundEndPopup : UI_ConfirmPopup
{
    private enum Texts
    {
        Text_Title,
    }

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        BindTexts(typeof(Texts));
        return true;
    }

    public void SetInfo(ERound endedRound, Action onConfirmed)
    {
        Init();

        GetText(Texts.Text_Title).text = $"라운드 {endedRound} 종료";
        Open(onConfirmed);
    }
}