using System.Collections.Generic;
using UnityEngine;

/// <summary>타일의 지속적인 소유권을 선점 중인 캐릭터와 별개로 구분한다.</summary>
public enum TileOwner
{
    /// <summary>이번 라운드에서 아직 어느 진영도 점령하지 않은 상태다.</summary>
    Neutral,
    /// <summary>플레이어가 마지막으로 진입하여 소유한 상태다.</summary>
    Player,
    /// <summary>몬스터가 마지막으로 진입하여 소유한 상태다.</summary>
    Monster
}

/// <summary>정산 순간 확정한 진영별 소유 타일 수를 변경 불가능한 값으로 전달한다.</summary>
public readonly struct TerritoryScore
{
    /// <summary>정산 순간 플레이어가 소유한 타일 수다.</summary>
    public int PlayerTiles { get; }
    /// <summary>정산 순간 몬스터가 소유한 타일 수다.</summary>
    public int MonsterTiles { get; }

    /// <summary>현재 소유 타일 수를 정산 결과에 복사한다.</summary>
    /// <param name="playerTiles">플레이어 소유 타일 수다.</param>
    /// <param name="monsterTiles">몬스터 소유 타일 수다.</param>
    public TerritoryScore(int playerTiles, int monsterTiles)
    {
        PlayerTiles = playerTiles;
        MonsterTiles = monsterTiles;
    }
}

/// <summary>타일 소유권과 현재 소유 수를 관리하며 정산 후 변경과 중복 집계를 차단한다.</summary>
public sealed class TileTerritory
{
    /// <summary>중립이 아닌 타일만 기록하며 라운드가 바뀌어도 저장 공간을 재사용한다.</summary>
    private readonly Dictionary<Vector2Int, TileOwner> _owners = new();
    /// <summary>현재 라운드에 플레이어가 소유한 타일 수다.</summary>
    public int PlayerTiles { get; private set; }
    /// <summary>현재 라운드에 몬스터가 소유한 타일 수다.</summary>
    public int MonsterTiles { get; private set; }
    /// <summary>점령 라운드가 시작된 맵인지 나타내며 정산 대기와 미시작 상태를 구분한다.</summary>
    public bool HasRound { get; private set; }
    /// <summary>현재 라운드에서 점령과 탈취를 허용하는지 나타낸다.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>기록이 없는 칸은 중립으로 간주하여 현재 소유권을 조회한다.</summary>
    /// <param name="cell">소유권을 조회할 타일 좌표다.</param>
    public TileOwner GetOwner(Vector2Int cell)
    {
        // owner는 해당 칸을 마지막으로 점령한 진영이다.
        return _owners.TryGetValue(cell, out TileOwner owner) ? owner : TileOwner.Neutral;
    }

    /// <summary>새 라운드의 소유권과 집계를 비우고 점령을 허용한다.</summary>
    public void BeginRound()
    {
        Clear();
        HasRound = true;
        IsOpen = true;
    }

    /// <summary>진영이 바뀔 때만 이전 진영의 소유 수를 빼고 새 진영에 한 칸을 더한다.</summary>
    /// <param name="cell">실제로 진입한 타일 좌표다.</param>
    /// <param name="owner">이번 진입으로 소유권을 얻을 진영이다.</param>
    public bool TryCapture(Vector2Int cell, TileOwner owner)
    {
        if (!IsOpen || (owner != TileOwner.Player && owner != TileOwner.Monster))
            return false;
        // previous는 탈취 시 집계에서 한 칸을 빼야 할 기존 소유 진영이다.
        TileOwner previous = GetOwner(cell);
        if (previous == owner)
            return false;
        if (previous == TileOwner.Player)
            PlayerTiles--;
        else if (previous == TileOwner.Monster)
            MonsterTiles--;
        _owners[cell] = owner;
        if (owner == TileOwner.Player)
            PlayerTiles++;
        else
            MonsterTiles++;
        return true;
    }

    /// <summary>추가 점령을 잠그고 현재 소유 수를 복사하며 기존 색상과 소유권은 유지한다.</summary>
    public TerritoryScore FinishRound()
    {
        IsOpen = false;
        return new TerritoryScore(PlayerTiles, MonsterTiles);
    }

    /// <summary>맵 이탈이나 다음 라운드 준비 시 소유권과 집계를 초기화한다.</summary>
    public void Clear()
    {
        _owners.Clear();
        PlayerTiles = 0;
        MonsterTiles = 0;
        HasRound = false;
        IsOpen = false;
    }
}
