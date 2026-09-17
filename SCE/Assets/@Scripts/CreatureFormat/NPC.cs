using Data;
using UnityEngine;
using static Define;

public class NPC : CreatureBase
{
    public NPCData Data { get; private set; }

    public override Vector2 LookDirection => throw new System.NotImplementedException();

    public override BaseCreatureAnim CreatureAnim => throw new System.NotImplementedException();

    public override void SetInfo(int objectID)
    {
        base.SetInfo(objectID);

        Data = GetCreatureData<NPCData>();
    }
}