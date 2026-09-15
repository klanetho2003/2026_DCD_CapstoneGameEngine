using UnityEngine;

/// <summary>
/// Hitbox test용 임시 객체
/// </summary>
public class Hitbox_temp : HitboxBase
{
    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        return true;
    }
}
