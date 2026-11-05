using UnityEngine;
using static Define;

/// <summary>
/// 설정 (Popup). 돌아가기 / ESC 로 닫힘.
/// 볼륨·화면 모드·해상도 값 처리는 사운드/옵션 방식이 정해지면 연결.
/// </summary>
public class UI_Panel_Settings : UI_Popup
{
    private enum GameObjects
    {
        Btn_Back,
    }

    /// <summary>설정창이 떠 있는지 (일시정지가 ESC 를 양보할 때 씀)</summary>
    public static bool IsOpen { get; private set; }

    private void OnEnable()  { IsOpen = true; }
    private void OnDisable() { IsOpen = false; }

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        BindObjects(typeof(GameObjects));

        GameObject back = GetObject(GameObjects.Btn_Back);
        if (back != null) BindEvent(back, (evt) => Close());

        // TODO: Row_BGM / Row_SFX 볼륨 칸, Row_ScreenMode / Row_Resolution 좌우 화살표
        return true;
    }

    private void Update()
    {
        if (UIPanelInput.ConsumeEsc())
            Close();
    }

    private void Close()
    {
        Managers.UI.ClosePopupUI(this);
    }
}
