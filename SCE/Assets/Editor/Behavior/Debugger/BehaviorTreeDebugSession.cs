using System;
using UnityEngine;

/// <summary>
/// Behavior Tree Editor의 디버거 (창 하나에 하나) — Play 중 대상 몬스터 · breakpoint · 멈춘 순간 · 한 평가씩 보기.
/// 실행기의 알림 (IBehaviorTreeDebugListener — BehaviorTreeRunner.DebugListener)을 받는다. 알림은 평가 안에서 오므로 게임 상태를 바꾸지 않고 담기만 하고, 멈춤은 Unity Pause뿐이다
/// (트리를 그 노드에서 세우지 않는다 — Unity는 그 프레임을 마친 뒤 멈추므로 들어간 순간을 Paused에 따로 담는다).
///  - breakpoint: 대상이 있으면 대상의 실행기만, 없으면 그 트리를 쓰는 모든 실행기 — 처음 들어온 몬스터가 대상이 된다. 한 프레임에 한 번 (처음 것).
///  - 한 평가씩: RequestStep 뒤 대상의 다음 평가가 끝나면 멈춘다 (그 평가에서 breakpoint에 이미 멈췄으면 그 순간 그대로).
///  - 대상은 실행기와 몸의 생애 번호로 — 몸이 없어지거나 꺼지거나 (Despawn) 다른 생애가 되면 (풀에서 다시 스폰) 대상이 아니다 (잠든 몸은 그대로).
/// </summary>
public sealed class BehaviorTreeDebugSession : IBehaviorTreeDebugListener
{
    private readonly Action _pause;
    private readonly Func<bool> _isPlaying;
    private readonly Func<int> _frameCount;
    private BehaviorTreeRunner _target;
    private int _targetSerial;
    private int _heldFrame = -1; // 멈춘 순간을 담은 프레임 — 같은 프레임의 다음 breakpoint는 보지 않는다
    private bool _stepPending;

    /// <param name="pause">Unity Pause (EditorApplication.isPaused = true) — 시험은 바꿔 끼운다</param>
    /// <param name="isPlaying">알림을 받을 때인가 (기본 Application.isPlaying — Play가 아닌 실행기의 알림은 보지 않는다)</param>
    /// <param name="frameCount">지금 프레임 번호 (기본 Time.frameCount)</param>
    public BehaviorTreeDebugSession(BehaviorBreakpoints breakpoints, Action pause, Func<bool> isPlaying = null, Func<int> frameCount = null)
    {
        Breakpoints = breakpoints;
        _pause = pause;
        _isPlaying = isPlaying ?? (() => Application.isPlaying);
        _frameCount = frameCount ?? (() => Time.frameCount);
    }

    public BehaviorBreakpoints Breakpoints { get; }

    /// <summary>대상의 지금 모습 (CaptureLive가 담는다)</summary>
    public BehaviorDebugFrame Live { get; } = new BehaviorDebugFrame();

    /// <summary>멈춘 순간 (breakpoint에 들어간 순간 · 한 평가가 끝난 순간)</summary>
    public BehaviorDebugFrame Paused { get; } = new BehaviorDebugFrame();

    /// <summary>멈춘 순간을 담았다 — Unity가 Pause를 풀면 (Resumed) 지운다</summary>
    public bool HasPaused { get; private set; }

    /// <summary>멈춘 노드의 실행 순서 — 한 평가로 멈췄으면 -1</summary>
    public int PausedNode { get; private set; } = -1;

    /// <summary>멈춘 까닭 ("breakpoint 'Chase'" · "다음 평가")</summary>
    public string PausedReason { get; private set; } = "";

    /// <summary>한 평가 뒤에 멈추기를 기다린다</summary>
    public bool StepPending => _stepPending;

    /// <summary>대상 · 멈춘 순간이 바뀔 때마다 오른다 — 창이 견주어 상태 줄을 맞춘다</summary>
    public int Version { get; private set; }

    /// <summary>대상을 골랐다 (지금 쓸 수 없어도)</summary>
    public bool HasTarget => _target != null;

    /// <summary>대상이 지금도 그 몸 · 그 생애인가 — 몸이 없어졌거나 꺼졌거나 (Despawn — 풀에 반납) 다른 생애면 (다시 스폰) 아니다. 잠든 몸은 그대로 대상</summary>
    public bool IsTargetValid
    {
        get
        {
            if (_target == null)
                return false;
            IBehaviorAgent agent = _target.Agent;
            if (agent == null)
                return false;
            if (agent is Component body && (body == null || body.gameObject.activeSelf == false))
                return false;
            return agent.SpawnSerial == _targetSerial;
        }
    }

    /// <summary>대상의 실행기 — 쓸 수 없으면 null</summary>
    public BehaviorTreeRunner Target => IsTargetValid ? _target : null;

    /// <summary>대상 몬스터 — 쓸 수 없거나 몸이 몬스터가 아니면 null</summary>
    public MonsterBase TargetMonster => Target != null ? Target.Agent as MonsterBase : null;

    /// <summary>대상을 고른다 (null이면 대상 없음) — 기다리던 한 평가는 그만두고, 멈춘 순간 (앞 대상의 것)은 지운다. 지금 대상 그대로면 아무것도 하지 않는다</summary>
    public void SetTarget(BehaviorTreeRunner runner)
    {
        if (ReferenceEquals(runner, _target) && IsTargetValid)
            return;
        _target = runner;
        _targetSerial = runner != null && runner.Agent != null ? runner.Agent.SpawnSerial : 0;
        _stepPending = false;
        if (HasPaused)
            ClearPaused();
        Version++;
    }

    public void ClearTarget()
    {
        SetTarget(null);
    }

    /// <summary>실행기의 알림을 받는다 (BehaviorTreeRunner.DebugListener) — 여러 번 불러도 된다</summary>
    public void Attach()
    {
        BehaviorTreeRunner.DebugListener = this;
    }

    /// <summary>알림을 그만 받는다 — 다른 곳이 받고 있으면 그대로</summary>
    public void Detach()
    {
        if (ReferenceEquals(BehaviorTreeRunner.DebugListener, this))
            BehaviorTreeRunner.DebugListener = null;
    }

    /// <summary>대상의 지금 모습을 Live에 담는다 — 대상이 없으면 비운다</summary>
    public void CaptureLive()
    {
        Live.Capture(Target);
    }

    /// <summary>대상이 평가하는 상태인가 — 정의를 묶었고 멈추지 않았고 (잠듦 · 죽음 · 쫓기) 고장 나지 않았다. 대상이 없으면 false</summary>
    public bool CanStep
    {
        get
        {
            BehaviorTreeRunner target = Target;
            return target != null && target.Definition != null && target.IsStopped == false && target.IsFaulted == false;
        }
    }

    /// <summary>한 평가 뒤에 멈춘다 — 부르는 쪽이 Pause를 푼다. 대상이 평가하는 상태가 아니면 (CanStep) 하지 않고 false</summary>
    public bool RequestStep()
    {
        if (CanStep == false)
            return false;
        _stepPending = true;
        Version++;
        return true;
    }

    /// <summary>Unity가 Pause를 풀었다 — 멈춘 순간을 지운다 (기다리던 한 평가는 그대로)</summary>
    public void Resumed()
    {
        if (HasPaused == false)
            return;
        ClearPaused();
        Version++;
    }

    /// <summary>Unity가 멈췄다 — 이 디버거가 멈춘 것이 아니면 (Pause 버튼) 기다리던 한 평가는 그만둔다</summary>
    public void PausedOutside()
    {
        if (HasPaused || _stepPending == false)
            return;
        _stepPending = false;
        Version++;
    }

    /// <summary>Play를 마칠 때 — 대상 · 멈춘 순간 · 한 평가를 모두 지운다 (breakpoint는 남는다)</summary>
    public void Reset()
    {
        _target = null;
        _targetSerial = 0;
        _stepPending = false;
        _heldFrame = -1;
        ClearPaused();
        Live.Clear();
        Version++;
    }

    void IBehaviorTreeDebugListener.OnNodeEntered(BehaviorTreeRunner runner, int index)
    {
        if (_isPlaying() == false)
            return;
        BehaviorTreeDefinition definition = runner.Definition;
        if (definition == null || (uint)index >= (uint)definition.MainNodeCount)
            return;
        string nodeId = definition.GetMainNode(index).NodeId;
        if (Breakpoints == null || Breakpoints.Contains(definition.Id, nodeId) == false)
            return;
        bool hasTarget = IsTargetValid;
        if (hasTarget && ReferenceEquals(runner, _target) == false)
            return;
        int frame = _frameCount();
        if (HasPaused && frame == _heldFrame)
            return;
        if (hasTarget == false)
            SetTarget(runner);
        Hold(runner, index, "breakpoint '" + nodeId + "'", frame);
    }

    void IBehaviorTreeDebugListener.OnEvaluated(BehaviorTreeRunner runner)
    {
        if (_stepPending == false || _isPlaying() == false || IsTargetValid == false || ReferenceEquals(runner, _target) == false)
            return;
        _stepPending = false;
        int frame = _frameCount();
        if (HasPaused && frame == _heldFrame)
        {
            Version++; // 이 평가에서 이미 멈췄다 (breakpoint) — 그 순간을 남긴다
            return;
        }
        Hold(runner, -1, "다음 평가", frame);
    }

    /// <summary>멈춘 순간을 담고 Unity를 멈춘다</summary>
    private void Hold(BehaviorTreeRunner runner, int node, string reason, int frame)
    {
        Paused.Capture(runner);
        PausedNode = node;
        PausedReason = reason;
        HasPaused = true;
        _heldFrame = frame;
        Version++;
        _pause?.Invoke();
    }

    private void ClearPaused()
    {
        HasPaused = false;
        PausedNode = -1;
        PausedReason = "";
        Paused.Clear();
    }
}