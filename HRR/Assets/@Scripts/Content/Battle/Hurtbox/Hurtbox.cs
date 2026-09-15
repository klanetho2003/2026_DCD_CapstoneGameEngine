using UnityEditor;
using UnityEngine;
using static Define;
using static UnityEngine.UI.GridLayoutGroup;

[RequireComponent(typeof(Collider2D))]
public class Hurtbox : InitBase
{
    public Collider2D Collider { get; private set; }

    private HurtboxGroup _hurtboxGroup;
    public CombatCreature Owner { get { return _hurtboxGroup.Owner; } }
    //public int Priority = 0; // { get { return _priority; } }
    public int Priority { get; private set; } = 0; // { get { return _priority; } }

    public override bool Init()
    {
        if (base.Init() == false)
            return false;

        Collider = GetComponent<Collider2D>();
        if (Collider != null)
            Collider.isTrigger = true;

        return true;
    }

    public void SetInfo(HurtboxGroup group)
    {
        _hurtboxGroup = group;

        this.gameObject.layer = (Owner.CreatureData.creatureType == EObjectType.Villager)
                ? (int)ELayer.Player_Hurtbox
                : (int)ELayer.Monster_Hurtbox;
    }

    /// <summary>
    /// Hitbox.OnTriggerEnter2D에서 호출. 실질적인 Damage 적용 시작 지점
    /// </summary>
    public void OnHit(DamageInfo damageInfo)
    {
        if (Owner == null)
            return;

        Owner.OnDamaged(damageInfo);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        var col = GetComponent<Collider2D>();
        if (col == null)
            return;

        Gizmos.color = new Color(0.5f, 1f, 0.5f, 0.4f); // green

        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = col.transform.localToWorldMatrix;

        if (col is BoxCollider2D box)
        {
            Gizmos.DrawCube(box.offset, box.size);

            Gizmos.color = new Color(Gizmos.color.r, Gizmos.color.g, Gizmos.color.b, 1f);
            Gizmos.DrawWireCube(box.offset, box.size);
        }
        else if (col is CircleCollider2D sphere)
        {
            Gizmos.DrawSphere(sphere.offset, sphere.radius);
        }

        Gizmos.matrix = oldMatrix;
    }
#endif
}
