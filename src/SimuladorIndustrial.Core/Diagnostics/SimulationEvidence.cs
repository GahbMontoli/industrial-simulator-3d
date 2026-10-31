using SimuladorIndustrial.Core.Runtime;
using SimuladorIndustrial.Core.Tags;

namespace SimuladorIndustrial.Core.Diagnostics;

public sealed record SimulationEvidence(
    int SchemaVersion,
    string RunId,
    DateTimeOffset ExportedAtUtc,
    Guid SceneId,
    string SceneName,
    RuntimeEvidence Runtime,
    IReadOnlyList<TagEvidence> Tags,
    IReadOnlyList<SimulationEvent> Events);

public sealed record RuntimeEvidence(
    SimulationRunState State,
    long Tick,
    double FixedStepMilliseconds,
    long DroppedStepCount,
    long EventCount,
    long ExecutedSteps,
    double LastLogicStepMilliseconds,
    double AverageLogicStepMilliseconds,
    double MaximumLogicStepMilliseconds);

public sealed record TagEvidence(
    Guid Id,
    string Name,
    string CanonicalName,
    TagDataType DataType,
    TagDirection Direction,
    int Address,
    string RawValue,
    string EffectiveValue,
    TagQuality Quality,
    bool IsForced,
    string? ForcedValue,
    TagFault Fault,
    long LastChangedTick,
    DateTimeOffset LastChangedAtUtc);
