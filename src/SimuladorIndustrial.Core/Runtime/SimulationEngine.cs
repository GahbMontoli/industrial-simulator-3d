using System.Diagnostics;
using SimuladorIndustrial.Core.Diagnostics;
using SimuladorIndustrial.Core.Tags;

namespace SimuladorIndustrial.Core.Runtime;

public sealed class SimulationEngine
{
    private readonly TagRegistry _tags;
    private readonly EventJournal _journal;
    private readonly IReadOnlyList<ISimulationSystem> _systems;
    private long _executedSteps;
    private double _lastStepMilliseconds;
    private double _totalStepMilliseconds;
    private double _maximumStepMilliseconds;

    public SimulationEngine(
        TagRegistry tags,
        EventJournal journal,
        IEnumerable<ISimulationSystem>? systems = null,
        FixedStepClock? clock = null)
    {
        _tags = tags ?? throw new ArgumentNullException(nameof(tags));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _systems = systems?.ToArray() ?? [];
        Clock = clock ?? new FixedStepClock();
    }

    public SimulationRunState State { get; private set; } = SimulationRunState.Stopped;

    public FixedStepClock Clock { get; }

    public SimulationMetricsSnapshot Metrics => new(
        _executedSteps,
        _lastStepMilliseconds,
        _executedSteps == 0 ? 0d : _totalStepMilliseconds / _executedSteps,
        _maximumStepMilliseconds,
        Clock.DroppedStepCount);

    public void Run()
    {
        if (State == SimulationRunState.Running)
        {
            return;
        }

        var previous = State;
        State = SimulationRunState.Running;
        _journal.Record(Clock.Tick, "runtime.state", $"Simulation changed from {previous} to Running.");
    }

    public void Pause()
    {
        if (State == SimulationRunState.Paused)
        {
            return;
        }

        if (State != SimulationRunState.Running)
        {
            throw new InvalidOperationException("Only a running simulation can be paused.");
        }

        State = SimulationRunState.Paused;
        _journal.Record(Clock.Tick, "runtime.state", "Simulation paused.");
    }

    public void Reset()
    {
        Clock.Reset();
        _executedSteps = 0;
        _lastStepMilliseconds = 0d;
        _totalStepMilliseconds = 0d;
        _maximumStepMilliseconds = 0d;
        _tags.ResetRuntimeState(Clock.Tick);

        foreach (var system in _systems)
        {
            system.Reset(_tags);
        }

        State = SimulationRunState.Stopped;
        _journal.Record(Clock.Tick, "runtime.state", "Simulation reset and stopped.");
    }

    public int Advance(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed), "Elapsed time cannot be negative.");
        }

        if (State != SimulationRunState.Running)
        {
            return 0;
        }

        var executed = Clock.Advance(elapsed, ExecuteStep);

        if (Clock.LastDroppedSteps > 0)
        {
            _journal.Record(
                Clock.Tick,
                "runtime.overrun",
                $"Dropped {Clock.LastDroppedSteps} fixed steps after reaching the catch-up limit.");
        }

        return executed;
    }

    private void ExecuteStep(SimulationTick tick)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            foreach (var system in _systems)
            {
                system.Step(tick, _tags);
            }
        }
        catch (Exception exception)
        {
            _journal.Record(
                tick.Number,
                "runtime.error",
                $"Simulation system failed: {exception.GetType().Name}: {exception.Message}");
            throw;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            _lastStepMilliseconds = elapsed;
            _totalStepMilliseconds += elapsed;
            _maximumStepMilliseconds = Math.Max(_maximumStepMilliseconds, elapsed);
            _executedSteps = checked(_executedSteps + 1);
        }
    }
}
