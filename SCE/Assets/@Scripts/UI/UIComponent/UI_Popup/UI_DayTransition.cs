using UnityEngine;
using UnityEngine.EventSystems;

public class UI_DayTransition : UI_Popup
{
    private enum Buttons
    {
        GoToNextDay,
    }

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        BindButtons(typeof(Buttons));

        GetButton(Buttons.GoToNextDay).gameObject.BindEvent(OnClickNextButton, Define.UIEvent.Click);

        return true;
    }

    private void OnClickNextButton(PointerEventData evt)
    {
        Managers.GameState.Day.AdvanceDay();

        Managers.Stage.Begin(); // 재시작

        // Managers.Object.Spawn<NPC>(300, -Vector2.one); // 날이 바뀐 뒤에 spawn되어도 interaction on
    }
}
