/// <summary>
/// 노드 등록 목록. 새 조건/효과를 만들면 여기에 한 줄 추가한다.
/// 키는 각 클래스의 [InteractionNode] 어트리뷰트에서 읽으므로 여기서 문자열을 쓰지 않는다.
/// </summary>
public static class InteractionNodeCatalog
{
    /// <summary>상호작용 편집기와 JSON 로더에서 선택할 수 있는 조건 및 효과 부품을 등록한다.</summary>
    public static void RegisterAll()
    {
        // Conditions
        InteractionNodeRegistry.RegisterCondition<DistanceCondition>();
        InteractionNodeRegistry.RegisterCondition<InZoneCondition>();
        InteractionNodeRegistry.RegisterCondition<StatThresholdCondition>();
        InteractionNodeRegistry.RegisterCondition<NotCondition>();
        InteractionNodeRegistry.RegisterCondition<StateValueCondition>();
        InteractionNodeRegistry.RegisterCondition<DayCondition>();
        InteractionNodeRegistry.RegisterCondition<DayPeriodCondition>();
        InteractionNodeRegistry.RegisterCondition<StagePlayingCondition>();
        InteractionNodeRegistry.RegisterCondition<CanPushBoxCondition>();

        // Effects
        InteractionNodeRegistry.RegisterEffect<ShowMarkerEffect>();
        InteractionNodeRegistry.RegisterEffect<KillVillagerEffect>();
        InteractionNodeRegistry.RegisterEffect<PossessEffect>();
        InteractionNodeRegistry.RegisterEffect<PrintLogEffect>();
        InteractionNodeRegistry.RegisterEffect<SpawnUIEffect>();
        InteractionNodeRegistry.RegisterEffect<PlayAnimationEffect>();
        InteractionNodeRegistry.RegisterEffect<RaiseSignalEffect>();
        InteractionNodeRegistry.RegisterEffect<SwapCreatureDataEffect>();
        InteractionNodeRegistry.RegisterEffect<PushBoxEffect>();
    }
}
