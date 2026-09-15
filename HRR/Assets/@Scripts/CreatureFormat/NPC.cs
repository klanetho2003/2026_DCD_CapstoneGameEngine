using Data;
using UnityEngine;
using static Define;

public class NPC : CreatureBase
{
    public NPCData Data { get; private set; }
    public NpcInteractionComponent Interaction { get; private set; }

    public override Vector3 LookDirection => throw new System.NotImplementedException();

    public override void SetInfo(int objectID)
    {
        base.SetInfo(objectID);

        if (CreatureData is NPCData data)
            Data = data;

        // null일 경우 생성해서 할당
        Interaction ??= new NpcInteractionComponent();

        InteractionSetDefinition set = null;
        if (string.IsNullOrEmpty(Data.InteractionSetId) == false)
        {
            set = Managers.Interaction.GetSet(Data.InteractionSetId);
            if (set == null)
                LogPrinter.LogError($"[NPC] InteractionSet 없음 >> {Data.InteractionSetId} ({gameObject.name})");
        }

        Interaction.SetInfo(this, set);
    }

    // 프로젝트의 디스폰 훅(풀 반환 함수)이 따로 있으면 거기서 Interaction.Clear()를 호출하고 아래는 안전망으로 남긴다.
    protected virtual void OnDisable()
    {
        Interaction?.Clear();
    }

    #region Trigger Method
    private void OnTriggerEnter2D(Collider2D other)
    {
        CreatureBase target = Managers.Object.PossessedTarget;
        if (target == null || other.gameObject != target.gameObject)
            return;

        Interaction?.OnZoneEnter(target);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        CreatureBase target = Managers.Object.PossessedTarget;
        if (target == null || other.gameObject != target.gameObject)
            return;

        Interaction?.OnZoneExit(target);
    }
    #endregion
}