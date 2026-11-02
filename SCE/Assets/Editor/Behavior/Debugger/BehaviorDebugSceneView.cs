using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 디버거의 Scene 뷰 — 대상 몬스터 하나: 노드가 담은 모양 (거리 원 · 목적지 — 쓰임마다 색, BehaviorNode.CollectDebugShapes), Blackboard 크리처 키가 가리키는 대상까지의 점선 (키 이름).
/// 창이 갱신마다 보일 모습을 넘긴다 (Show) — Hide가 구독을 푼다. Play 중에만 그린다
/// </summary>
public static class BehaviorDebugSceneView
{
    private const float PointRadius = 0.15f; // 점 표시의 반지름 (월드 단위)

    private static readonly Color s_rangeColor = new Color(0.35f, 0.85f, 1f, 0.95f);   // 감지 · 조건 거리
    private static readonly Color s_limitColor = new Color(1f, 0.45f, 0.35f, 0.95f);   // 놓는 거리 · 최소 사거리
    private static readonly Color s_goalColor = new Color(0.65f, 1f, 0.35f, 0.95f);    // 목적지 · 다 왔다고 보는 거리
    private static readonly Color s_homeColor = new Color(0.8f, 0.6f, 1f, 0.95f);      // 집 둘레
    private static readonly Color s_targetColor = new Color(1f, 0.9f, 0.3f, 0.95f);    // 대상 점선

    private static BehaviorDebugFrame s_frame;
    private static bool s_subscribed;

    /// <summary>그릴 모습을 넘긴다 (모습이 없으면 그리지 않는다) — 처음이면 Scene 뷰 그리기를 구독한다</summary>
    public static void Show(BehaviorDebugFrame frame)
    {
        s_frame = frame;
        if (s_subscribed)
            return;
        s_subscribed = true;
        SceneView.duringSceneGui -= OnSceneGUI;
        SceneView.duringSceneGui += OnSceneGUI;
    }

    /// <summary>그리기를 그만둔다 — 구독을 풀고 Scene 뷰를 한 번 다시 그려 지운다</summary>
    public static void Hide()
    {
        bool wasShown = s_subscribed;
        s_frame = null;
        s_subscribed = false;
        SceneView.duringSceneGui -= OnSceneGUI;
        if (wasShown)
            SceneView.RepaintAll();
    }

    private static void OnSceneGUI(SceneView view)
    {
        BehaviorDebugFrame frame = s_frame;
        if (Event.current.type != EventType.Repaint || frame == null || frame.Definition == null || DebugMenuGuard.CanUse() == false)
            return;

        CompareFunction previousZTest = Handles.zTest;
        Color previousColor = Handles.color;
        Handles.zTest = CompareFunction.Always;

        for (int i = 0; i < frame.Shapes.Count; i++)
            DrawShape(frame.Shapes[i]);

        Vector3 position = frame.Position;
        Handles.color = s_targetColor;
        for (int i = 0; i < frame.Targets.Count; i++)
        {
            BehaviorDebugTarget target = frame.Targets[i];
            Handles.DrawDottedLine(position, target.Position, 4f);
            Handles.Label(target.Position, target.Key + ": " + target.Name);
        }

        Handles.color = previousColor;
        Handles.zTest = previousZTest;
    }

    private static void DrawShape(BehaviorDebugShape shape)
    {
        Handles.color = ColorOf(shape.Role);
        Vector3 from = shape.From;
        switch (shape.Kind)
        {
            case EBehaviorDebugShapeKind.Circle:
                Handles.DrawWireDisc(from, Vector3.forward, shape.Radius);
                if (shape.Label.Length > 0)
                    Handles.Label(from + new Vector3(0f, shape.Radius, 0f), shape.Label);
                break;
            case EBehaviorDebugShapeKind.Point:
                Handles.DrawWireDisc(from, Vector3.forward, PointRadius);
                Handles.DrawLine(from + new Vector3(-PointRadius, 0f, 0f), from + new Vector3(PointRadius, 0f, 0f));
                Handles.DrawLine(from + new Vector3(0f, -PointRadius, 0f), from + new Vector3(0f, PointRadius, 0f));
                if (shape.Label.Length > 0)
                    Handles.Label(from, shape.Label);
                break;
            default:
                Vector3 to = shape.To;
                Handles.DrawLine(from, to);
                if (shape.Label.Length > 0)
                    Handles.Label((from + to) / 2f, shape.Label);
                break;
        }
    }

    private static Color ColorOf(EBehaviorDebugShapeRole role)
    {
        switch (role)
        {
            case EBehaviorDebugShapeRole.Range:
                return s_rangeColor;
            case EBehaviorDebugShapeRole.Limit:
                return s_limitColor;
            case EBehaviorDebugShapeRole.Goal:
                return s_goalColor;
            default:
                return s_homeColor;
        }
    }
}