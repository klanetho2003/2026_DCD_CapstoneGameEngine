using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 디버거 (Play 중) — 대상 몬스터의 실행 모습을 그래프 · Blackboard 탭 · Scene 뷰에 보이고, breakpoint · 한 평가씩 보기 · Play 중 저장한 트리를 몬스터에 입힌다.
///  - 대상: Hierarchy · Scene에서 몬스터를 고르면 그 몬스터 (창도 그 몬스터의 트리를 고른다), 툴바 [대상]에서도 (보이는 트리를 쓰는 몬스터), breakpoint에 처음 들어온 몬스터도.
///    대상을 바꾸면 앞 대상의 멈춘 순간을 지우고 상태 줄에 알린다 (Unity는 Pause 그대로).
///  - 갱신: 0.1초마다 대상의 지금 모습을 담아 보인다 (BehaviorTreeDebugSession.CaptureLive). 멈춘 순간이 있으면 그것을 — 툴바 [멈춘 순간]을 끄면 지금 모습 (Unity가 멈춘 프레임 끝).
///    대상이 다른 트리를 쓰면 그래프에는 보이지 않고 ROOT 카드에 알린다 (Scene 뷰는 그대로).
///  - breakpoint: 카드 오른쪽 클릭 — 이 PC에만 남는다 (BehaviorBreakpoints, 트리 Id + 노드 이름표), 그래프 위 [breakpoint 끄기 (n)]가 이 트리의 것을 모두 끈다.
///    게임은 저장한 트리 파일을 읽으므로 이름표가 파일 · 게임이 읽은 트리에 있어야 걸린다 — 켤 때 걸리지 않을 까닭을 상태 줄에 (BreakpointBlocker),
///    Play 중 게임이 읽은 트리에 이름표가 빈 노드가 있으면 ROOT 카드에 알린다. 대상이 없을 때는 이 트리의 breakpoint 가운데 게임이 읽은 트리에 없는 이름표가 있으면 (MissingBreakpointsNote).
///  - 저장: Play 중 저장한 트리는 저장이 끝난 뒤 다시 컴파일해 그 트리를 쓰는 몬스터를 다시 묶는다 (BehaviorManager.ReloadTree — 결과는 상태 줄).
/// 실행기의 알림은 Play 중 · 창이 있는 동안만 받는다 (BehaviorTreeRunner.DebugListener). Play를 마치면 디버거 표시를 모두 지운다 (breakpoint는 남는다)
/// </summary>
public sealed partial class BehaviorTreeEditorWindow
{
    private const long DebugRefreshMs = 100;

    [SerializeField] private bool _debugSceneView = true; // Scene 뷰에 그린다 — 창을 다시 만들어도 그대로

    [NonSerialized] private BehaviorTreeDebugSession _debug;
    [NonSerialized] private IVisualElementScheduledItem _debugJob;
    [NonSerialized] private VisualElement _debugBar;
    [NonSerialized] private ToolbarButton _targetButton;
    [NonSerialized] private ToolbarToggle _sceneToggle;
    [NonSerialized] private ToolbarButton _stepButton;
    [NonSerialized] private ToolbarToggle _pausedToggle;
    [NonSerialized] private Button _clearBreakpointsButton;
    [NonSerialized] private VisualElement _debugKeysView;     // Blackboard 탭 위 — 보이는 모습의 키 값
    [NonSerialized] private BehaviorDebugFrame _shownDebugFrame; // 그래프가 보이는 모습 — 없으면 null
    [NonSerialized] private int _seenDebugVersion = -1;      // 상태 줄에 알린 디버거 판 (대상 · 멈춘 순간)
    [NonSerialized] private bool _showPaused = true;          // 멈춘 순간이 있으면 그것을 보인다 (끄면 지금 모습)
    [NonSerialized] private readonly List<KeyValuePair<string, string>> _playReloads = new List<KeyValuePair<string, string>>(); // Play 중 저장한 트리 (Id, 글) — 저장이 끝난 뒤 입힌다
    [NonSerialized] private readonly List<BehaviorNode> _debugNodes = new List<BehaviorNode>(); // 대상이 없을 때 고른 트리의 노드를 담는 곳 (갱신마다 다시 쓴다)

    #region 생명주기
    /// <summary>OnEnable — 디버거를 만들고 고르기 · Pause · Play 알림을 구독한다. Play 중이면 실행기의 알림도 받는다</summary>
    private void EnableDebug()
    {
        _debug = new BehaviorTreeDebugSession(new BehaviorBreakpoints(BehaviorBreakpoints.ProjectKeyPrefix()), () => EditorApplication.isPaused = true);
        Selection.selectionChanged += OnDebugSelectionChanged;
        EditorApplication.pauseStateChanged += OnDebugPauseStateChanged;
        EditorApplication.playModeStateChanged += OnDebugPlayModeChanged;
        if (EditorApplication.isPlaying)
            _debug.Attach();
    }

    /// <summary>OnDisable — 구독 · 실행기의 알림 · Scene 뷰 그리기를 그만둔다</summary>
    private void DisableDebug()
    {
        Selection.selectionChanged -= OnDebugSelectionChanged;
        EditorApplication.pauseStateChanged -= OnDebugPauseStateChanged;
        EditorApplication.playModeStateChanged -= OnDebugPlayModeChanged;
        _debugJob?.Pause();
        _debug?.Detach();
        BehaviorDebugSceneView.Hide();
    }

    /// <summary>CreateGUI — 0.1초마다 갱신 (Play가 아니면 바로 끝난다)</summary>
    private void StartDebugJob()
    {
        _debugJob = rootVisualElement.schedule.Execute(DebugTick).Every(DebugRefreshMs);
    }

    private void OnDebugPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
            _debug.Attach();
        else if (change == PlayModeStateChange.ExitingPlayMode)
            StopDebugView();
    }

    /// <summary>Pause가 풀리면 멈춘 순간을 지우고, 이 디버거가 멈춘 것이 아니면 기다리던 한 평가를 그만둔다</summary>
    private void OnDebugPauseStateChanged(PauseState state)
    {
        if (state == PauseState.Unpaused)
            _debug.Resumed();
        else
            _debug.PausedOutside();
        DebugTick();
    }

    /// <summary>Play를 마칠 때 — 대상 · 멈춘 순간 · 표시를 모두 지운다</summary>
    private void StopDebugView()
    {
        _debug.Reset();
        _debug.Detach();
        _playReloads.Clear();
        _shownDebugFrame = null;
        _graph?.ShowDebug(null, -1);
        _graph?.SetDebugRootText("");
        BehaviorDebugSceneView.Hide();
        UpdateDebugKeysView();
        UpdateDebugBar(false);
    }
    #endregion

    #region 툴바
    /// <summary>툴바의 디버거 묶음 (Play 중에만 보인다) — [대상] · [Scene 뷰] · [다음 평가] · [멈춘 순간]</summary>
    private VisualElement BuildDebugBar()
    {
        _debugBar = new VisualElement();
        _debugBar.style.flexDirection = FlexDirection.Row;
        _debugBar.style.alignItems = Align.Center;
        var title = new Label("디버그");
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = BehaviorGraphStyle.DebugText;
        title.style.marginLeft = 4;
        title.style.marginRight = 4;
        _debugBar.Add(title);
        _targetButton = new ToolbarButton(ShowTargetMenu) { text = "대상 없음", tooltip = "디버그 대상 — 보이는 트리를 쓰는 몬스터 (Hierarchy · Scene에서 몬스터를 고르면 그 몬스터, breakpoint에 처음 들어온 몬스터도)" };
        _debugBar.Add(_targetButton);
        _sceneToggle = new ToolbarToggle { text = "Scene 뷰", tooltip = "대상의 거리 원 · 크리처 키 대상까지의 점선 · 집 · 남은 경로를 Scene 뷰에 그린다" };
        _sceneToggle.SetValueWithoutNotify(_debugSceneView);
        _sceneToggle.RegisterValueChangedCallback(evt =>
        {
            _debugSceneView = evt.newValue;
            DebugTick();
        });
        _debugBar.Add(_sceneToggle);
        _stepButton = new ToolbarButton(OnStepClicked)
        {
            text = "다음 평가",
            tooltip = "대상의 다음 평가가 끝난 프레임에 멈춘다 (멈춰 있으면 Pause를 풀고). 평가하지 않는 대상 (트리 없음 · 죽음 · 고장)이면 꺼진다 — 활성 거리 밖이면 가까이 올 때까지 기다린다",
        };
        _debugBar.Add(_stepButton);
        _pausedToggle = new ToolbarToggle { text = "멈춘 순간", tooltip = "켜면 breakpoint에 들어간 순간 (또는 한 평가가 끝난 순간), 끄면 지금 모습 (Unity가 멈춘 프레임 끝)" };
        _pausedToggle.SetValueWithoutNotify(true);
        _pausedToggle.RegisterValueChangedCallback(evt =>
        {
            _showPaused = evt.newValue;
            DebugTick();
        });
        _debugBar.Add(_pausedToggle);
        _debugBar.Add(new ToolbarSpacer());
        _debugBar.style.display = DisplayStyle.None;
        return _debugBar;
    }

    /// <summary>[대상] — 보이는 트리 (그 트리의 변형도)를 쓰는 몬스터 (스폰 차례)와 '대상 없음'</summary>
    private void ShowTargetMenu()
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("대상 없음"), _debug.IsTargetValid == false, () =>
        {
            if (ChooseTarget(null))
                SetStatus(DroppedMomentStatus(null));
            DebugTick();
        });
        string treeId = _selected != null ? _selected.Tree.Id : null;
        var monsters = new List<MonsterBase>();
        if (DebugMenuGuard.CanUse() && string.IsNullOrEmpty(treeId) == false)
        {
            foreach (MonsterBase monster in Managers.Object.Monsters)
            {
                if (monster != null && BehaviorTreeVariants.BelongsTo(monster.BehaviorTreeId, treeId))
                    monsters.Add(monster);
            }
        }
        monsters.Sort((a, b) => a.SpawnSerial.CompareTo(b.SpawnSerial));
        menu.AddSeparator("");
        if (monsters.Count == 0)
            menu.AddDisabledItem(new GUIContent($"'{treeId}' 트리를 쓰는 몬스터 없음".Replace('/', '∕')));
        for (int i = 0; i < monsters.Count; i++)
        {
            MonsterBase monster = monsters[i];
            BehaviorTreeVariants.TrySplit(monster.BehaviorTreeId, out _, out string variantName);
            string label = TargetLabel(monster) + (variantName.Length > 0 ? " · 변형 " + variantName : "") + (monster.IsDead ? " · 죽음" : "");
            menu.AddItem(new GUIContent(label.Replace('/', '∕')), ReferenceEquals(_debug.Target, monster.BehaviorRunner), () =>
            {
                if (ChooseTarget(monster.BehaviorRunner))
                    SetStatus(DroppedMomentStatus(monster.BehaviorRunner));
                DebugTick();
            });
        }
        menu.DropDown(_targetButton.worldBound);
    }

    /// <summary>대상을 고른다 (null이면 대상 없음) — 앞 대상의 멈춘 순간을 지웠으면 true (부르는 쪽이 상태 줄에 알린다 — DroppedMomentStatus)</summary>
    private bool ChooseTarget(BehaviorTreeRunner runner)
    {
        bool hadPaused = _debug.HasPaused;
        _debug.SetTarget(runner);
        return hadPaused && _debug.HasPaused == false;
    }

    /// <summary>대상을 바꿔 멈춘 순간을 지웠을 때의 상태 줄 — Unity는 Pause 그대로</summary>
    private static string DroppedMomentStatus(BehaviorTreeRunner runner)
    {
        return runner != null
            ? "멈춘 순간을 지웠습니다 — 대상을 바꿈 (Unity는 Pause 그대로, [다음 평가] = 새 대상의 한 평가)"
            : "멈춘 순간을 지웠습니다 — 대상 없음 (Unity는 Pause 그대로)";
    }

    /// <summary>[다음 평가] — 대상의 다음 평가가 끝나면 멈춘다 (Pause를 푼다). 대상이 없거나 평가하지 않는 상태면 상태 줄에 까닭</summary>
    private void OnStepClicked()
    {
        if (_debug.RequestStep() == false)
        {
            SetStatus(_debug.IsTargetValid
                ? $"지금 평가하지 않는 대상 — {TargetLabel(_debug.TargetMonster)} (트리 없음 · 죽음 · 고장)"
                : "디버그 대상이 없습니다 — Hierarchy · Scene에서 몬스터를 고르거나 [대상]에서 고르세요", true);
            return;
        }
        SetStatus($"{TargetLabel(_debug.TargetMonster)}의 다음 평가가 끝나면 멈춥니다");
        EditorApplication.isPaused = false;
    }

    /// <summary>툴바 글 · 켜짐을 맞춘다 (Play가 아니면 감춘다)</summary>
    private void UpdateDebugBar(bool playing)
    {
        if (_debugBar == null)
            return;
        _debugBar.style.display = playing ? DisplayStyle.Flex : DisplayStyle.None;
        _targetButton.text = _debug.IsTargetValid ? "대상: " + TargetLabel(_debug.TargetMonster) : "대상 없음";
        _stepButton.SetEnabled(_debug.CanStep);
        _stepButton.text = _debug.StepPending ? "다음 평가 기다림…" : "다음 평가";
        _pausedToggle.style.display = _debug.HasPaused ? DisplayStyle.Flex : DisplayStyle.None;
        _pausedToggle.SetValueWithoutNotify(_showPaused);
    }
    #endregion

    #region 갱신
    /// <summary>
    /// 0.1초마다 (그리고 대상 · Pause가 바뀔 때) — breakpoint 끄기 버튼, Play면 대상 확인 → 보일 모습 (멈춘 순간 또는 지금 모습) → 그래프 · ROOT 글 · 툴바 · Blackboard 탭 · Scene 뷰.
    /// Play가 아니면 디버거 표시를 지운다
    /// </summary>
    private void DebugTick()
    {
        if (_debug == null)
            return;
        UpdateClearBreakpointsButton();
        bool playing = DebugMenuGuard.CanUse();
        if (playing == false)
        {
            _debug.Detach();
            if (_shownDebugFrame != null || _debug.HasTarget || _debug.HasPaused || (_graph != null && _graph.DebugRootText.Length > 0))
                StopDebugView();
            UpdateDebugBar(false);
            return;
        }
        _debug.Attach();

        if (_debug.HasTarget && _debug.IsTargetValid == false)
        {
            _debug.ClearTarget();
            SetStatus("디버그 대상이 사라졌습니다 (Despawn · 다른 생애) — 다시 고르세요");
        }
        if (_debug.Version != _seenDebugVersion)
        {
            _seenDebugVersion = _debug.Version;
            if (_debug.HasPaused)
            {
                _showPaused = true;
                string missing = ShowTreeOfTarget(); // 다른 트리의 breakpoint면 그 트리로
                SetStatus(missing != null ? HeldStatus() + " · " + missing : HeldStatus());
            }
        }

        BehaviorDebugFrame frame = null;
        int held = -1;
        if (_debug.HasPaused && _showPaused)
        {
            frame = _debug.Paused;
            held = _debug.PausedNode;
        }
        else if (_debug.IsTargetValid)
        {
            _debug.CaptureLive();
            frame = _debug.Live;
        }
        if (frame != null && frame.Definition == null)
            frame = null;

        string shownTree = _selected != null ? _selected.Tree.Id : null;
        bool otherTree = frame != null && string.Equals(frame.Definition.Id, shownTree, StringComparison.Ordinal) == false;
        _shownDebugFrame = otherTree ? null : frame;
        if (_graph != null)
        {
            _graph.ShowDebug(_shownDebugFrame, held);
            _graph.SetDebugRootText(RootDebugText(frame, otherTree));
        }
        UpdateDebugBar(true);
        UpdateDebugKeysView();
        UpdateDebugSceneView(frame);
    }

    /// <summary>
    /// ROOT 카드의 디버거 글 — 대상 · 변형 · (멈춘 순간이면 그 까닭) · 평가 횟수 · 멈춤 · 고장 · 기다리는 요청 · 이름표 없음 · 편집본과 다름 · 저장하지 않은 편집.
    /// 대상이 없으면 이름표 알림 (NoTargetText) 또는 빈 글
    /// </summary>
    private string RootDebugText(BehaviorDebugFrame frame, bool otherTree)
    {
        MonsterBase monster = _debug.TargetMonster;
        if (frame == null)
            return _debug.IsTargetValid ? TargetLabel(monster) + NoTreeText(monster) : NoTargetText();
        var text = new StringBuilder(TargetLabel(monster));
        if (frame.Definition.VariantName.Length > 0)
            text.Append(" · 변형 ").Append(frame.Definition.VariantName);
        if (otherTree)
        {
            string treeId = frame.Definition.Id;
            bool listed = _documents.Exists(candidate => string.Equals(candidate.Tree.Id, treeId, StringComparison.Ordinal));
            return text.Append(" — '").Append(treeId).Append(listed ? "' 트리를 씀 (목록에서 그 트리를 고르면 보임)" : "' 트리를 씀 (목록에 없는 트리)").ToString();
        }
        if (ReferenceEquals(frame, _debug.Paused))
            text.Append(" · 멈춘 순간 — ").Append(_debug.PausedReason);
        text.Append(" · 평가 ").Append(frame.EvaluationCount);
        if (frame.IsFaulted)
            text.Append(" · 고장 — 평가 중 예외 (Console)");
        else if (frame.IsStopped)
            text.Append(" · 멈춤 (죽음)");
        if (frame.RequestKind != EBehaviorDebugRequest.None)
            text.Append(" · 요청: ").Append(frame.RequestKind == EBehaviorDebugRequest.Fail ? "실패로 끝냄" : "다시 검색").Append(" (번호 ").Append(frame.RequestNode).Append(')');
        if (RunsKeptVariant(_debug.Target))
            return text.Append(" · 이전 트리 — Play 중 저장한 트리로 이 변형을 만들지 못해 그대로 씀 (Console, 저장해도 입히지 않음 — Play를 다시 시작하면 가만히)").ToString();
        if (_graph != null && _graph.DebugUnlabeled)
            text.Append(UnlabeledTreeNote(_selected != null && _selected.SavedFileMissesNodeIds()));
        else if (_graph != null && _graph.DebugMismatch)
            text.Append(" · 편집본이 실행 중인 트리와 다름 — 이름표가 같은 카드 · 줄만 보임 (저장하면 Play에 입힘)");
        else if (_selected != null && _selected.IsDirty && _selected.HasOnlyLoadFill() == false)
            text.Append(" · 저장하지 않은 편집이 있음 — 실행 중인 트리는 저장한 판 (저장하면 Play에 입힘)");
        return text.ToString();
    }

    /// <summary>
    /// 대상이 지금 쓰는 변형 정의가 지금 기반 정의에서 만든 것이 아닌가 — Play 중 저장한 트리로 그 변형을 만들지 못해 이전 정의를 그대로 쓰는 몬스터 (BehaviorManager.ReloadTree).
    /// 보이는 순간이 아니라 실행기의 지금 정의로 본다 — 멈춘 뒤 저장해 새 정의로 다시 묶였으면 멈춘 순간이 이전 정의여도 아니다
    /// </summary>
    private static bool RunsKeptVariant(BehaviorTreeRunner runner)
    {
        BehaviorTreeDefinition definition = runner != null ? runner.Definition : null;
        if (definition == null || definition.VariantName.Length == 0)
            return false;
        BehaviorTreeDefinition current = Managers.Behavior.GetDefinition(definition.Id);
        return current != null && ReferenceEquals(current.Asset, definition.Asset) == false;
    }

    /// <summary>
    /// 대상에 행동 트리가 없을 때 — 게임이 그 트리 Id를 읽거나 만들지 못해 기억했으면 그 Id (까닭은 Console — 없는 트리 · 변형 · 컴파일 에러),
    /// 아니면 그대로 (트리 Id가 비었거나 아직 묶기 전 — 스폰한 프레임)
    /// </summary>
    private static string NoTreeText(MonsterBase monster)
    {
        string treeId = monster != null ? monster.BehaviorTreeId : null;
        return Managers.Behavior.HasFailed(treeId) ? $" — 행동 트리 없음: '{treeId}'를 쓸 수 없음 (Console)" : " — 행동 트리 없음";
    }

    /// <summary>
    /// ROOT 카드 — 게임이 읽은 트리에 이름표가 빈 노드가 있을 때 (카드 · 줄 · breakpoint는 이름표로 잇는다). fileMissesIds = 트리 파일에도 이름표가 없다 (불러올 때 채운 트리) —
    /// 아니면 파일에는 있는데 게임이 예전 판을 읽었다 (Addressables가 예전 빌드를 읽음 등)
    /// </summary>
    internal static string UnlabeledTreeNote(bool fileMissesIds)
    {
        return fileMissesIds
            ? " · 실행 중인 트리에 노드 이름표가 없음 — 이 트리 파일에 아직 이름표가 없음: [저장]하면 파일에 쓰고 Play에도 입힘 (이 트리의 몬스터는 루트부터 다시 시작, 그 뒤 카드 표시 · breakpoint가 걸림)"
            : " · 실행 중인 트리에 노드 이름표가 없음 — 파일에는 있음: 게임이 예전 판을 읽음 " + OldCopyRemedy;
    }

    /// <summary>게임이 예전 판의 트리를 읽었을 때 할 일</summary>
    private const string OldCopyRemedy = "(Play를 다시 시작 — Addressables Play Mode Script가 'Use Existing Build'면 다시 빌드하거나 'Use Asset Database'로)";

    /// <summary>
    /// 대상이 없을 때 ROOT 글 — 고른 트리의 breakpoint 가운데 Play 중 그 트리를 쓰는 몬스터의 정의 (게임이 읽은 트리)에 없는 이름표가 있으면 알림
    /// (그 breakpoint는 걸리지 않아 대상도 생기지 않는다). 아니면 빈 글. O(몬스터 수 + 노드 수 × breakpoint 수) — 갱신 (0.1초)마다, breakpoint가 있을 때만
    /// </summary>
    private string NoTargetText()
    {
        if (_selected == null || _debug.Breakpoints.CountOf(_selected.Tree.Id) == 0)
            return "";
        BehaviorTreeDefinition running = RunningDefinitionOf(_selected.Tree.Id);
        if (running == null)
            return "";
        int missing = CountMissingBreakpoints(_selected, _debug.Breakpoints, running, _debugNodes, out bool anyUnsaved);
        return MissingBreakpointsNote(missing, anyUnsaved);
    }

    /// <summary>
    /// 문서의 트리 (루트 부분 트리)에서 breakpoint를 켠 노드 가운데 정의 (게임이 읽은 트리)의 Main 노드에 그 이름표가 없는 수.
    /// anyUnsaved = 그 가운데 트리 파일에도 없는 이름표가 있다 (저장하면 Play에 입힌다). nodes는 담을 곳 (비우고 쓴다). O(노드 수 × breakpoint 수)
    /// </summary>
    internal static int CountMissingBreakpoints(BehaviorTreeDocumentAsset document, BehaviorBreakpoints breakpoints, BehaviorTreeDefinition running, List<BehaviorNode> nodes, out bool anyUnsaved)
    {
        anyUnsaved = false;
        nodes.Clear();
        if (document.Tree.Root == null)
            return 0;
        BehaviorTreeEditing.CollectSubtreeNodes(document.Tree.Root, nodes);
        int missing = 0;
        for (int i = 0; i < nodes.Count; i++)
        {
            string nodeId = nodes[i].NodeId;
            if (breakpoints.Contains(document.Tree.Id, nodeId) == false || BehaviorTreeGraphView.HasMainNodeId(running, nodeId))
                continue;
            missing++;
            if (document.SavedFileHasNodeId(nodeId) == false)
                anyUnsaved = true;
        }
        return missing;
    }

    /// <summary>대상이 없을 때 ROOT 글 — 걸리지 않을 breakpoint 수와 할 일 (트리 파일에 없는 이름표가 있으면 저장, 모두 파일에 있으면 게임이 예전 판). missing이 0이면 빈 글</summary>
    internal static string MissingBreakpointsNote(int missing, bool anyUnsaved)
    {
        if (missing == 0)
            return "";
        return $"대상 없음 · breakpoint {missing}개가 게임이 읽은 트리에 없음 — " + (anyUnsaved
            ? "이름표가 아직 트리 파일에 없음: [저장]하면 파일에 쓰고 Play에도 입힘 (이 트리의 몬스터는 루트부터 다시 시작)"
            : "파일에는 있음: 게임이 예전 판을 읽음 " + OldCopyRemedy);
    }

    /// <summary>Play 중 그 트리 Id를 쓰는 몬스터의 정의 (게임이 읽은 트리 — 같은 트리의 몬스터는 정의 하나를 함께 쓴다). 없거나 Play가 아니면 null. O(몬스터 수)</summary>
    private static BehaviorTreeDefinition RunningDefinitionOf(string treeId)
    {
        if (string.IsNullOrEmpty(treeId) || DebugMenuGuard.CanUse() == false)
            return null;
        foreach (MonsterBase monster in Managers.Object.Monsters)
        {
            BehaviorTreeDefinition definition = monster != null && monster.BehaviorRunner != null ? monster.BehaviorRunner.Definition : null;
            if (definition != null && string.Equals(definition.Id, treeId, StringComparison.Ordinal))
                return definition;
        }
        return null;
    }

    /// <summary>멈췄을 때 상태 줄</summary>
    private string HeldStatus()
    {
        return $"{_debug.PausedReason} — {TargetLabel(_debug.TargetMonster)}에서 멈춤 (Unity Pause). [멈춘 순간]을 끄면 지금 모습 (프레임 끝), [다음 평가] = 한 평가 더";
    }

    /// <summary>"이름 (templateID) #스폰 번호" — 몬스터가 없으면 "대상"</summary>
    private static string TargetLabel(MonsterBase monster)
    {
        return monster != null ? $"{monster.name} ({monster.TemplateId}) #{monster.SpawnSerial}" : "대상";
    }

    /// <summary>Scene 뷰 — 켜져 있고 보일 모습이 있으면 (그래프가 다른 트리를 보여도) 그린다</summary>
    private void UpdateDebugSceneView(BehaviorDebugFrame frame)
    {
        if (_debugSceneView == false || frame == null)
        {
            BehaviorDebugSceneView.Hide();
            return;
        }
        BehaviorDebugSceneView.Show(frame);
        SceneView.RepaintAll();
    }
    #endregion

    #region Hierarchy · Scene 고르기
    /// <summary>Play 중 고른 것이 몬스터 (또는 그 아래)면 그 몬스터를 대상으로 — 창도 그 몬스터의 트리를 고른다</summary>
    private void OnDebugSelectionChanged()
    {
        if (DebugMenuGuard.CanUse() == false)
            return;
        GameObject chosen = Selection.activeGameObject;
        MonsterBase monster = chosen != null ? chosen.GetComponentInParent<MonsterBase>() : null;
        if (monster == null || monster.BehaviorRunner == null)
            return;
        bool dropped = ChooseTarget(monster.BehaviorRunner);
        string missing = ShowTreeOfTarget();
        if (dropped && missing != null)
            SetStatus(DroppedMomentStatus(monster.BehaviorRunner) + " · " + missing, true);
        else if (dropped)
            SetStatus(DroppedMomentStatus(monster.BehaviorRunner));
        else if (missing != null)
            SetStatus(missing, true);
        DebugTick();
    }

    /// <summary>
    /// 대상의 트리를 목록에서 고른다 (이미 보이면 그대로) — 입력 중이던 칸의 확정을 먼저 요청하고 (누름 없이 오는 전환 — 멈춘 순간 — 에서도) 한 틱 뒤에 고른다 (AfterInputCommit).
    /// 목록에 없으면 상태 줄에 쓸 글, 아니면 null
    /// </summary>
    private string ShowTreeOfTarget()
    {
        BehaviorTreeRunner runner = _debug.Target;
        MonsterBase monster = _debug.TargetMonster;
        string treeId = runner != null && runner.Definition != null ? runner.Definition.Id
            : monster != null ? BehaviorTreeVariants.BaseIdOf(monster.BehaviorTreeId) : null;
        if (string.IsNullOrEmpty(treeId) || (_selected != null && string.Equals(_selected.Tree.Id, treeId, StringComparison.Ordinal)))
            return null;
        BehaviorTreeDocumentAsset document = _documents.Find(candidate => string.Equals(candidate.Tree.Id, treeId, StringComparison.Ordinal));
        if (document == null)
            return $"대상의 트리 '{treeId}'가 목록에 없습니다";
        EditorTextInput.CommitFocused(rootVisualElement);
        AfterInputCommit(() =>
        {
            SelectItem(document);
            RefreshTreeList();
        });
        return null;
    }
    #endregion

    #region breakpoint
    /// <summary>그래프 위 [breakpoint 끄기 (n)] — 이 트리의 breakpoint가 있을 때만 보인다</summary>
    private Button BuildClearBreakpointsButton()
    {
        _clearBreakpointsButton = new Button(ClearBreakpoints) { tooltip = "이 트리의 breakpoint를 모두 끈다 (이 PC에만 남는 표시)" };
        _clearBreakpointsButton.style.display = DisplayStyle.None;
        return _clearBreakpointsButton;
    }

    private bool HasBreakpoint(BehaviorMainNode node)
    {
        return _debug != null && _graph != null && _graph.Document != null && _debug.Breakpoints.Contains(_graph.Document.Tree.Id, node.NodeId);
    }

    /// <summary>카드 메뉴 'breakpoint' — 트리 Id + 노드 이름표로 켜고 끈다. 켤 때 걸리지 않을 까닭이 있으면 (BreakpointBlocker) 상태 줄에 알린다</summary>
    private void ToggleBreakpoint(BehaviorMainNode node)
    {
        if (_debug == null || _graph == null || _graph.Document == null)
            return;
        BehaviorTreeDocumentAsset document = _graph.Document;
        bool on = _debug.Breakpoints.Toggle(document.Tree.Id, node.NodeId);
        UpdateClearBreakpointsButton();
        if (on == false)
        {
            SetStatus($"breakpoint 끔 — '{node.NodeId}'");
            return;
        }
        BehaviorTreeDefinition running = RunningDefinitionOf(document.Tree.Id);
        string blocker = BreakpointBlocker(BehaviorTreeEditing.IsInTree(document, node), document.SavedFileHasNodeId(node.NodeId),
            running != null ? BehaviorTreeGraphView.HasMainNodeId(running, node.NodeId) : (bool?)null);
        if (blocker == null)
            SetStatus($"breakpoint 켬 — '{node.NodeId}' (Play 중 이 노드에 들어가면 Unity를 멈춘다)");
        else
            SetStatus($"breakpoint 켬 — '{node.NodeId}' · {blocker}", true);
    }

    /// <summary>
    /// 켠 breakpoint가 걸리지 않을 까닭 — 트리에 이어지지 않은 노드 (실행하지 않는다 — 게임이 읽은 트리에는 있으면 지금은 걸리지만 저장하면 걸리지 않는다) ·
    /// 트리 파일에 없는 이름표 (게임은 저장한 트리를 읽는다) · 게임이 읽은 트리에 없는 이름표 (runningHasId — Play 중 그 트리를 쓰는 몬스터가 없거나 Play가 아니면 null).
    /// 걸릴 수 있으면 null
    /// </summary>
    internal static string BreakpointBlocker(bool inTree, bool fileHasId, bool? runningHasId)
    {
        if (inTree == false)
            return runningHasId == true
                ? "편집본에서는 트리에 이어지지 않은 노드: 지금 Play (게임이 읽은 트리)에서는 걸리지만, 이대로 저장하면 걸리지 않음"
                : "트리에 이어지지 않은 노드: 이어야 걸림 (이어지지 않은 노드는 실행하지 않는다)";
        if (fileHasId == false)
            return "이 이름표는 아직 트리 파일에 없음: [저장]해야 Play에서 걸림 (게임은 저장한 트리를 읽는다)";
        if (runningHasId == false)
            return "게임이 읽은 트리에 이 이름표가 없음 — 파일에는 있음: 게임이 예전 판을 읽음 " + OldCopyRemedy;
        return null;
    }

    private void ClearBreakpoints()
    {
        if (_selected == null)
            return;
        _debug.Breakpoints.Clear(_selected.Tree.Id);
        _graph?.RefreshBreakpoints();
        UpdateClearBreakpointsButton();
        SetStatus($"'{LabelOf(_selected)}'의 breakpoint를 모두 껐습니다");
    }

    private void UpdateClearBreakpointsButton()
    {
        if (_clearBreakpointsButton == null)
            return;
        int count = _selected != null ? _debug.Breakpoints.CountOf(_selected.Tree.Id) : 0;
        _clearBreakpointsButton.text = $"breakpoint 끄기 ({count})";
        _clearBreakpointsButton.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }
    #endregion

    #region Blackboard 탭의 지금 값
    /// <summary>Blackboard 탭 위 — 보이는 모습의 키 값 (보이는 모습이 없으면 감춤). 탭을 다시 만들 때마다 새로</summary>
    private VisualElement BuildDebugKeysView()
    {
        _debugKeysView = new VisualElement();
        _debugKeysView.style.marginBottom = 6;
        _debugKeysView.style.paddingLeft = 6;
        _debugKeysView.style.paddingRight = 6;
        _debugKeysView.style.paddingTop = 4;
        _debugKeysView.style.paddingBottom = 4;
        _debugKeysView.style.backgroundColor = new Color(1f, 0.85f, 0.45f, 0.08f);
        UpdateDebugKeysView();
        return _debugKeysView;
    }

    /// <summary>맨 위 글 = 무엇의 값인가, 그 아래 키마다 "이름 (형식) = 값 · 판 n"</summary>
    private void UpdateDebugKeysView()
    {
        VisualElement view = _debugKeysView;
        if (view == null)
            return;
        BehaviorDebugFrame frame = _shownDebugFrame;
        if (frame == null)
        {
            view.style.display = DisplayStyle.None;
            return;
        }
        view.style.display = DisplayStyle.Flex;
        int lines = frame.Keys.Count + 1;
        while (view.childCount < lines)
        {
            var line = new Label();
            line.style.fontSize = 11;
            line.style.whiteSpace = WhiteSpace.Normal;
            view.Add(line);
        }
        while (view.childCount > lines)
            view.RemoveAt(view.childCount - 1);

        var title = (Label)view[0];
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = BehaviorGraphStyle.DebugText;
        title.text = (ReferenceEquals(frame, _debug.Paused) ? "멈춘 순간의 값 — " : "지금 값 — ") + TargetLabel(_debug.TargetMonster);
        for (int i = 0; i < frame.Keys.Count; i++)
        {
            BehaviorDebugKey key = frame.Keys[i];
            ((Label)view[i + 1]).text = $"{key.Name} ({key.Type}) = {key.Value} · 판 {key.Version}";
        }
    }
    #endregion

    #region Play 중 저장한 트리
    /// <summary>저장한 트리 하나 — Play 중이면 저장이 끝난 뒤 몬스터에 입힐 차례에 둔다 (ApplyPlayReloads). Id가 없는 트리는 쓰는 몬스터가 없다</summary>
    private void QueuePlayReload(string treeId, string treeText)
    {
        if (DebugMenuGuard.CanUse() && string.IsNullOrEmpty(treeId) == false)
            _playReloads.Add(new KeyValuePair<string, string>(treeId, treeText));
    }

    /// <summary>
    /// 저장한 트리를 Play 중 몬스터에 입힌다 (BehaviorManager.ReloadTree — 다시 컴파일해 그 트리 · 그 변형을 쓰는 몬스터를 새 정의로 다시 묶음, 루트부터).
    /// 상태 줄에 붙일 글 (입힐 것이 없으면 "") — 컴파일에 실패한 트리 (옛 정의 그대로) · 입히다 예외가 난 트리 (정의는 새 것, 몬스터는 일부만 새 트리일 수 있음) ·
    /// 새 트리로 만들지 못한 변형의 몬스터 (하던 트리 그대로)가 있으면 isError (문제는 Console)
    /// </summary>
    private string ApplyPlayReloads(out bool isError)
    {
        isError = false;
        if (_playReloads.Count == 0)
            return "";
        if (DebugMenuGuard.CanUse() == false)
        {
            _playReloads.Clear();
            return "";
        }

        var messages = new List<BehaviorCompileMessage>();
        var failed = new List<string>(); // 컴파일 실패
        var thrown = new List<string>(); // 입히다 예외
        int trees = 0, monsters = 0, unchanged = 0;
        try
        {
            for (int i = 0; i < _playReloads.Count; i++)
            {
                string treeId = _playReloads[i].Key;
                int count;
                int kept;
                try
                {
                    count = Managers.Behavior.ReloadTree(treeId, _playReloads[i].Value, messages, out kept);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    thrown.Add(treeId);
                    continue;
                }
                if (count < 0)
                {
                    failed.Add(treeId);
                    for (int m = 0; m < messages.Count; m++)
                        Debug.LogError($"[{ToolName}] Play에 입히지 못한 트리 '{treeId}' — {messages[m]}");
                    continue;
                }
                trees++;
                monsters += count;
                unchanged += kept;
            }
        }
        finally
        {
            _playReloads.Clear(); // 한 번만 입힌다 — 실패한 것도 다음 저장 때 다시 입히지 않는다
        }

        string text = trees > 0 ? $" · Play: 몬스터 {monsters}마리에 새 트리 (루트부터)" : "";
        if (unchanged > 0)
            text += $" · 새 트리로 변형을 만들지 못한 몬스터 {unchanged}마리는 그대로 (Console)";
        if (failed.Count > 0)
            text += $" · Play에 입히지 못함: {string.Join(", ", failed)} (컴파일 에러 — Console)";
        if (thrown.Count > 0)
            text += $" · Play에 입히다 예외: {string.Join(", ", thrown)} (일부 몬스터만 새 트리일 수 있음 — Console)";
        isError = failed.Count > 0 || thrown.Count > 0 || unchanged > 0;
        return text;
    }
    #endregion
}