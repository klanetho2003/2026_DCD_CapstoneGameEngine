using UnityEngine;
using static Define;

public static class InputHandlerRegistry
{
    private static readonly BaseInputHandler[] s_table = Build();

    private static BaseInputHandler[] Build()
    {
        var table = new BaseInputHandler[(int)EUserInputState.Count];

        // 새 input 방법 추가 시 여기에 한 줄
        Register(table, new MovementHandler());
        Register(table, new DamagedHandler());

        for (int i = 0; i < table.Length; i++)
            Debug.Assert(table[i] != null, $"[InputHandlerRegistry] EUserInputState.{(EUserInputState)i} 미등록");

        return table;
    }

    private static void Register(BaseInputHandler[] table, BaseInputHandler handler)
    {
        int index = (int)handler.StateType;
        Debug.Assert(table[index] == null,
            $"[InputHandlerRegistry] 중복 등록 >> {handler.StateType} : {table[index]?.GetType().Name} vs {handler.GetType().Name}");
        table[index] = handler;
    }

    public static BaseInputHandler Get(EUserInputState state)
    {
        int index = (int)state;
        if ((uint)index >= (uint)s_table.Length)
            return null;
        return s_table[index];
    }
}
