using SimuladorIndustrial.Core.Diagnostics;
using SimuladorIndustrial.Core.Runtime;
using SimuladorIndustrial.Core.Scenarios;
using SimuladorIndustrial.Core.Tags;

namespace SimuladorIndustrial.Core.Tests;

public sealed class AnimatedFillingLineSystemTests
{
    [Fact]
    public void AutomaticControllerCompletesAndRestartsTheAnimatedCycle()
    {
        var fixture = CreateFixture(controllerEnabled: true);
        fixture.Engine.Reset();
        fixture.Engine.Run();

        AdvanceUntil(
            fixture.Engine,
            () => fixture.Line.Phase == FillingLinePhase.Filling && fixture.Line.VolumeLiters > 0f);

        Assert.True(fixture.Line.SensorActive);
        Assert.True(fixture.Tags.Get(fixture.Gate.Id).EffectiveValue.AsBool());
        Assert.True(fixture.Tags.Get(fixture.Valve.Id).EffectiveValue.AsBool());

        AdvanceUntil(
            fixture.Engine,
            () => fixture.Line.Phase == FillingLinePhase.Departing);

        Assert.Equal(0.2f, fixture.Line.VolumeLiters, precision: 3);
        Assert.Equal(1f, fixture.Line.FillRatio);
        Assert.False(fixture.Tags.Get(fixture.Gate.Id).EffectiveValue.AsBool());
        Assert.False(fixture.Tags.Get(fixture.Valve.Id).EffectiveValue.AsBool());

        AdvanceUntil(
            fixture.Engine,
            () => fixture.Line.Phase == FillingLinePhase.WaitingForNextContainer);
        Assert.False(fixture.Line.ContainerVisible);
        Assert.False(fixture.Line.SensorActive);

        AdvanceUntil(
            fixture.Engine,
            () => fixture.Line.Phase == FillingLinePhase.Approaching);
        Assert.True(fixture.Line.ContainerVisible);
        Assert.Equal(fixture.Line.SpawnX, fixture.Line.ContainerX, precision: 6);
        Assert.Equal(0f, fixture.Line.VolumeLiters);
        Assert.True(fixture.Line.BeltTravelMeters > 0d);
    }

    [Fact]
    public void DisabledControllerLetsTheContainerPassWithoutFilling()
    {
        var fixture = CreateFixture(controllerEnabled: false);
        fixture.Engine.Reset();
        fixture.Engine.Run();

        AdvanceUntil(
            fixture.Engine,
            () => fixture.Line.Phase == FillingLinePhase.Departing);

        Assert.Equal(0f, fixture.Line.VolumeLiters);
        Assert.False(fixture.Tags.Get(fixture.Gate.Id).EffectiveValue.AsBool());
        Assert.False(fixture.Tags.Get(fixture.Valve.Id).EffectiveValue.AsBool());
    }

    [Fact]
    public void RejectsInvalidGeometryAndDuplicatedBindings()
    {
        var id = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => new AnimatedFillingLineSystem(
            id,
            id,
            Guid.NewGuid(),
            Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new AnimatedFillingLineSystem(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            spawnX: 1d,
            stationX: 0d,
            exitX: 2d));
    }

    private static Fixture CreateFixture(bool controllerEnabled)
    {
        var journal = new EventJournal();
        var tags = new TagRegistry(journal);
        var detected = Register(tags, "recipienteDetectado", TagDataType.Bool, TagDirection.PlcInput, 0);
        var gate = Register(tags, "bloqueadorFechado", TagDataType.Bool, TagDirection.PlcOutput, 0);
        var valve = Register(tags, "valvulaEnchimento", TagDataType.Bool, TagDirection.PlcOutput, 1);
        var volume = Register(tags, "volumePreenchido", TagDataType.Float32, TagDirection.PlcInput, 0);
        var line = new AnimatedFillingLineSystem(
            detected.Id,
            gate.Id,
            valve.Id,
            volume.Id,
            spawnX: -1d,
            stationX: 0d,
            exitX: 1d,
            conveyorSpeedMetersPerSecond: 1d,
            flowLitersPerSecond: 1f,
            capacityLiters: 0.2f,
            respawnDelaySeconds: 0.1d,
            sensorHalfWidthMeters: 0.1d);
        var controller = new DemoPlcControllerSystem(
            detected.Id,
            volume.Id,
            gate.Id,
            valve.Id,
            targetVolumeLiters: 0.2f)
        {
            Enabled = controllerEnabled
        };
        var engine = new SimulationEngine(
            tags,
            journal,
            [line, controller],
            new FixedStepClock(TimeSpan.FromMilliseconds(20), maxStepsPerAdvance: 100));

        return new Fixture(tags, detected, gate, valve, volume, line, engine);
    }

    private static void AdvanceUntil(
        SimulationEngine engine,
        Func<bool> condition,
        int maximumSteps = 500)
    {
        for (var index = 0; index < maximumSteps; index++)
        {
            engine.Advance(engine.Clock.Step);
            if (condition())
            {
                return;
            }
        }

        Assert.Fail($"Condition was not reached after {maximumSteps} steps.");
    }

    private static TagDefinition Register(
        TagRegistry registry,
        string canonicalName,
        TagDataType dataType,
        TagDirection direction,
        int address)
    {
        var tag = new TagDefinition(
            Guid.NewGuid(),
            canonicalName,
            canonicalName,
            dataType,
            direction,
            address,
            $"teste.{canonicalName}");
        registry.Register(tag);
        return tag;
    }

    private sealed record Fixture(
        TagRegistry Tags,
        TagDefinition Detected,
        TagDefinition Gate,
        TagDefinition Valve,
        TagDefinition Volume,
        AnimatedFillingLineSystem Line,
        SimulationEngine Engine);
}
