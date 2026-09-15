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

        var player = Managers.Object.Spawn<Villager>(10); // TempVillager
        Managers.Object.SetPossession(player);  // Set Possess

        var testNPC = Managers.Object.Spawn<NPC>(11); // test npc

        var handle = Managers.UI.ShowPopupUI<UI_FocusGroup>();
        handle.SetInfo();
        handle.OnClosed();
        Managers.UI.ClosePopupUI();

        /*var handle = Managers.UI.ShowSceneUI<UI_GameScene>();
        handle.SetInfo();*/
    }

    public override void Clear()
    {
        
    }
}
