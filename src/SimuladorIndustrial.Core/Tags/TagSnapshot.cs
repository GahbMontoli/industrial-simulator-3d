namespace SimuladorIndustrial.Core.Tags;

public sealed record TagSnapshot(
    TagDefinition Definition,
    TagValue RawValue,
    TagValue EffectiveValue,
    TagQuality Quality,
    bool IsForced,
    TagValue? ForcedValue,
    TagFault Fault,
    long LastChangedTick,
    DateTimeOffset LastChangedAtUtc);
