/// <summary>
/// 노드 등록 목록. 새 조건/효과를 만들면 여기에 한 줄 추가한다.
/// 키는 각 클래스의 [InteractionNode] 어트리뷰트에서 읽으므로 여기서 문자열을 쓰지 않는다.
/// </summary>
public static class InteractionNodeCatalog
{
    public static void RegisterAll()
    {
        // Conditions
        InteractionNodeRegistry.RegisterCondition<DistanceCondition>();
        InteractionNodeRegistry.RegisterCondition<InZoneCondition>();
        InteractionNodeRegistry.RegisterCondition<StatThresholdCondition>();
        InteractionNodeRegistry.RegisterCondition<NotCondition>();

        // Effects
        InteractionNodeRegistry.RegisterEffect<ShowMarkerEffect>();
        InteractionNodeRegistry.RegisterEffect<PossessEffect>();
        InteractionNodeRegistry.RegisterEffect<PrintLogEffect>();
        InteractionNodeRegistry.RegisterEffect<SpawnUIEffect>();
        InteractionNodeRegistry.RegisterEffect<PlayAnimationEffect>();
    }
}