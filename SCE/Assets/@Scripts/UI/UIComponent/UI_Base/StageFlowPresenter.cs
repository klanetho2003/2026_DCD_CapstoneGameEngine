using System;
using Data;
using Unity.Cinemachine;
using UnityEngine;
using static Define;

/// <summary>
/// StageManager의 상태를 UI로 옮긴다. GameScene이 소유한다.
///
/// 규칙: 상태가 바뀌면 "이전 상태의 UI를 닫고 → 새 상태의 UI를 연다".
///       어떤 전이든 이 두 단계로 처리되므로 전이마다 따로 코드를 두지 않는다.
/// 의존 방향: UI → Presenter → StageManager. StageManager는 이 클래스를 모른다.
///
/// 사용 순서: SetInfo() → Managers.Stage.OpenSelect() ... 씬을 떠날 때 Clear() → Managers.Stage.Clear()
///
/// [UI 교체] 도트 UI(UI_Panel_*)로 연결
///   Selecting   : UI_Panel_PlanetSelect (기존 UI_StageSelect 자리)
///   Playing     : UI_Panel_HUD + 라운드 시작 때 UI_Panel_StageStart(VS) → UI_Panel_Countdown. 그동안 timeScale 0 (타이머 정지)
///   RoundResult : 결과 창 없이 바로 다음 라운드 (다음 라운드 정보창이 뜬다)
///   StageResult : 성공 UI_Panel_Clear / 실패 UI_Panel_YouDied (다시 도전 = 같은 Stage 재진입)
///   일시정지 "스테이지 선택" : GameScene 다시 로드 → Stage 선택으로
/// </summary>
public sealed class StageFlowPresenter
{
    // 1회 캐싱 — 구독·해제에 같은 인스턴스를 쓴다
    private readonly Action<EStagePhase, EStagePhase> _onPhaseChanged;
    private readonly Action<StageInfoData> _onStageSelected;
    private readonly Action _onResultConfirmed;

    private UI_Panel_PlanetSelect _selectUI;
    private UI_Panel_HUD _hud;
    private UI_ConfirmPopup _resultPopup;   // 라운드 결과 Popup (기존)
    private UI_Panel_Clear _clearPopup;     // 지금 떠 있는 CLEAR
    private UI_Panel_YouDied _diedPopup;    // 지금 떠 있는 YOU DIED
    private StageInfoData _retryInfo;       // "다시 도전" 으로 다시 들어갈 Stage
    private bool _isBound;

    public StageFlowPresenter()
    {
        _onPhaseChanged = OnPhaseChanged;
        _onStageSelected = OnStageSelected;
        _onResultConfirmed = OnResultConfirmed;
    }

    public void SetInfo()
    {
        if (_isBound)
            return;
        _isBound = true;

        _selectUI = Managers.UI.ShowBaseUI<UI_Panel_PlanetSelect>(sortOrder: UI_Panel_PlanetSelect.SORT_ORDER);
        _selectUI.SetInfo(_onStageSelected, OnSelectBack);
        _selectUI.Hide(); // 상태가 Selecting이 될 때 연다

        _hud = Managers.UI.ShowBaseUI<UI_Panel_HUD>();
        _hud.gameObject.SetActive(false); // Playing 일 때 연다

        UI_Panel_Paused.StageSelectRequested = OnPausedStageSelect;
        UI_Panel_Paused.RestartRequested = OnPausedRestart;

        // "스테이지 재시작"으로 씬을 다시 연 경우: 선택 화면 없이 같은 Stage 로 (아래 ShowUI(Selecting) 이 처리)
        if (s_restartInfo != null)
        {
            _retryInfo = s_restartInfo;
            s_restartInfo = null;
        }

        // 일시정지 "스테이지 선택"으로 씬을 다시 연 경우: 행성 선택 화면에서 발사 연출
        if (s_launchPlanet >= 0)
        {
            _launchPlanet = s_launchPlanet;
            s_launchPlanet = -1;
        }

        Managers.Stage.PhaseChanged += _onPhaseChanged;
        ShowUI(Managers.Stage.Phase); // 이미 진행 중인 상태에 붙는 경우 대비
    }

    /// <summary>씬을 떠날 때. 매니저는 씬보다 오래 살므로 구독을 반드시 해제한다. UI 오브젝트는 씬과 함께 정리된다.</summary>
    public void Clear()
    {
        if (_isBound == false)
            return;
        _isBound = false;

        if (Managers.Stage != null)
            Managers.Stage.PhaseChanged -= _onPhaseChanged;

        if (UI_Panel_Paused.StageSelectRequested == (Action)OnPausedStageSelect)
            UI_Panel_Paused.StageSelectRequested = null;
        if (UI_Panel_Paused.RestartRequested == (Action)OnPausedRestart)
            UI_Panel_Paused.RestartRequested = null;

        Time.timeScale = 1f;
        EndIntroCamera();

        _isLeaving = false;
        _selectUI = null;
        _hud = null;
        _resultPopup = null;
        _clearPopup = null;
        _diedPopup = null;
        _retryInfo = null;
        _launchPlanet = -1;
    }

    private void OnPhaseChanged(EStagePhase previous, EStagePhase next)
    {
        HideUI(previous);
        ShowUI(next);
    }

    private void HideUI(EStagePhase phase)
    {
        switch (phase)
        {
            case EStagePhase.Selecting:
                _selectUI.Hide();
                break;

            case EStagePhase.RoundResult:
                // 정상 경로에서는 버튼을 누른 순간 이미 닫혀 있다.
                // 버튼 없이 상태가 바뀐 경우(Popup이 떠 있는 동안 사망)만 여기서 닫힌다
                if (_resultPopup != null)
                {
                    _resultPopup.Dismiss();
                    _resultPopup = null;
                }
                break;

            case EStagePhase.StageResult:
                if (_clearPopup != null)
                {
                    _clearPopup.Dismiss();
                    _clearPopup = null;
                }
                if (_diedPopup != null)
                {
                    _diedPopup.Dismiss();
                    _diedPopup = null;
                }
                SetHudVisible(false);
                break;
        }
    }

    /// <summary>스테이지 상태에 맞는 화면을 열고 결과 팝업에 확정된 점령 점수를 전달한다.</summary>
    /// <param name="phase">표시할 선택 또는 결과 화면의 기준이 되는 현재 스테이지 상태다.</param>
    private void ShowUI(EStagePhase phase)
    {
        switch (phase)
        {
            case EStagePhase.Selecting:
                // "다시 도전" 이면 선택 화면 없이 같은 Stage 로 바로 들어간다
                if (_retryInfo != null)
                {
                    StageInfoData info = _retryInfo;
                    _retryInfo = null;
                    _launchPlanet = -1; // 같은 Stage 로 바로 들어가므로 발사 연출 없음
                    Managers.Stage.RequestEnter(info);
                    break;
                }
                SetHudVisible(false);
                _selectUI.Show();

                // 스테이지에서 돌아온 경우: 그 행성에서 우주선이 떠나는 연출 뒤에 행성 선택
                if (_launchPlanet >= 0)
                    _selectUI.PlayLaunch(_launchPlanet);
                _launchPlanet = -1;
                break;

            case EStagePhase.Playing:
                SetHudVisible(true);
                if (_hud != null) // 왼쪽 위 행성 아이콘·이름, 라운드 번호
                    _hud.SetStage(Managers.Stage.CurrentStageInfo, Managers.Stage.CurrentRound);
                PlayRoundIntro(Managers.Stage.CurrentRound);
                break;

            case EStagePhase.RoundResult:
                // 라운드 사이 결과 창 없이 바로 다음 라운드로 →
                // Playing 이 되면 다음 라운드 정보창(라운드 번호·행성·시간) → 3-2-1 이 뜬다.
                // (점수는 StageManager 가 이미 누적했다. 예전 결과 창 UI_RoundEndPopup 은 쓰지 않음)
                Managers.Stage.RequestProceed();
                break;

            case EStagePhase.StageResult:
                // 행성 선택으로 돌아갈 때 발사 연출에 쓸 행성 (Stage 를 나가면 Stage 정보가 지워지므로 지금 기억)
                _launchPlanet = StagePlanet.Current();

                if (Managers.Stage.Result == EStageResult.Cleared)
                {
                    // 다음 행성 해금용 기록 (행성 선택 화면이 읽는다)
                    if (Managers.Stage.CurrentStageInfo != null)
                        PlanetProgress.MarkCleared(Managers.Stage.CurrentStageInfo.StageId);

                    UI_Panel_Clear popup = Managers.UI.ShowPopupUI<UI_Panel_Clear>();
                    popup.SetInfo(Managers.Stage.TotalPlayerTiles, 0, _onResultConfirmed);
                    _clearPopup = popup;
                }
                else
                {
                    UI_Panel_YouDied popup = Managers.UI.ShowPopupUI<UI_Panel_YouDied>();
                    popup.SetInfo(OnRetry, _onResultConfirmed);
                    _diedPopup = popup;
                }
                break;
        }
    }

    /// <summary>라운드 시작 연출: VS(3초) → 3-2-1-GO. 연출 동안 게임 시간을 멈춘다 (라운드 타이머는 Time.deltaTime 기준).</summary>
    private void PlayRoundIntro(ERound round)
    {
        Time.timeScale = 0f;
        BeginIntroCamera();

        Managers.UI.ShowPopupUI<UI_Panel_StageStart>().SetInfo(round, () =>
        {
            if (_isBound == false) { EndIntroCamera(); Time.timeScale = 1f; return; }

            Managers.UI.ShowPopupUI<UI_Panel_Countdown>().Play(() =>
            {
                EndIntroCamera();
                Time.timeScale = 1f; // GO! — 게임 진행
            });
        });
    }

    #region 연출 중 카메라
    // 씬의 CinemachineBrain 은 FixedUpdate 갱신이라 timeScale 0 이면 카메라가 아예 갱신되지 않는다
    // → 3-2-1 동안 맵을 불러온 직후의 위치에 머물다가 GO 뒤에야 플레이어로 이동했다.
    // 연출 동안만 LateUpdate 로 돌리고, "이전 상태 무효"로 감쇠 없이 플레이어(라운드 영역 안)에 바로 맞춘다.
    private CinemachineBrain _brain;
    private CinemachineBrain.UpdateMethods _savedUpdateMethod;
    private CinemachineBrain.BrainUpdateMethods _savedBlendUpdateMethod;
    private bool _isCameraOverridden;

    private void BeginIntroCamera()
    {
        Camera main = Camera.main;
        _brain = main != null ? main.GetComponent<CinemachineBrain>() : null;

        if (_brain != null && _isCameraOverridden == false)
        {
            _savedUpdateMethod = _brain.UpdateMethod;
            _savedBlendUpdateMethod = _brain.BlendUpdateMethod;
            _brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
            _brain.BlendUpdateMethod = CinemachineBrain.BrainUpdateMethods.LateUpdate;
            _isCameraOverridden = true;
        }

        CameraManager cameraManager = Managers.Camera;
        if (cameraManager != null && cameraManager.FollowCamera != null)
            cameraManager.FollowCamera.PreviousStateIsValid = false;
    }

    private void EndIntroCamera()
    {
        if (_isCameraOverridden && _brain != null)
        {
            _brain.UpdateMethod = _savedUpdateMethod;
            _brain.BlendUpdateMethod = _savedBlendUpdateMethod;
        }
        _isCameraOverridden = false;
        _brain = null;
    }
    #endregion

    private void SetHudVisible(bool visible)
    {
        if (_hud != null)
            _hud.gameObject.SetActive(visible);
    }

    /// <summary>Stage 선택 UI의 트리거 — 고른 Stage의 정보를 그대로 넘긴다.</summary>
    private void OnStageSelected(StageInfoData info)
    {
        Managers.Stage.RequestEnter(info);
    }

    /// <summary>행성 선택 Back — 타이틀로 돌아간다. 페이드는 타이틀 → 게임과 같은 0.8초.</summary>
    private const float BackFadeDuration = 0.8f;
    private bool _isLeaving;

    private void OnSelectBack()
    {
        if (_isLeaving)
            return; // 페이드 중에 다시 눌러도 한 번만
        _isLeaving = true;

        CoroutineFade.FadeOut(() =>
        {
            Managers.Scene.LoadAsyncScene(EScene.TitleScene, () => CoroutineFade.FadeIn(null, BackFadeDuration), false);
        }, BackFadeDuration);
    }

    /// <summary>결과 Popup들이 같은 요청을 보낸다. 다음 라운드인지 Stage 선택인지는 StageManager의 상태가 정한다.</summary>
    private void OnResultConfirmed()
    {
        Managers.Stage.RequestProceed();
    }

    /// <summary>YOU DIED "다시 도전하기" — Stage 선택으로 나간 뒤 같은 Stage 로 바로 재진입.</summary>
    private void OnRetry()
    {
        _retryInfo = Managers.Stage.CurrentStageInfo;
        Managers.Stage.RequestProceed();
    }

    /// <summary>일시정지 "스테이지 선택" — 진행 중인 판을 버리고 GameScene 을 다시 연다 (들어오면 Stage 선택부터).</summary>
    private void OnPausedStageSelect()
    {
        s_launchPlanet = StagePlanet.Current(); // 새 씬의 행성 선택 화면에서 발사 연출
        ReloadGameScene();
    }

    /// <summary>행성 선택 화면에서 발사 연출을 보여 줄 행성. -1 이면 연출 없음 (처음 들어올 때, 재시작).</summary>
    private int _launchPlanet = -1;
    private static int s_launchPlanet = -1; // 씬을 다시 여는 경우 새 Presenter 로 넘기는 값

    /// <summary>
    /// 일시정지 "스테이지 재시작" — 진행 중인 판을 버리고 GameScene 을 다시 연 뒤, 선택 화면 없이 같은 Stage 1라운드부터.
    /// 씬을 다시 열면 이 Presenter 는 새로 만들어지므로 다시 들어갈 Stage 를 static 으로 넘긴다.
    /// </summary>
    private static StageInfoData s_restartInfo;

    private void OnPausedRestart()
    {
        s_restartInfo = Managers.Stage.CurrentStageInfo;
        ReloadGameScene();
    }

    /// <summary>GameScene 다시 열기. 페이드는 타이틀 → 게임과 같은 0.8초.</summary>
    private void ReloadGameScene()
    {
        if (_isLeaving)
            return;
        _isLeaving = true;

        Time.timeScale = 1f;
        CoroutineFade.FadeOut(() =>
        {
            Managers.Scene.LoadAsyncScene(EScene.GameScene, () => CoroutineFade.FadeIn(null, BackFadeDuration), false);
        }, BackFadeDuration);
    }
}
