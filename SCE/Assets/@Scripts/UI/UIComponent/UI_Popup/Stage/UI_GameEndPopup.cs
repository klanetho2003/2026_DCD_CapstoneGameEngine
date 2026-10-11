using System;
using static Define;

/// <summary>게임 종료 UI Popup. 버튼을 누르면 닫히고 Stage 선택으로 돌아간다.</summary>
public class UI_GameEndPopup : UI_ConfirmPopup
{
    private const string TITLE_CLEARED = "게임 종료 — 클리어";
    private const string TITLE_FAILED = "게임 종료 — 실패";

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

    public void SetInfo(EStageResult result, Action onConfirmed)
    {
        Init();

        GetText(Texts.Text_Title).text = result == EStageResult.Cleared ? TITLE_CLEARED : TITLE_FAILED;
        Open(onConfirmed);
    }
}