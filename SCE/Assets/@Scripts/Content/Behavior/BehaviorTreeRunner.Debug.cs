using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// 디버거 입구 — 편집기만 (출시 · 개발 빌드에는 읽기 함수도, 알림 호출도 없다). Behavior Tree Editor의 디버거가 Play 중 몬스터 하나의 실행 상태를 읽고 (보기만 — 게임 상태를 바꾸지 않는다),
/// 노드에 들어갈 때 · 평가가 끝날 때 알림을 받는다 (breakpoint · 한 평가씩 보기). 알림을 받는 곳이 없으면 알림마다 static 비교 한 번.
/// </summary>
public sealed partial class BehaviorTreeRunner
{
    /// <summary>노드에 들어갔다 — 조건을 지나 경로에 올랐다 (반복으로 다시 들어온 것도, Task는 시작 전). 알림을 받는 곳의 예외는 로그만 남긴다 (이 몬스터의 트리를 멈추지 않게)</summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void DebugNodeEntered(int index)
    {
#if UNITY_EDITOR
        IBehaviorTreeDebugListener listener = DebugListener;
        if (listener == null)
            return;
        try
        {
            listener.OnNodeEntered(this, index);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
#endif
    }

    /// <summary>평가 하나가 끝났다. 알림을 받는 곳의 예외는 로그만 남긴다</summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void DebugEvaluated()
    {
#if UNITY_EDITOR
        IBehaviorTreeDebugListener listener = DebugListener;
        if (listener == null)
            return;
        try
        {
            listener.OnEvaluated(this);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
#endif
    }

#if UNITY_EDITOR
    /// <summary>알림을 받는 곳 — 편집기의 디버거 하나 (없으면 null). 편집기가 Play 중에 두고 Play를 마치면 비운다</summary>
    public static IBehaviorTreeDebugListener DebugListener;

    /// <summary>Decorator의 마지막 조건 결과 — 들어올 때 · 지켜볼 때 적은 것 (미리 보기는 적지 않는다). 아직 보지 않았거나 이 실행기의 정의에 없는 노드면 Unknown. O(1)</summary>
    public EBehaviorDebugCondition DebugConditionOf(BehaviorDecorator decorator)
    {
        if (OwnsAuxiliary(decorator) == false)
            return EBehaviorDebugCondition.Unknown;
        switch (SystemMemory(decorator).Int0 & ResultMask)
        {
            case ResultTrue:
                return EBehaviorDebugCondition.True;
            case ResultFalse:
                return EBehaviorDebugCondition.False;
            default:
                return EBehaviorDebugCondition.Unknown;
        }
    }

    /// <summary>
    /// LowerPriority로 지켜보는 중인가 — 평가마다 다시 보는 조건: 검색이 지나가 등록되었고 붙은 노드가 진행 경로 끝 노드보다 앞이다 (결과가 참이 되면 붙은 노드부터 다시 검색을 요청한다).
    /// 경로가 비었거나 (멈춤 · 고장 · 트리 끝) 이 실행기의 정의에 없는 노드면 false. O(1)
    /// </summary>
    public bool DebugIsObserving(BehaviorDecorator decorator)
    {
        if (OwnsAuxiliary(decorator) == false || (SystemMemory(decorator).Int0 & ObservingFlag) == 0)
            return false;
        return _pathLength > 0 && decorator.OwnerIndex < _path[_pathLength - 1]; // 평가 (CollectRequests)가 보는 범위와 같게
    }

    /// <summary>Service가 진행 경로에 있어 갱신하는 중인가 — nextTime = 다음 갱신 시각 (Now와 같은 시계, 경로를 나간 뒤에도 남는다). 이 실행기의 정의에 없는 노드면 false. O(1)</summary>
    public bool DebugServiceState(BehaviorService service, out float nextTime)
    {
        nextTime = 0f;
        if (OwnsAuxiliary(service) == false)
            return false;
        ref BehaviorNodeMemory memory = ref SystemMemory(service);
        nextTime = memory.Float0;
        return memory.Int0 != 0;
    }

    /// <summary>기다리는 요청의 종류 — 그 노드가 실패로 끝남 (Fail) · 그 노드부터 다시 검색 (Search). 노드는 PendingRequestNode, 없으면 None</summary>
    public EBehaviorDebugRequest DebugPendingRequest
    {
        get
        {
            if (_requestNode < 0)
                return EBehaviorDebugRequest.None;
            return _requestKind == ERequestKind.Fail ? EBehaviorDebugRequest.Fail : EBehaviorDebugRequest.Search;
        }
    }

    /// <summary>이 실행기가 묶은 정의의 보조 노드인가 — 다른 정의 (다시 읽기 전 등)의 노드로 칸을 읽지 않게</summary>
    private bool OwnsAuxiliary(BehaviorAuxiliaryNode node)
    {
        return node != null && _definition != null && (uint)node.Index < (uint)_definition.Auxiliaries.Length && ReferenceEquals(_definition.Auxiliaries[node.Index], node);
    }
#endif
}

#if UNITY_EDITOR
/// <summary>실행기의 알림을 받는 곳 (편집기의 디버거) — 실행기의 평가 안에서 불리므로 게임 상태를 바꾸지 않는다. 예외는 실행기가 로그만 남긴다</summary>
public interface IBehaviorTreeDebugListener
{
    /// <summary>runner가 실행 순서 index의 노드에 들어갔다 — 경로 끝이 그 노드 (Task는 아직 시작 전)</summary>
    void OnNodeEntered(BehaviorTreeRunner runner, int index);

    /// <summary>runner의 평가 하나가 끝났다</summary>
    void OnEvaluated(BehaviorTreeRunner runner);
}

/// <summary>Decorator의 마지막 조건 결과 (디버거)</summary>
public enum EBehaviorDebugCondition : byte
{
    Unknown, // 아직 보지 않음
    False,
    True,
}

/// <summary>기다리는 요청 (디버거)</summary>
public enum EBehaviorDebugRequest : byte
{
    None,
    Fail,   // 그 노드가 실패로 끝남 — 부모가 이어 감 (Self)
    Search, // 그 노드부터 다시 검색 (LowerPriority · 키 값 바뀜)
}

/// <summary>Scene 뷰 모양의 쓰임 — 색은 편집기가 정한다</summary>
public enum EBehaviorDebugShapeRole : byte
{
    Range, // 감지 · 조건 거리 (대상 거리 · 사거리)
    Limit, // 놓는 거리 · 최소 사거리
    Goal,  // 목적지 · 다 왔다고 보는 거리 · 공전 원
    Home,  // 집 둘레
}

/// <summary>Scene 뷰 모양의 꼴</summary>
public enum EBehaviorDebugShapeKind : byte
{
    Circle,
    Point,
    Line,
}

/// <summary>Scene 뷰 모양 하나 (월드 좌표)</summary>
public struct BehaviorDebugShape
{
    public EBehaviorDebugShapeKind Kind;
    public EBehaviorDebugShapeRole Role;
    public Vector2 From;  // 원 · 점의 가운데, 선의 시작
    public Vector2 To;    // 선의 끝
    public float Radius;  // 원의 반지름
    public string Label;  // 모양 옆 글 (없으면 빈 글)
}

/// <summary>노드가 담는 Scene 뷰 모양 (BehaviorNode.CollectDebugShapes) — 그리기는 편집기가 한다. 유한하지 않은 값 · 0 이하 반지름의 원은 담지 않는다</summary>
public sealed class BehaviorDebugShapes
{
    private readonly List<BehaviorDebugShape> _shapes = new List<BehaviorDebugShape>();

    public int Count => _shapes.Count;

    public BehaviorDebugShape this[int index] => _shapes[index];

    public void Clear()
    {
        _shapes.Clear();
    }

    public void Circle(Vector2 center, float radius, EBehaviorDebugShapeRole role, string label)
    {
        if (radius > 0f && IsFinite(center) && float.IsInfinity(radius) == false)
            _shapes.Add(new BehaviorDebugShape { Kind = EBehaviorDebugShapeKind.Circle, Role = role, From = center, Radius = radius, Label = label ?? "" });
    }

    public void Point(Vector2 at, EBehaviorDebugShapeRole role, string label)
    {
        if (IsFinite(at))
            _shapes.Add(new BehaviorDebugShape { Kind = EBehaviorDebugShapeKind.Point, Role = role, From = at, Label = label ?? "" });
    }

    public void Line(Vector2 from, Vector2 to, EBehaviorDebugShapeRole role, string label)
    {
        if (IsFinite(from) && IsFinite(to))
            _shapes.Add(new BehaviorDebugShape { Kind = EBehaviorDebugShapeKind.Line, Role = role, From = from, To = to, Label = label ?? "" });
    }

    private static bool IsFinite(Vector2 point)
    {
        return float.IsNaN(point.x) == false && float.IsNaN(point.y) == false && float.IsInfinity(point.x) == false && float.IsInfinity(point.y) == false;
    }
}

/// <summary>디버거 글의 수 · 자리 — 소수 둘째 자리까지, 문화권 없이</summary>
public static class BehaviorDebugText
{
    public static string Number(float value)
    {
        string text = value.ToString("0.##", CultureInfo.InvariantCulture);
        return text == "-0" ? "0" : text; // 0으로 반올림된 음수
    }

    public static string Point(Vector2 point)
    {
        return "(" + Number(point.x) + ", " + Number(point.y) + ")";
    }
}
#endif