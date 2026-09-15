using UnityEngine;
using UnityEngine.UIElements;
using static Define;

public class MathUtil
{
    #region Equal
    public static bool IsEqualValue(float valueA, float valueB, float EPS = 0.001f)
    {
        return Mathf.Abs(valueA - valueB) < EPS;
    }

    public static bool IsEqual(Vector2 vector_A, Vector2 vector_B, float EPS = 0.01f)
    {
        return (vector_A - vector_B).sqrMagnitude < EPS * EPS;
    }

    public static bool IsEqual(Vector3 vector_A, Vector3 vector_B, float EPS = 0.01f)
    {
        return (vector_A - vector_B).sqrMagnitude < EPS * EPS;
    }
    #endregion

    public static float GetZRotationToTarget(
        Vector2 from,
        Vector2 to,
        float spriteForwardAngle = 0f,
        float fallbackAngle = 0f)
    {
        return GetZRotationFromDirection(to - from, spriteForwardAngle, fallbackAngle);
    }

    public static float GetZRotationFromDirection(
        Vector2 direction,
        float spriteForwardAngle = 0f,
        float fallbackAngle = 0f)
    {
        // 1. Zero 벡터 가드
        if (direction.sqrMagnitude < DIRECTION_EPSILON_SQR)
            return fallbackAngle;

        // 2. Atan2로 방향각 계산 (라디안 → 도)
        float rad = Mathf.Atan2(direction.y, direction.x);
        float deg = rad * Mathf.Rad2Deg;

        // 3. 스프라이트 기본 방향 보정
        deg -= spriteForwardAngle;

        // 4. [0, 360) 범위로 정규화
        return NormalizeAngle360(deg);
    }

    /// <summary>
    /// 임의 각도(deg)를 [0, 360) 범위로 정규화.
    /// -90도 >> 270도, 450도 >> 90도
    /// </summary>
    public static float NormalizeAngle360(float angle)
    {
        // C#의 % 연산자는 음수 입력 시 음수 반환ㄹ → +360 후 다시 % 적용
        angle %= 360f;
        if (angle < 0f) angle += 360f;
        return angle;
    }

    /// <summary>회전을 가장 가까운 90도 배수로 snap. 굴림 누적 오차의 최후 보정.</summary>
    public static Quaternion SnapRotation90(Quaternion rot)
    {
        Vector3 e = rot.eulerAngles;
        return Quaternion.Euler(
            Mathf.Round(e.x / 90f) * 90f,
            Mathf.Round(e.y / 90f) * 90f,
            Mathf.Round(e.z / 90f) * 90f);
    }
}
