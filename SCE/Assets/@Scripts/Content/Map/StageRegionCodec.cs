using System.Collections.Generic;
using System.Text;
using static Define;

/// <summary>
/// MapData.StageRegionRows / StageRegionLegend 형식의 인코더/디코더.
/// Exporter(인코딩)와 런타임(디코딩)이 같은 코드를 써서 형식 불일치를 차단한다.
/// </summary>
public static class StageRegionCodec
{
    public const char NoneSymbol = '.';

    // Key 0~61은 같은 위치의 글자를 쓴다 ex.Stage_3 = '3', Stage_10 = 'A', Stage_36 = 'a'
    private const string Symbols = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const int Undefined = int.MinValue;

    public static int MaxStageCount { get { return Symbols.Length; } }

    #region Encode (Exporter 전용)
    /// <summary>
    /// Stage Key 목록으로 범례를 만든다.
    /// 1차: Key < 62면 Symbols[Key]. 2차: 나머지 Key는 남은 기호를 순서대로.
    /// </summary>
    public static bool TryBuildLegend(IEnumerable<int> stageKeys, Dictionary<string, int> legend, out string error)
    {
        error = null;
        legend.Clear();

        var used = new bool[Symbols.Length];
        var pending = new List<int>();

        foreach (int key in stageKeys)
        {
            if (key >= 0 && key < Symbols.Length)
            {
                used[key] = true;
                legend.Add(Symbols[key].ToString(), key);
            }
            else
            {
                pending.Add(key);
            }
        }

        int cursor = 0;
        for (int i = 0; i < pending.Count; i++)
        {
            while (cursor < Symbols.Length && used[cursor])
                cursor++;

            if (cursor >= Symbols.Length)
            {
                error = $"스테이지 수가 기호 수({Symbols.Length})를 초과 — 맵을 분할할 것";
                return false;
            }

            used[cursor] = true;
            legend.Add(Symbols[cursor].ToString(), pending[i]);
        }
        return true;
    }

    /// <summary>셀 배열(행 우선, y 오름차순) > 문자열 행.</summary>
    public static void Encode(int[] cells, int width, int height, Dictionary<string, int> legend, List<string> rows)
    {
        var symbolOf = new Dictionary<int, char>(legend.Count);
        foreach (var entry in legend)
            symbolOf.Add(entry.Value, entry.Key[0]);

        rows.Clear();
        var sb = new StringBuilder(width);
        for (int y = 0; y < height; y++)
        {
            sb.Clear();
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                int key = cells[row + x];
                sb.Append(key == STAGE_REGION_NONE ? NoneSymbol : symbolOf[key]);
            }
            rows.Add(sb.ToString());
        }
    }
    #endregion

    #region Decode (런타임)
    /// <summary>
    /// 문자열 행 + 범례 → 셀 배열. 행 수·행 길이·미정의 기호를 전부 검증한다.
    /// into.Length는 width * height여야 한다.
    /// </summary>
    public static bool TryDecode(IReadOnlyList<string> rows, IReadOnlyDictionary<string, int> legend,
        int width, int height, int[] into, out string error)
    {
        error = null;

        if (rows == null || legend == null)
        {
            error = "StageRegionRows/Legend 없음 — 맵 재Export 필요";
            return false;
        }
        if (into.Length != width * height)
        {
            error = $"출력 버퍼 크기 불일치 — 기대 {width * height}, 실제 {into.Length}";
            return false;
        }
        if (rows.Count != height)
        {
            error = $"행 수 불일치 — 기대 {height}, 실제 {rows.Count}";
            return false;
        }

        // 기호 > Key 조회 테이블 (ASCII). 맵 로드 시 1회만 할당
        var table = new int[128];
        for (int i = 0; i < table.Length; i++)
            table[i] = Undefined;
        table[NoneSymbol] = STAGE_REGION_NONE;

        foreach (var entry in legend)
        {
            string symbol = entry.Key;
            if (symbol == null || symbol.Length != 1 || symbol[0] >= 128)
            {
                error = $"범례 기호 '{symbol}'가 ASCII 1글자가 아님";
                return false;
            }
            char c = symbol[0];
            if (c == NoneSymbol)
            {
                error = $"범례에 통로 기호 '{NoneSymbol}' 사용 불가";
                return false;
            }
            if (entry.Value < 0)
            {
                error = $"범례 '{symbol}'의 Key가 음수 ({entry.Value})";
                return false;
            }
            table[c] = entry.Value; // Dictionary 키가 유일하므로 기호 중복 없음
        }

        for (int y = 0; y < height; y++)
        {
            string row = rows[y];
            if (row == null || row.Length != width)
            {
                error = $"행 {y} 길이 불일치 — 기대 {width}, 실제 {(row == null ? 0 : row.Length)}";
                return false;
            }

            int offset = y * width;
            for (int x = 0; x < width; x++)
            {
                char c = row[x];
                int key = c < 128 ? table[c] : Undefined;
                if (key == Undefined)
                {
                    error = $"셀({x},{y})의 기호 '{c}'가 범례에 없음";
                    return false;
                }
                into[offset + x] = key;
            }
        }
        return true;
    }
    #endregion
}