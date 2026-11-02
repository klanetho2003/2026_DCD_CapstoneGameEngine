using System.Text;

/// <summary>
/// 앞 몇 개만 이름으로 적는 목록 글자 — "a · b · c 외 2개" (Game State Monitor 위 패널의 요약 · 충돌 · 덮어쓰기 · 프리팹에 없는 값, 도구 탭 받는 규칙, 맵 타일 갱신 보고의 종류 목록, 맵 Export Set 머리말의 종류 · 크리처 목록, 신호 규칙 카드의 내는 상호작용 · 신호 발생 효과의 받는 규칙 (단위 '곳'), Set을 쓰는 곳, Behavior Tree 붙이기의 더한 키 · 그대로 둔 키).
/// 항목마다 Next()가 true면 부르는 쪽이 그 항목 글자를 붙인다 (구분자는 Next가 붙인다). 값 형식이라 지역 변수로만 쓴다 (복사하면 수가 따로 센다). 할당 없음.
/// </summary>
public struct EditorShownList
{
    private readonly StringBuilder _into;
    private readonly int _maxShown;
    private readonly string _separator;
    private readonly string _unit;

    /// <summary>Next로 센 항목 수 (적지 않은 것 포함).</summary>
    public int Count { get; private set; }

    /// <param name="unit">" 외 n" 뒤에 붙이는 단위 — 기본 "개"</param>
    public EditorShownList(StringBuilder into, int maxShown, string separator, string unit = "개")
    {
        _into = into;
        _maxShown = maxShown;
        _separator = separator;
        _unit = unit;
        Count = 0;
    }

    /// <summary>항목 하나를 센다 — 앞 maxShown개 안이면 구분자를 붙이고 true (부르는 쪽이 그 항목 글자를 붙인다). O(1)</summary>
    public bool Next()
    {
        Count++;
        if (Count > _maxShown)
            return false;
        if (Count > 1)
            _into.Append(_separator);
        return true;
    }

    /// <summary>적지 않은 항목이 있으면 " 외 n개" (단위는 만들 때 정한 것) — total = 전체 수 (앞 몇 개만 세고 멈췄으면 그 전체, 끝까지 셌으면 Count). O(1)</summary>
    public void AppendRest(int total)
    {
        if (total > _maxShown)
            _into.Append(" 외 ").Append(total - _maxShown).Append(_unit);
    }
}