using NUnit.Framework;
using static Define;

/// <summary>상호작용에서 상태 키 읽기·신호 발생을 빠짐없이 찾는지 (InteractionReferenceScanner.Collect).</summary>
public class InteractionReferenceScanTests
{
    private const string FilePath = "Assets/Test/merchant.json";

    private static InteractionSetDefinition MakeSet()
    {
        return new InteractionSetDefinition
        {
            Id = "merchant",
            Interactions = new[]
            {
                new InteractionDefinition
                {
                    Id = "talk",
                    Conditions = new InteractionCondition[]
                    {
                        new DistanceCondition(),
                        new StateValueCondition { Key = "quest.a.done", Op = EComparison.GreaterOrEqual, Value = 1 },
                        new NotCondition { Inner = new StateValueCondition { Key = "kill.slime", Op = EComparison.Equal, Value = 0 } },
                    },
                    EffectPrototypes = new InteractionEffect[]
                    {
                        new PrintLogEffect(),
                        new RaiseSignalEffect { SignalId = 100 },
                    },
                },
            },
        };
    }

    [Test]
    public void Collect_FindsTopLevelStateValue()
    {
        var scan = new InteractionReferenceScan();
        InteractionReferenceScanner.Collect(MakeSet(), FilePath, scan);

        Assert.AreEqual(2, scan.KeyReads.Count);
        InteractionKeyRead read = scan.KeyReads[0];
        Assert.AreEqual("quest.a.done", read.Key);
        Assert.AreEqual("merchant", read.SetId);
        Assert.AreEqual("talk", read.InteractionId);
        Assert.AreEqual(0, read.InteractionIndex);
        Assert.AreEqual(1, read.ConditionIndex); // Distance 다음
        Assert.IsFalse(read.IsNested);
        Assert.AreEqual(EComparison.GreaterOrEqual, read.Op);
        Assert.AreEqual(1, read.Value);
        Assert.AreEqual(FilePath, read.FilePath);
    }

    [Test]
    public void Collect_FindsStateValueInsideNot()
    {
        var scan = new InteractionReferenceScan();
        InteractionReferenceScanner.Collect(MakeSet(), FilePath, scan);

        InteractionKeyRead read = scan.KeyReads[1];
        Assert.AreEqual("kill.slime", read.Key);
        Assert.AreEqual(2, read.ConditionIndex, "묶음 조건(Not)의 순번");
        Assert.IsTrue(read.IsNested, "Not 안쪽");
    }

    [Test]
    public void Collect_FindsRaiseSignal()
    {
        var scan = new InteractionReferenceScan();
        InteractionReferenceScanner.Collect(MakeSet(), FilePath, scan);

        Assert.AreEqual(1, scan.SignalRaises.Count);
        Assert.AreEqual(100, scan.SignalRaises[0].SignalId);
        Assert.AreEqual(1, scan.SignalRaises[0].EffectIndex); // PrintLog 다음
        Assert.AreEqual("talk", scan.SignalRaises[0].InteractionId);
    }

    [Test]
    public void Collect_IsNullSafe()
    {
        var scan = new InteractionReferenceScan();

        InteractionReferenceScanner.Collect(null, FilePath, scan);
        InteractionReferenceScanner.Collect(new InteractionSetDefinition { Id = "empty" }, FilePath, scan);
        InteractionReferenceScanner.Collect(new InteractionSetDefinition
        {
            Id = "holes",
            Interactions = new[]
            {
                null,
                new InteractionDefinition { Id = "no-arrays" },
                new InteractionDefinition
                {
                    Id = "null-nodes",
                    Conditions = new InteractionCondition[] { null, new NotCondition() }, // Inner가 null인 Not
                    EffectPrototypes = new InteractionEffect[] { null },
                },
            },
        }, FilePath, scan);

        Assert.AreEqual(0, scan.KeyReads.Count);
        Assert.AreEqual(0, scan.SignalRaises.Count);
    }
}