using SimuladorIndustrial.Core.Diagnostics;
using SimuladorIndustrial.Core.Runtime;
using SimuladorIndustrial.Core.Tags;

namespace SimuladorIndustrial.Core.Tests;

public sealed class RuntimeTests
{
    [Fact]
    public void FixedClockAccumulatesRemainderAndExecutesWholeSteps()
    {
        var clock = new FixedStepClock(TimeSpan.FromMilliseconds(20));
        var ticks = new List<SimulationTick>();

        var executed = clock.Advance(TimeSpan.FromMilliseconds(45), ticks.Add);

        Assert.Equal(2, executed);
        Assert.Equal([1L, 2L], ticks.Select(tick => tick.Number));
        Assert.All(ticks, tick => Assert.Equal(TimeSpan.FromMilliseconds(20), tick.Step));
        Assert.Equal(TimeSpan.FromMilliseconds(5), clock.PendingTime);
        Assert.Equal(2, clock.Tick);
    }

    [Fact]
    public void FixedClockDropsExcessCatchUpStepsDeterministically()
    {
        var clock = new FixedStepClock(TimeSpan.FromMilliseconds(20), maxStepsPerAdvance: 2);

        var executed = clock.Advance(TimeSpan.FromMilliseconds(100), _ => { });

        Assert.Equal(2, executed);
        Assert.Equal(3, clock.LastDroppedSteps);
        Assert.Equal(3, clock.DroppedStepCount);
        Assert.Equal(TimeSpan.Zero, clock.PendingTime);
    }

    [Fact]
    public void EngineRunsPausesAndResetsSystemsAndTags()
    {
        var journal = new EventJournal();
        var tags = new TagRegistry(journal);
        var input = new TagDefinition(
            Guid.NewGuid(),
            "Sensor",
            "sensor",
            TagDataType.Bool,
            TagDirection.PlcInput,
            0,
            "teste.sensor");
        tags.Register(input);
        var system = new CountingSystem(input.Id);
        var engine = new SimulationEngine(
            tags,
            journal,
            [system],
            new FixedStepClock(TimeSpan.FromMilliseconds(20)));

        Assert.Equal(0, engine.Advance(TimeSpan.FromMilliseconds(100)));
        engine.Run();
        Assert.Equal(2, engine.Advance(TimeSpan.FromMilliseconds(45)));
        Assert.Equal(2, system.StepCount);
        Assert.True(tags.Get(input.Id).EffectiveValue.AsBool());
        Assert.Equal(2, engine.Metrics.ExecutedSteps);
        Assert.True(engine.Metrics.LastLogicStepMilliseconds >= 0d);
        Assert.True(engine.Metrics.MaximumLogicStepMilliseconds >= engine.Metrics.AverageLogicStepMilliseconds);

        engine.Pause();
        Assert.Equal(0, engine.Advance(TimeSpan.FromMilliseconds(100)));
        Assert.Equal(2, system.StepCount);

        engine.Reset();
        Assert.Equal(SimulationRunState.Stopped, engine.State);
        Assert.Equal(0, engine.Clock.Tick);
        Assert.Equal(1, system.ResetCount);
        Assert.False(tags.Get(input.Id).EffectiveValue.AsBool());
        Assert.Equal(0, engine.Metrics.ExecutedSteps);
        Assert.Equal(0d, engine.Metrics.AverageLogicStepMilliseconds);
    }

    private sealed class CountingSystem(Guid tagId) : ISimulationSystem
    {
        public int StepCount { get; private set; }

        public int ResetCount { get; private set; }

        public void Step(SimulationTick tick, TagRegistry tags)
        {
            StepCount++;
            tags.PublishFromSimulation(tagId, TagValue.FromBool(true), tick.Number);
        }

        public void Reset(TagRegistry tags)
        {
            ResetCount++;
        }
    }
}
