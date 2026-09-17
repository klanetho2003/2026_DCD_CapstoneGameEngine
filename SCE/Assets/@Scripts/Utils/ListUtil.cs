using System.Collections.Generic;
using UnityEngine;

public class ListUtil
{
    public static void FastRemoveAt<T>(IList<T> list, int index)
    {
        int lastIndex = list.Count - 1;

        // 1. 마지막 요소를 삭제할 위치로 복사
        list[index] = list[lastIndex];

        // 2. 마지막 요소 삭제 (이동 비용 없음)
        list.RemoveAt(lastIndex);
    }
}
