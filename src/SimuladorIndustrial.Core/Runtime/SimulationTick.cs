namespace SimuladorIndustrial.Core.Runtime;

public readonly record struct SimulationTick(long Number, TimeSpan Step)
{
    public TimeSpan Elapsed => TimeSpan.FromTicks(checked(Number * Step.Ticks));
}
