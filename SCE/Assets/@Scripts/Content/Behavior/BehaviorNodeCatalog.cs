/// <summary>
/// Behavior Tree 노드 등록 목록 — 새 노드를 만들면 여기에 한 줄 더한다. 키는 각 클래스의 [BehaviorNode] 어트리뷰트에서 읽으므로 여기서 글자를 쓰지 않는다.
/// BehaviorNodeRegistry.Default가 처음 쓰일 때 한 번 부른다.
/// </summary>
public static class BehaviorNodeCatalog
{
    public static void RegisterAll(BehaviorNodeRegistry registry)
    {
        // Composite
        registry.Register<BehaviorSelector>();
        registry.Register<BehaviorSequence>();

        // Task
        registry.Register<BehaviorWaitTask>();
        registry.Register<BehaviorIdleTask>();
        registry.Register<BehaviorSetBoolTask>();
        registry.Register<BehaviorSetIntTask>();
        registry.Register<BehaviorSetFloatTask>();
        registry.Register<BehaviorSetVector2Task>();
        registry.Register<BehaviorClearKeyTask>();

        // Task — 칸 이동 (SCE)
        registry.Register<BehaviorTilePatrolStepTask>();
        registry.Register<BehaviorTileEscapeStepTask>();

        // Decorator
        registry.Register<BehaviorBlackboardDecorator>();
        registry.Register<BehaviorCooldownDecorator>();
        registry.Register<BehaviorTimeLimitDecorator>();
        registry.Register<BehaviorLoopDecorator>();
        registry.Register<BehaviorForceSuccessDecorator>();

        // Decorator — 칸 이동 (SCE)
        registry.Register<BehaviorTileEscapeDueDecorator>();
    }
}