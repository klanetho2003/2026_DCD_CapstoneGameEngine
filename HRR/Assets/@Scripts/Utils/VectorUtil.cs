using UnityEngine;

public static class VectorUtil
{
    private static readonly Vector2[] _snap8Directions = new Vector2[8]
    {
        new Vector2( 1f,           0f         ),
        new Vector2( 0.7071068f,   0.7071068f ),
        new Vector2( 0f,           1f         ),
        new Vector2(-0.7071068f,   0.7071068f ),
        new Vector2(-1f,           0f         ),
        new Vector2(-0.7071068f,  -0.7071068f ),
        new Vector2( 0f,          -1f         ),
        new Vector2( 0.7071068f,  -0.7071068f ),
    };

    public static Vector2 SnapTo8Direction(Vector2 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return Vector2.zero;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        if (angle < 0f) angle += 360f;

        int index = Mathf.RoundToInt(angle / 45f) % 8;
        return _snap8Directions[index];
    }

    public static Vector2 SnapTo4Direction(Vector2 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return Vector2.zero;

        return (Mathf.Abs(dir.x) > Mathf.Abs(dir.y))
            ? (dir.x > 0 ? Vector2.right : Vector2.left)
            : (dir.y > 0 ? Vector2.up : Vector2.down);
    }
}