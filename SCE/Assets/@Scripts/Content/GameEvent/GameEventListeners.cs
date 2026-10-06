using UnityEngine;




/// <summary>사용 예시 — 규칙으로 표현할 수 없는, 인자 전체가 필요한 처리는 리스너로.</summary>
public sealed class KillFeed : IGameEventListener<CreatureDiedEvent>
{
    public void Init() { GameEventBus.Subscribe(this); }
    public void Clear() { GameEventBus.Unsubscribe(this); }

    public void OnGameEvent(in CreatureDiedEvent evt)
    {
        // 상태 규칙이 이미 적용된 뒤 호출된다 — 여기서 처치 수를 읽으면 이번 처치가 포함되어 있다
        bool byPlayer = evt.Killer != null && evt.Killer == Managers.Object.PossessedTarget;
        if (byPlayer)
            LogPrinter.Log($"[KillFeed] 처치 templateID {evt.TemplateId}");
    }
}
