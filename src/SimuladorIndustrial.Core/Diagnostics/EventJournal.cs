namespace SimuladorIndustrial.Core.Diagnostics;

public sealed class EventJournal
{
    private readonly List<SimulationEvent> _events = [];
    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider;
    private long _nextSequence = 1;

    public EventJournal(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public long Count
    {
        get
        {
            lock (_sync)
            {
                return _events.Count;
            }
        }
    }

    public IReadOnlyList<SimulationEvent> Events
    {
        get
        {
            lock (_sync)
            {
                return _events.ToArray();
            }
        }
    }

    public SimulationEvent Record(
        long tick,
        string category,
        string message,
        Guid? tagId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        lock (_sync)
        {
            var simulationEvent = new SimulationEvent(
                _nextSequence++,
                tick,
                _timeProvider.GetUtcNow(),
                category.Trim(),
                message.Trim(),
                tagId);

            _events.Add(simulationEvent);
            return simulationEvent;
        }
    }
}
