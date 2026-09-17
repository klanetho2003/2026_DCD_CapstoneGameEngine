using Data;
using System.Collections;
using UnityEngine;

public class ActivableHitbox : HitboxBase
{
    [Header("Identification")]
    [Tooltip("Animation Event에서 어떤 Hitbox를 켜고 끌지 식별. 동일 캐릭터에 여러 Hitbox 있을 때 사용.")]
    [SerializeField]
    private int _hitboxId = 0;
    public int HitboxId { get { return _hitboxId; } }

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        _collider.enabled = false;

        return true;
    }
}
