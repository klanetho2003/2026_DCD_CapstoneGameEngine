using System;
using System.Collections.Generic;

/// <summary>
/// Binary min-heap 우선순위 큐. Pop은 CompareTo 기준 가장 작은 원소를 반환.
/// 
/// 계약: T의 CompareTo는 자연 순서를 정직하게 반환할 것 (작으면 음수).
/// 우선순위 반전이 필요하면 T 쪽이 아니라 비교 값 자체를 부호 반전할 것 —
/// CompareTo를 거짓말시키는 순간 List.Sort 등 다른 표준 API와 호환이 깨진다.
/// 
/// 재사용: Clear()는 내부 배열 capacity를 유지하므로,
/// 인스턴스를 보관하고 매 탐색마다 Clear 후 재사용하면 GC Alloc 0.
/// </summary>
public class PriorityQueue<T> where T : IComparable<T>
{
    private readonly List<T> _heap;

    public int Count { get { return _heap.Count; } }

    public PriorityQueue(int capacity = 0)
    {
        _heap = capacity > 0 ? new List<T>(capacity) : new List<T>();
    }

    /// <summary>O(log N)</summary>
    public void Push(T data)
    {
        _heap.Add(data);
        int now = _heap.Count - 1;

        while (now > 0)
        {
            int parent = (now - 1) / 2;

            // min-heap: 자식이 부모보다 작을 때만 위로 올라감
            if (_heap[now].CompareTo(_heap[parent]) >= 0)
                break;

            (_heap[now], _heap[parent]) = (_heap[parent], _heap[now]);
            now = parent;
        }
    }

    /// <summary>O(log N). 빈 큐에서 호출 금지 — 호출자가 Count 확인.</summary>
    public T Pop()
    {
        T ret = _heap[0];

        int lastIndex = _heap.Count - 1;
        _heap[0] = _heap[lastIndex];
        _heap.RemoveAt(lastIndex);   // 마지막 원소 제거는 O(1) — Array.Copy 발생 안 함
        lastIndex--;

        int now = 0;
        while (true)
        {
            int left = 2 * now + 1;
            int right = 2 * now + 2;
            int smallest = now;

            if (left <= lastIndex && _heap[left].CompareTo(_heap[smallest]) < 0)
                smallest = left;
            if (right <= lastIndex && _heap[right].CompareTo(_heap[smallest]) < 0)
                smallest = right;

            if (smallest == now)
                break;

            (_heap[now], _heap[smallest]) = (_heap[smallest], _heap[now]);
            now = smallest;
        }

        return ret;
    }

    /// <summary>O(1). 빈 큐에서 호출 금지.</summary>
    public T Peek() { return _heap[0]; }

    /// <summary>O(N). capacity 유지 — 재사용 진입점.</summary>
    public void Clear() { _heap.Clear(); }
}