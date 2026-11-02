using System.Collections.Generic;
using Data;
using static Define;

/// <summary>크리처 데이터 행 하나와 그 행을 읽은 표.</summary>
public readonly struct CreatureTableRow
{
    public readonly CreatureData Row;
    public readonly EObjectType Table; // Villager · Monster · NPC

    public CreatureTableRow(CreatureData row, EObjectType table)
    {
        Row = row;
        Table = table;
    }
}

/// <summary>
/// 크리처 데이터 세 표(Villager · Monster · NPC)를 합친 것 — EditorGameData가 채운다.
/// 먼저 넣은 행이 이긴다 — 게임(DataManager.CreatureDataDic)과 같은 결과가 되도록 넣는 차례는 EditorGameData가 정한다.
/// 행이 어느 표에서 왔는지도 기억한다 — 행동 트리 편집기는 Monster 표의 행만 본다.
/// </summary>
public sealed class MergedCreatureTable
{
    private readonly Dictionary<int, CreatureTableRow> _rows = new Dictionary<int, CreatureTableRow>();

    public int Count { get { return _rows.Count; } }

    /// <summary>모든 행 — 순서는 정해져 있지 않다. foreach는 할당이 없다. 읽기 전용으로 쓸 것.</summary>
    public Dictionary<int, CreatureTableRow>.ValueCollection Rows { get { return _rows.Values; } }

    /// <summary>먼저 넣은 행이 이긴다. 이미 있으면 false와, 있던 행의 표.</summary>
    public bool TryAdd(CreatureData row, EObjectType table, out EObjectType existing)
    {
        if (_rows.TryGetValue(row.templateID, out CreatureTableRow found))
        {
            existing = found.Table;
            return false;
        }
        _rows.Add(row.templateID, new CreatureTableRow(row, table));
        existing = table;
        return true;
    }

    /// <summary>없으면 false — row는 null, table은 None.</summary>
    public bool TryGet(int templateId, out CreatureData row, out EObjectType table)
    {
        if (_rows.TryGetValue(templateId, out CreatureTableRow found))
        {
            row = found.Row;
            table = found.Table;
            return true;
        }
        row = null;
        table = EObjectType.None;
        return false;
    }
}
