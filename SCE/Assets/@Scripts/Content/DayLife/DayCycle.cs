/// <summary>
/// 일차 진행 담당. 일차 값을 들고 있지 않다 — 원본은 GameStateManager의 sys.day.
/// DayAdvancedEvent는 이 클래스만 발생시킨다.
/// </summary>
public sealed class DayCycle
{
    private GameStateManager _state { get { return Managers.GameState; } }

    // event버스가 처리 중일 때 발생시켜 아직 처리되지 않았을 수 있는 마지막 일차 (0 => 없음)
    private int _queuedDay;

    public int CurrentDay { get { return _state != null ? _state.CurrentDay : 0; } }

    public void Init()
    {
        _queuedDay = 0;
    }

    /// <summary>
    /// 다음 일차로 진행. 버스가 유휴 상태면 반환 시점에 모든 처리가 끝나 있고,
    /// 처리 중에 호출됐다면 현재 이벤트가 끝난 뒤 순서대로 처리된다.
    /// </summary>
    public bool AdvanceDay()
    {
        if (_state == null || _state.IsLoaded == false)
        {
            LogPrinter.LogError("[DayCycle] 상태 정의 로드 전 — 일차 진행 불가");
            return false;
        }

        // 버스가 유휴면 대기 중인 DayAdvanced가 있을 수 없다 → 저장소 값이 정확하다
        if (GameEventBus.IsFlushing == false)
            _queuedDay = 0;

        int from = _queuedDay > 0 ? _queuedDay : _state.CurrentDay;
        int to = from + 1;
        _queuedDay = to;

        GameEventBus.Raise(new DayAdvancedEvent(from, to));
        return true;
    }

    /// <summary>여러 일차 진행. 하루씩 이벤트를 발생시켜 일차별 규칙·리스너가 빠짐없이 실행되게 한다.</summary>
    public void AdvanceDays(int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (AdvanceDay() == false)
                return;
        }
    }
}