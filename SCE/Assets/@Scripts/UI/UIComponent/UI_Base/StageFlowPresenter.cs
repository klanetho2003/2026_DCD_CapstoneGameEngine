using System;
using Data;
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
///   RoundResult : 기존 UI_RoundEndPopup 유지
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
        _selectUI.SetInfo(_onStageSelected);
        _selectUI.Hide(); // 상태가 Selecting이 될 때 연다

        _hud = Managers.UI.ShowBaseUI<UI_Panel_HUD>();
        _hud.gameObject.SetActive(false); // Playing 일 때 연다

        UI_Panel_Paused.StageSelectRequested = OnPausedStageSelect;

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

        Time.timeScale = 1f;

        _selectUI = null;
        _hud = null;
        _resultPopup = null;
        _clearPopup = null;
        _diedPopup = null;
        _retryInfo = null;
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
                    Managers.Stage.RequestEnter(info);
                    break;
                }
                SetHudVisible(false);
                _selectUI.Show();
                break;

            case EStagePhase.Playing:
                SetHudVisible(true);
                PlayRoundIntro(Managers.Stage.CurrentRound);
                break;

            case EStagePhase.RoundResult:
                {
                    // popup은 이번 라운드 점령 수와 누적 점수를 표시할 기존 결과 창이다.
                    UI_RoundEndPopup popup = Managers.UI.ShowPopupUI<UI_RoundEndPopup>();
                    popup.SetInfo(Managers.Stage.CurrentRound, _onResultConfirmed,
                        Managers.Stage.LastRoundScore.PlayerTiles, Managers.Stage.TotalPlayerTiles);
                    _resultPopup = popup;
                    break;
                }

            case EStagePhase.StageResult:
                if (Managers.Stage.Result == EStageResult.Cleared)
                {
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

    /// <summary>라운드 시작 연출: VS(2초) → 3-2-1-GO. 연출 동안 게임 시간을 멈춘다 (라운드 타이머는 Time.deltaTime 기준).</summary>
    private void PlayRoundIntro(ERound round)
    {
        Time.timeScale = 0f;

        Managers.UI.ShowPopupUI<UI_Panel_StageStart>().SetInfo(round, () =>
        {
            if (_isBound == false) { Time.timeScale = 1f; return; }

            Managers.UI.ShowPopupUI<UI_Panel_Countdown>().Play(() =>
            {
                Time.timeScale = 1f; // GO! — 게임 진행
            });
        });
    }

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
        Time.timeScale = 1f;
        Managers.Scene.LoadAsyncScene(EScene.GameScene, () => CoroutineFade.FadeIn(), true);
    }
}
