using SimuladorIndustrial.Core.Diagnostics;
using SimuladorIndustrial.Core.Tags;

namespace SimuladorIndustrial.Core.Tests;

public sealed class TagRegistryTests
{
    [Fact]
    public void RejectsWritesFromTheWrongSideAndValuesOfTheWrongType()
    {
        var registry = new TagRegistry(new EventJournal());
        var input = CreateTag("entrada", TagDataType.Bool, TagDirection.PlcInput, 0);
        var output = CreateTag("saida", TagDataType.Bool, TagDirection.PlcOutput, 0);
        registry.Register(input);
        registry.Register(output);

        Assert.Throws<InvalidOperationException>(
            () => registry.WriteFromPlc(input.Id, TagValue.FromBool(true), 1));
        Assert.Throws<InvalidOperationException>(
            () => registry.PublishFromSimulation(output.Id, TagValue.FromBool(true), 1));
        Assert.Throws<ArgumentException>(
            () => registry.PublishFromSimulation(input.Id, TagValue.FromInt32(1), 1));
    }

    [Fact]
    public void FaultOverridesForceAndResetClearsRuntimeState()
    {
        var registry = new TagRegistry(new EventJournal());
        var input = CreateTag("sensor", TagDataType.Bool, TagDirection.PlcInput, 0);
        registry.Register(input);
        registry.PublishFromSimulation(input.Id, TagValue.FromBool(true), 1);

        var forced = registry.Force(input.Id, TagValue.FromBool(false), 2);
        Assert.True(forced.IsForced);
        Assert.False(forced.EffectiveValue.AsBool());

        var faulted = registry.SetFault(input.Id, TagFault.AlwaysOn, 3);
        Assert.Equal(TagFault.AlwaysOn, faulted.Fault);
        Assert.True(faulted.EffectiveValue.AsBool());

        registry.SetQuality(input.Id, TagQuality.Bad, 4);
        registry.ResetRuntimeState(5);

        var reset = registry.Get(input.Id);
        Assert.False(reset.RawValue.AsBool());
        Assert.False(reset.EffectiveValue.AsBool());
        Assert.False(reset.IsForced);
        Assert.Null(reset.ForcedValue);
        Assert.Equal(TagFault.None, reset.Fault);
        Assert.Equal(TagQuality.Good, reset.Quality);
        Assert.Equal(5, reset.LastChangedTick);
    }

    [Fact]
    public void RejectsOverlappingThirtyTwoBitRegisterRanges()
    {
        var registry = new TagRegistry(new EventJournal());
        registry.Register(CreateTag("inteiro", TagDataType.Int32, TagDirection.PlcInput, 10));

        var overlapping = CreateTag("real", TagDataType.Float32, TagDirection.PlcInput, 11);

        Assert.Throws<InvalidOperationException>(() => registry.Register(overlapping));
    }

    [Fact]
    public void RecordsTheUtcInstantOfTheLastChange()
    {
        var initial = DateTimeOffset.Parse("2026-10-27T12:00:00Z");
        var timeProvider = new MutableTimeProvider(initial);
        var registry = new TagRegistry(new EventJournal(timeProvider), timeProvider);
        var input = CreateTag("timestamp", TagDataType.Bool, TagDirection.PlcInput, 0);
        registry.Register(input);
        Assert.Equal(initial, registry.Get(input.Id).LastChangedAtUtc);

        timeProvider.UtcNow = initial.AddSeconds(5);
        var changed = registry.PublishFromSimulation(input.Id, TagValue.FromBool(true), 3);

        Assert.Equal(3, changed.LastChangedTick);
        Assert.Equal(initial.AddSeconds(5), changed.LastChangedAtUtc);
    }

    [Fact]
    public void NoiseFaultProducesBoundedDeterministicVariations()
    {
        var registry = new TagRegistry(new EventJournal());
        var input = CreateTag("noise", TagDataType.Int32, TagDirection.PlcInput, 0);
        registry.Register(input);
        registry.PublishFromSimulation(input.Id, TagValue.FromInt32(100), 1);
        registry.SetFault(input.Id, TagFault.Noise, 2);
        var samples = new HashSet<int>();

        for (var tick = 2; tick <= 20; tick++)
        {
            samples.Add(registry.PublishFromSimulation(input.Id, TagValue.FromInt32(100), tick).EffectiveValue.AsInt32());
        }

        Assert.True(samples.Count > 1);
        Assert.All(samples, sample => Assert.InRange(sample, 97, 103));
    }

    private static TagDefinition CreateTag(
        string canonicalName,
        TagDataType dataType,
        TagDirection direction,
        int address) => new(
            Guid.NewGuid(),
            canonicalName,
            canonicalName,
            dataType,
            direction,
            address,
            $"teste.{canonicalName}");

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
