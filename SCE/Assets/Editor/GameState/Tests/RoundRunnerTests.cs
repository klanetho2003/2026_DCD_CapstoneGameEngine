using NUnit.Framework;

public class RoundRunnerTests
{
    private const float DURATION = 180f;

    private RoundRunner _runner;
    private StageSpawnData _emptyContent;
    private int _endedCount;

    [SetUp]
    public void SetUp()
    {
        _runner = new RoundRunner();
        _emptyContent = new StageSpawnData();
        _endedCount = 0;
        _runner.SetInfo(() => _endedCount++);
    }

    [Test]
    public void Tick_BeforeDuration_DoesNotEnd()
    {
        _runner.Begin(_emptyContent, DURATION);
        _runner.Tick(179.9f);

        Assert.IsTrue(_runner.IsRunning);
        Assert.AreEqual(0, _endedCount);
        Assert.AreEqual(0.1f, _runner.RemainingSeconds, 0.001f);
    }

    [Test]
    public void Tick_PassingDuration_EndsExactlyOnce()
    {
        _runner.Begin(_emptyContent, DURATION);
        _runner.Tick(179.9f);
        _runner.Tick(0.2f);
        _runner.Tick(10f); // 종료 후의 Tick은 무시된다

        Assert.IsFalse(_runner.IsRunning);
        Assert.AreEqual(1, _endedCount);
        Assert.AreEqual(0f, _runner.RemainingSeconds);
    }

    [Test]
    public void Tick_ExactlyAtDuration_Ends()
    {
        _runner.Begin(_emptyContent, DURATION);
        _runner.Tick(DURATION);

        Assert.AreEqual(1, _endedCount);
    }

    [Test]
    public void Tick_ZeroDelta_MakesNoProgress()
    {
        _runner.Begin(_emptyContent, 1f);
        for (int i = 0; i < 100; i++)
            _runner.Tick(0f); // Time.timeScale = 0 상황

        Assert.IsTrue(_runner.IsRunning);
        Assert.AreEqual(0, _endedCount);
    }

    [Test]
    public void Stop_DoesNotNotify()
    {
        _runner.Begin(_emptyContent, DURATION);
        _runner.Tick(100f);
        _runner.Stop();
        _runner.Tick(100f);

        Assert.IsFalse(_runner.IsRunning);
        Assert.AreEqual(0, _endedCount);
    }

    [Test]
    public void Begin_WhileRunning_RestartsFromZero()
    {
        _runner.Begin(_emptyContent, DURATION);
        _runner.Tick(170f);
        _runner.Begin(_emptyContent, DURATION); // 이전 170초가 남아 있으면 안 된다
        _runner.Tick(170f);

        Assert.IsTrue(_runner.IsRunning);
        Assert.AreEqual(0, _endedCount);

        _runner.Tick(11f);
        Assert.AreEqual(1, _endedCount);
    }

    [Test]
    public void EndCallback_CanBeginNextRound()
    {
        int ended = 0;
        _runner.SetInfo(() =>
        {
            ended++;
            if (ended == 1)
                _runner.Begin(_emptyContent, DURATION); // 통지 안에서 곧바로 다음 라운드
        });

        _runner.Begin(_emptyContent, DURATION);
        _runner.Tick(DURATION);

        Assert.AreEqual(1, ended);
        Assert.IsTrue(_runner.IsRunning); // 새 라운드가 실행 중
        Assert.AreEqual(DURATION, _runner.RemainingSeconds, 0.001f);
    }
}