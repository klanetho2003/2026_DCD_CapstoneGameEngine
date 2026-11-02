using System;
using UnityEngine;

/// <summary>칸 순찰의 방향 규칙이며 정면, 오른쪽, 뒤쪽, 왼쪽 순서로 처음 진입 가능한 방향을 고른다.</summary>
public static class TilePatrolRule
{
    /// <summary>현재 시선에서 오른쪽으로 90도씩 돌리며 실제 진입 가능한 방향을 선택한다.</summary>
    /// <param name="cell">다음 이동을 판단하는 현재 타일이다.</param>
    /// <param name="facing">정면으로 삼을 현재 시선 방향이다.</param>
    /// <param name="canEnter">벽, 영역, 다른 캐릭터 선점을 확인할 공통 조회 함수다.</param>
    /// <param name="selected">통행 가능한 다음 한 칸 방향이며 실패 시 0이다.</param>
    public static bool TryChooseDirection(Vector2Int cell, Vector2Int facing,
        Func<Vector2Int, bool> canEnter, out Vector2Int selected)
    {
        // 각 후보를 오른쪽으로 90도씩 돌리며 확인할 방향이다.
        Vector2Int direction = facing;
        // attempt는 정면부터 오른쪽으로 회전하며 검사한 방향 수다.
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (canEnter(cell + direction))
            {
                selected = direction;
                return true;
            }
            direction = TurnRight(direction);
        }
        selected = Vector2Int.zero;
        return false;
    }

    /// <summary>y가 위쪽인 타일 좌표에서 방향을 오른쪽으로 90도 돌린다.</summary>
    /// <param name="direction">돌리기 전의 한 칸 방향이다.</param>
    public static Vector2Int TurnRight(Vector2Int direction)
    {
        return new Vector2Int(direction.y, -direction.x);
    }
}
