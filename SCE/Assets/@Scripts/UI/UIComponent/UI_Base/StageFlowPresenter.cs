using System;
using Data;
using static Define;

/// <summary>
/// StageManager의 상태를 UI로 옮긴다. GameScene이 소유한다.
///
/// 규칙: 상태가 바뀌면 "이전 상태의 UI를 닫고 → 새 상태의 UI를 연다".
///       어떤 전이든 이 두 단계로 처리되므로 전이마다 따로 코드를 두지 않는다.
/// 의존 방향: UI → Presenter → StageManager. StageManager는 이 클래스를 모른다.
///
/// 사용 순서: SetInfo() → Managers.Stage.OpenSelect() ... 씬을 떠날 때 Clear() → Managers.Stage.Clear()
/// </summary>
public sealed class StageFlowPresenter
{
    // 1회 캐싱 — 구독·해제에 같은 인스턴스를 쓴다
    private readonly Action<EStagePhase, EStagePhase> _onPhaseChanged;
    private readonly Action<StageInfoData> _onStageSelected;
    private readonly Action _onResultConfirmed;

    private UI_StageSelect _selectUI;
    private UI_ConfirmPopup _resultPopup; // 지금 떠 있는 결과 Popup
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

        _selectUI = Managers.UI.ShowBaseUI<UI_StageSelect>(sortOrder: UI_StageSelect.SORT_ORDER);
        _selectUI.SetInfo(_onStageSelected);
        _selectUI.Hide(); // 상태가 Selecting이 될 때 연다

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

        _selectUI = null;
        _resultPopup = null;
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
            case EStagePhase.StageResult:
                // 정상 경로에서는 버튼을 누른 순간 이미 닫혀 있다.
                // 버튼 없이 상태가 바뀐 경우(Popup이 떠 있는 동안 사망)만 여기서 닫힌다
                if (_resultPopup != null)
                {
                    _resultPopup.Dismiss();
                    _resultPopup = null;
                }
                break;
        }
    }

    private void ShowUI(EStagePhase phase)
    {
        switch (phase)
        {
            case EStagePhase.Selecting:
                _selectUI.Show();
                break;

            case EStagePhase.RoundResult:
                {
                    UI_RoundEndPopup popup = Managers.UI.ShowPopupUI<UI_RoundEndPopup>();
                    popup.SetInfo(Managers.Stage.CurrentRound, _onResultConfirmed);
                    _resultPopup = popup;
                    break;
                }

            case EStagePhase.StageResult:
                {
                    UI_GameEndPopup popup = Managers.UI.ShowPopupUI<UI_GameEndPopup>();
                    popup.SetInfo(Managers.Stage.Result, _onResultConfirmed);
                    _resultPopup = popup;
                    break;
                }
        }
    }

    /// <summary>Stage 선택 UI의 트리거 — 고른 Stage의 정보를 그대로 넘긴다.</summary>
    private void OnStageSelected(StageInfoData info)
    {
        Managers.Stage.RequestEnter(info);
    }

    /// <summary>두 결과 Popup이 같은 요청을 보낸다. 다음 라운드인지 Stage 선택인지는 StageManager의 상태가 정한다.</summary>
    private void OnResultConfirmed()
    {
        Managers.Stage.RequestProceed();
    }
}