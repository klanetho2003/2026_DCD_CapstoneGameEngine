using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>순찰 방향 규칙이 정면, 오른쪽, 뒤쪽, 왼쪽 순서로 첫 진입 가능 방향을 고르는지 검증한다.</summary>
public class TilePatrolRuleTests
{
    /// <summary>오른쪽 회전이 오른쪽, 아래, 왼쪽, 위 순서로 도는지 확인한다.</summary>
    [Test]
    public void TurnRight_CyclesRightDownLeftUp()
    {
        Assert.AreEqual(Vector2Int.down, TilePatrolRule.TurnRight(Vector2Int.right));
        Assert.AreEqual(Vector2Int.left, TilePatrolRule.TurnRight(Vector2Int.down));
        Assert.AreEqual(Vector2Int.up, TilePatrolRule.TurnRight(Vector2Int.left));
        Assert.AreEqual(Vector2Int.right, TilePatrolRule.TurnRight(Vector2Int.up));
    }

    /// <summary>막힌 칸이 늘어날 때마다 다음 우선순위 방향을 고르고 모두 막히면 실패하는지 확인한다.</summary>
    [Test]
    public void TryChooseDirection_UsesFrontRightBackLeftOrder()
    {
        var blocked = new HashSet<Vector2Int>();
        bool CanEnter(Vector2Int cell) => !blocked.Contains(cell);

        Assert.IsTrue(TilePatrolRule.TryChooseDirection(Vector2Int.zero, Vector2Int.right, CanEnter, out Vector2Int selected));
        Assert.AreEqual(Vector2Int.right, selected, "정면");

        blocked.Add(Vector2Int.right);
        Assert.IsTrue(TilePatrolRule.TryChooseDirection(Vector2Int.zero, Vector2Int.right, CanEnter, out selected));
        Assert.AreEqual(Vector2Int.down, selected, "정면이 막히면 오른쪽");

        blocked.Add(Vector2Int.down);
        Assert.IsTrue(TilePatrolRule.TryChooseDirection(Vector2Int.zero, Vector2Int.right, CanEnter, out selected));
        Assert.AreEqual(Vector2Int.left, selected, "정면과 오른쪽이 막히면 뒤쪽");

        blocked.Add(Vector2Int.left);
        Assert.IsTrue(TilePatrolRule.TryChooseDirection(Vector2Int.zero, Vector2Int.right, CanEnter, out selected));
        Assert.AreEqual(Vector2Int.up, selected, "세 방향이 막히면 왼쪽");

        blocked.Add(Vector2Int.up);
        Assert.IsFalse(TilePatrolRule.TryChooseDirection(Vector2Int.zero, Vector2Int.right, CanEnter, out selected));
        Assert.AreEqual(Vector2Int.zero, selected, "모두 막히면 실패");
    }
}
