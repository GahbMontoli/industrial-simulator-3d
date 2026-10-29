namespace SimuladorIndustrial.Core.Diagnostics;

public sealed record SimulationMetricsSnapshot(
    long ExecutedSteps,
    double LastLogicStepMilliseconds,
    double AverageLogicStepMilliseconds,
    double MaximumLogicStepMilliseconds,
    long DroppedSteps);
