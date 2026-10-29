using System.Diagnostics;
using SimuladorIndustrial.Core.Diagnostics;
using SimuladorIndustrial.Core.Runtime;
using SimuladorIndustrial.Core.Tags;

namespace SimuladorIndustrial.Core.Tests;

public sealed class PerformanceTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1_000)]
    public void LogicBudgetScalesAcrossThePlannedItemCounts(int itemCount)
    {
        var journal = new EventJournal();
        var tags = new TagRegistry(journal);
        var systems = Enumerable.Range(0, itemCount)
            .Select(_ => (ISimulationSystem)new NoOpSystem())
            .ToArray();
        var engine = new SimulationEngine(
            tags,
            journal,
            systems,
            new FixedStepClock(TimeSpan.FromMilliseconds(10), maxStepsPerAdvance: 200));
        engine.Run();

        var stopwatch = Stopwatch.StartNew();
        var executed = engine.Advance(TimeSpan.FromSeconds(1));
        stopwatch.Stop();

        Assert.Equal(100, executed);
        Assert.Equal(100, engine.Metrics.ExecutedSteps);
        Assert.Equal(0, engine.Metrics.DroppedSteps);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"{itemCount} itens excederam o orçamento de segurança: {stopwatch.Elapsed}.");
    }

    private sealed class NoOpSystem : ISimulationSystem
    {
        public void Step(SimulationTick tick, TagRegistry tags)
        {
        }

        public void Reset(TagRegistry tags)
        {
        }
    }
}
