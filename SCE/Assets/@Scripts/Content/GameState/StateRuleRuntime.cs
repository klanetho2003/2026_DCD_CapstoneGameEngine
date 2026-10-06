using static Define;

/// <summary>로드 시 변환된 require. 문자열 키 없이 핸들만.</summary>
internal readonly struct CompiledStateRequire
{
    public readonly StateHandle Handle;
    public readonly EComparison Comparison;
    public readonly int Value;

    public CompiledStateRequire(StateHandle handle, EComparison comparison, int value)
    {
        Handle = handle;
        Comparison = comparison;
        Value = value;
    }
}

/// <summary>로드 시 변환된 op.</summary>
internal readonly struct CompiledStateOp
{
    public readonly StateHandle Handle;
    public readonly EStateOp Op;
    public readonly int Value;

    public CompiledStateOp(StateHandle handle, EStateOp op, int value)
    {
        Handle = handle;
        Op = op;
        Value = value;
    }
}

/// <summary>로드 시 변환된 규칙. 로드 후 불변.</summary>
internal sealed class CompiledStateRule
{
    public readonly bool AnyKey;
    public readonly int RuleKey;
    public readonly CompiledStateRequire[] Requires;
    public readonly CompiledStateOp[] Ops;
    public readonly string Label; // "{파일 id}.rules[{i}]" — 추적 로그&에러용

    public CompiledStateRule(bool anyKey, int ruleKey, CompiledStateRequire[] requires, CompiledStateOp[] ops, string label)
    {
        AnyKey = anyKey;
        RuleKey = ruleKey;
        Requires = requires;
        Ops = ops;
        Label = label;
    }
}