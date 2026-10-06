using Data;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Define;

public class GameScene : BaseScene
{
    private void Start()
    {
        if (Managers.Data.IsDataLoaded == false)
        {
            Managers.Scene.LoadScene(EScene.TitleScene);
            return;
        }

        Managers.Map.LoadMap("DevMapData");
        Managers.Stage.Begin();

        // Managers.UI.ShowPopupUI<UI_DayTransition>();
        /*var handle = Managers.UI.ShowPopupUI<UI_FocusGroup>();
        handle.SetInfo();
        handle.OnClosed();
        Managers.UI.ClosePopupUI();*/

        /*for (int i = 0; i < 11; i++)
        {
            var mosnter = Managers.Object.Spawn<MonsterBase>(0, new Vector2(i+5, 0)); // test monster
        }*/
    }

    public override void Clear()
    {
        
    }
}
