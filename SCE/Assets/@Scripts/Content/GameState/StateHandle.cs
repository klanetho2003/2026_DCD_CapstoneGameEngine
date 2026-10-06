using System;

/// <summary>
/// 상태 키의 런타임 핸들. 로드 시 문자열 키에서 1회 변환해 보관하고, 조회는 배열 인덱싱 1회.
/// default(StateHandle)은 무효 — 내부에 인덱스 + 1을 저장한다.
/// </summary>
public readonly struct StateHandle : IEquatable<StateHandle>
{
    private readonly int _indexPlusOne;

    internal StateHandle(int index)
    {
        _indexPlusOne = index + 1;
    }

    public int Index { get { return _indexPlusOne - 1; } }
    public bool IsValid { get { return _indexPlusOne > 0; } }

    public bool Equals(StateHandle other) { return _indexPlusOne == other._indexPlusOne; }
    public override bool Equals(object obj) { return obj is StateHandle other && Equals(other); }
    public override int GetHashCode() { return _indexPlusOne; }
}