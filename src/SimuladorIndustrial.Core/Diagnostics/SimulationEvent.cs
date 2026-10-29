namespace SimuladorIndustrial.Core.Diagnostics;

public sealed record SimulationEvent(
    long Sequence,
    long Tick,
    DateTimeOffset OccurredAtUtc,
    string Category,
    string Message,
    Guid? TagId);
