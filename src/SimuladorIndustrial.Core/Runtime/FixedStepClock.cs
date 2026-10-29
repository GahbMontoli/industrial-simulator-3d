namespace SimuladorIndustrial.Core.Runtime;

public sealed class FixedStepClock
{
    public static readonly TimeSpan DefaultStep = TimeSpan.FromMilliseconds(20);
    public const int DefaultMaxStepsPerAdvance = 100;

    private TimeSpan _accumulator;

    public FixedStepClock(
        TimeSpan? step = null,
        int maxStepsPerAdvance = DefaultMaxStepsPerAdvance)
    {
        Step = step ?? DefaultStep;

        if (Step <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(step), "The fixed step must be positive.");
        }

        if (maxStepsPerAdvance <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxStepsPerAdvance),
                "The catch-up limit must be positive.");
        }

        MaxStepsPerAdvance = maxStepsPerAdvance;
    }

    public TimeSpan Step { get; }

    public int MaxStepsPerAdvance { get; }

    public long Tick { get; private set; }

    public TimeSpan PendingTime => _accumulator;

    public long DroppedStepCount { get; private set; }

    public long LastDroppedSteps { get; private set; }

    public int Advance(TimeSpan elapsed, Action<SimulationTick> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);

        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed), "Elapsed time cannot be negative.");
        }

        LastDroppedSteps = 0;
        _accumulator += elapsed;
        var executed = 0;

        while (_accumulator >= Step && executed < MaxStepsPerAdvance)
        {
            _accumulator -= Step;
            Tick = checked(Tick + 1);
            execute(new SimulationTick(Tick, Step));
            executed++;
        }

        if (_accumulator >= Step)
        {
            LastDroppedSteps = _accumulator.Ticks / Step.Ticks;
            DroppedStepCount = checked(DroppedStepCount + LastDroppedSteps);
            _accumulator -= TimeSpan.FromTicks(checked(LastDroppedSteps * Step.Ticks));
        }

        return executed;
    }

    public void Reset()
    {
        Tick = 0;
        _accumulator = TimeSpan.Zero;
        DroppedStepCount = 0;
        LastDroppedSteps = 0;
    }
}
