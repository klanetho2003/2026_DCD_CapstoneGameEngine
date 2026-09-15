using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static Define;

public class TitleScene : BaseScene
{
    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        SceneType = EScene.TitleScene;

        //Managers.Pause.SetPause(false);

        return true;
    }

    public override void Clear()
    {
        
    }
}
