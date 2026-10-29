using SimuladorIndustrial.Core.Diagnostics;
using SimuladorIndustrial.Core.Runtime;
using SimuladorIndustrial.Core.Scenarios;
using SimuladorIndustrial.Core.Tags;

namespace SimuladorIndustrial.Core.Tests;

public sealed class FillingCellSystemTests
{
    [Fact]
    public void FillsOnlyWithInterlocksAndStopsAtCapacity()
    {
        var journal = new EventJournal();
        var tags = new TagRegistry(journal);
        var detected = Register(tags, "recipienteDetectado", TagDataType.Bool, TagDirection.PlcInput, 0);
        var gate = Register(tags, "bloqueadorFechado", TagDataType.Bool, TagDirection.PlcOutput, 0);
        var valve = Register(tags, "valvulaEnchimento", TagDataType.Bool, TagDirection.PlcOutput, 1);
        var volume = Register(tags, "volumePreenchido", TagDataType.Float32, TagDirection.PlcInput, 0);
        var system = new FillingCellSystem(
            detected.Id,
            gate.Id,
            valve.Id,
            volume.Id,
            flowLitersPerSecond: 0.25f,
            capacityLiters: 1f);
        var engine = new SimulationEngine(
            tags,
            journal,
            [system],
            new FixedStepClock(TimeSpan.FromMilliseconds(20), maxStepsPerAdvance: 1_000));

        engine.Reset();
        engine.Run();
        engine.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0f, tags.Get(volume.Id).EffectiveValue.AsFloat32());

        tags.WriteFromPlc(gate.Id, TagValue.FromBool(true), engine.Clock.Tick);
        engine.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0f, tags.Get(volume.Id).EffectiveValue.AsFloat32());

        tags.WriteFromPlc(valve.Id, TagValue.FromBool(true), engine.Clock.Tick);
        engine.Advance(TimeSpan.FromSeconds(10));

        Assert.True(tags.Get(detected.Id).EffectiveValue.AsBool());
        Assert.Equal(1f, tags.Get(volume.Id).EffectiveValue.AsFloat32());

        engine.Reset();
        Assert.Equal(0f, tags.Get(volume.Id).EffectiveValue.AsFloat32());
    }

    [Fact]
    public void RejectsReusingOneTagForMultipleBindings()
    {
        var repeated = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => new FillingCellSystem(
            repeated,
            repeated,
            Guid.NewGuid(),
            Guid.NewGuid()));
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
}
