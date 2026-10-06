using System;
using System.Collections.Generic;
using Data;
using UnityEngine;
using static Define;

/// <summary>
/// 셀 >> 스테이지 슬롯 조회. MapData.StageRegionRows를 StageRegionCodec으로 복원한 뒤
/// 같은 버퍼 안에서 Stage Key를 슬롯 번호로 치환해 보관한다.
///
/// - 셀 좌표: MapManager.WorldToCell과 같은 0-base (GridSpec 기준)
/// - 조회: 배열 인덱싱 1회 + 범위 검사 1회, 할당 없음
/// - 구축 실패 시 비어 있는 상태
/// </summary>
public sealed class StageRegionGrid
{
    /// <summary>어느 스테이지에도 속하지 않음 (통로 / 전장 밖 / 구축 실패).</summary>
    public const int NoSlot = -1;

    private int[] _cells = Array.Empty<int>(); // 구축 후 슬롯 번호. 통로 = NoSlot
    private int[] _cellCountBySlot = Array.Empty<int>();
    private int _width;
    private int _height;

    public bool IsBuilt { get; private set; }

    /// <param name="keysBySlot">슬롯 번호 >> Stage Key. 인덱스 : 슬롯.</param>
    public bool Build(MapData map, IReadOnlyList<int> keysBySlot)
    {
        Clear();

        if (map == null)
        {
            LogPrinter.LogError("[StageRegionGrid] MapData 없음");
            return false;
        }

        int width = map.Grid.Width;
        int height = map.Grid.Height;
        int size = width * height;
        if (width <= 0 || height <= 0)
        {
            LogPrinter.LogError($"[StageRegionGrid] {map.Name}: Grid 크기 비정상 ({width}x{height})");
            return false;
        }

        if (_cells.Length != size)
            _cells = new int[size]; // 같은 크기 맵 재로드 시 재사용

        // 1. 기호 >> Stage Key
        if (StageRegionCodec.TryDecode(map.StageRegionRows, map.StageRegionLegend, width, height, _cells, out string error) == false)
        {
            LogPrinter.LogError($"[StageRegionGrid] {map.Name}: {error}");
            return false;
        }

        // 2. Stage Key >> 슬롯 (같은 버퍼에서 덮어쓰기)
        var slotByKey = new Dictionary<int, int>(keysBySlot.Count); // 로드 시 1회
        for (int slot = 0; slot < keysBySlot.Count; slot++)
            slotByKey[keysBySlot[slot]] = slot;

        if (_cellCountBySlot.Length != keysBySlot.Count)
            _cellCountBySlot = new int[keysBySlot.Count];
        else
            Array.Clear(_cellCountBySlot, 0, _cellCountBySlot.Length);

        HashSet<int> unknownKeys = null;
        for (int i = 0; i < size; i++)
        {
            int key = _cells[i];
            if (key == STAGE_REGION_NONE)
            {
                _cells[i] = NoSlot; // 의미가 다른 두 상수를 명시적으로 변환
                continue;
            }

            if (slotByKey.TryGetValue(key, out int mapped))
            {
                _cells[i] = mapped;
                _cellCountBySlot[mapped]++;
            }
            else
            {
                (unknownKeys ??= new HashSet<int>()).Add(key);
            }
        }

        if (unknownKeys != null)
        {
            LogPrinter.LogError($"[StageRegionGrid] {map.Name}: 영역이 Stages에 없는 Key를 참조 >> {string.Join(", ", unknownKeys)} — 맵 재Export 필요");
            return false;
        }

        // 3. 영역 없는 스테이지 경고 (시작 스테이지로는 사용 가능)
        for (int slot = 0; slot < _cellCountBySlot.Length; slot++)
        {
            if (_cellCountBySlot[slot] == 0)
                LogPrinter.LogWarning($"[StageRegionGrid] {map.Name}: Stage {keysBySlot[slot]} 영역 셀 0개 — 이동으로 진입 불가");
        }

        _width = width;
        _height = height;
        IsBuilt = true;
        LogPrinter.Log($"[StageRegionGrid] {map.Name}: {width}x{height}, Stage {keysBySlot.Count}개 영역 구축");
        return true;
    }

    /// <summary>셀이 속한 슬롯. 통로/전장 밖/미구축이면 NoSlot.</summary>
    public int GetSlot(Vector2Int cell)
    {
        // 미구축 상태에서는 _width == 0 → 항상 범위 밖 → 별도 분기 없이 NoSlot
        if ((uint)cell.x >= (uint)_width || (uint)cell.y >= (uint)_height)
            return NoSlot;

        return _cells[cell.y * _width + cell.x];
    }

    /// <summary>디버그/검증용</summary>
    public int GetCellCount(int slot)
    {
        return (uint)slot < (uint)_cellCountBySlot.Length ? _cellCountBySlot[slot] : 0;
    }

    /// <summary>버퍼는 유지하고 조회만 막는다 — 다음 Build에서 재사용.</summary>
    public void Clear()
    {
        _width = 0;
        _height = 0;
        IsBuilt = false;
    }
}