#if UNITY_EDITOR
using System.Collections.Generic;

/// <summary>
/// 에디터 전용 진단 버퍼. 마지막 평가에서 각 조건이 참이었는지 기록한다.
/// 툴이 Watch를 걸었을 때만 기록하므로 평소 런타임 비용은 bool 검사 1회.
/// </summary>
public static class InteractionDiagnostics
{
    public sealed class Record
    {
        public readonly List<bool[]> ConditionResults = new(); // [interaction index][condition index]
        public readonly List<bool> Pass = new();
        public readonly List<bool> Active = new();
        public float LastEvaluateTime;
        public int LastFrame;
    }

    public static NpcInteractionComponent Watched { get; private set; }
    public static readonly Record Current = new Record();

    public static bool IsWatching(NpcInteractionComponent component)
    {
        return Watched != null && Watched == component;
    }

    public static void Watch(NpcInteractionComponent component)
    {
        Watched = component;
        Current.ConditionResults.Clear();
        Current.Pass.Clear();
        Current.Active.Clear();
    }

    public static void StopWatching()
    {
        Watched = null;
    }
}
#endif