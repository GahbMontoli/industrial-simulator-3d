using SimuladorIndustrial.Core.Runtime;
using SimuladorIndustrial.Core.Tags;

namespace SimuladorIndustrial.Core.Scenarios;

public sealed class AnimatedFillingLineSystem : ISimulationSystem
{
    private readonly Guid _containerDetectedTagId;
    private readonly Guid _gateClosedTagId;
    private readonly Guid _fillValveTagId;
    private readonly Guid _filledVolumeTagId;
    private readonly double _sensorHalfWidthMeters;
    private double _waitingSeconds;

    public AnimatedFillingLineSystem(
        Guid containerDetectedTagId,
        Guid gateClosedTagId,
        Guid fillValveTagId,
        Guid filledVolumeTagId,
        double spawnX = -2.0,
        double stationX = 0.6,
        double exitX = 2.2,
        double conveyorSpeedMetersPerSecond = 0.6,
        float flowLitersPerSecond = 0.25f,
        float capacityLiters = 1f,
        double respawnDelaySeconds = 1.0,
        double sensorHalfWidthMeters = 0.08)
    {
        EnsureDistinctTagIds(
            containerDetectedTagId,
            gateClosedTagId,
            fillValveTagId,
            filledVolumeTagId);

        if (!double.IsFinite(spawnX) ||
            !double.IsFinite(stationX) ||
            !double.IsFinite(exitX) ||
            spawnX >= stationX ||
            stationX >= exitX)
        {
            throw new ArgumentException("The positions must be finite and ordered as spawn < station < exit.");
        }

        if (!double.IsFinite(conveyorSpeedMetersPerSecond) || conveyorSpeedMetersPerSecond <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(conveyorSpeedMetersPerSecond),
                "The conveyor speed must be finite and positive.");
        }

        if (!float.IsFinite(flowLitersPerSecond) || flowLitersPerSecond <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(flowLitersPerSecond),
                "The filling flow must be finite and positive.");
        }

        if (!float.IsFinite(capacityLiters) || capacityLiters <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacityLiters),
                "The container capacity must be finite and positive.");
        }

        if (!double.IsFinite(respawnDelaySeconds) || respawnDelaySeconds < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(respawnDelaySeconds),
                "The respawn delay must be finite and non-negative.");
        }

        if (!double.IsFinite(sensorHalfWidthMeters) || sensorHalfWidthMeters <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sensorHalfWidthMeters),
                "The sensor half-width must be finite and positive.");
        }

        _containerDetectedTagId = containerDetectedTagId;
        _gateClosedTagId = gateClosedTagId;
        _fillValveTagId = fillValveTagId;
        _filledVolumeTagId = filledVolumeTagId;
        SpawnX = spawnX;
        StationX = stationX;
        ExitX = exitX;
        ConveyorSpeedMetersPerSecond = conveyorSpeedMetersPerSecond;
        FlowLitersPerSecond = flowLitersPerSecond;
        CapacityLiters = capacityLiters;
        RespawnDelaySeconds = respawnDelaySeconds;
        _sensorHalfWidthMeters = sensorHalfWidthMeters;
        ContainerX = spawnX;
    }

    public double SpawnX { get; }

    public double StationX { get; }

    public double ExitX { get; }

    public double ConveyorSpeedMetersPerSecond { get; }

    public float FlowLitersPerSecond { get; }

    public float CapacityLiters { get; }

    public double RespawnDelaySeconds { get; }

    public double ContainerX { get; private set; }

    public float VolumeLiters { get; private set; }

    public float FillRatio => Math.Clamp(VolumeLiters / CapacityLiters, 0f, 1f);

    public double BeltTravelMeters { get; private set; }

    public bool SensorActive { get; private set; }

    public bool ContainerVisible => Phase != FillingLinePhase.WaitingForNextContainer;

    public FillingLinePhase Phase { get; private set; }

    public void Step(SimulationTick tick, TagRegistry tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        var seconds = tick.Step.TotalSeconds;
        BeltTravelMeters += ConveyorSpeedMetersPerSecond * seconds;
        var gateClosed = tags.Get(_gateClosedTagId).EffectiveValue.AsBool();
        var fillValveOpen = tags.Get(_fillValveTagId).EffectiveValue.AsBool();

        switch (Phase)
        {
            case FillingLinePhase.Approaching:
                StepApproaching(seconds, gateClosed);
                break;
            case FillingLinePhase.Filling:
                StepFilling(seconds, gateClosed, fillValveOpen);
                break;
            case FillingLinePhase.Departing:
                StepDeparting(seconds);
                break;
            case FillingLinePhase.WaitingForNextContainer:
                StepWaiting(seconds);
                break;
            default:
                throw new InvalidOperationException($"Unsupported filling-line phase: {Phase}.");
        }

        PublishProcessInputs(tags, tick.Number);
    }

    public void Reset(TagRegistry tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        ValidateTag(tags, _containerDetectedTagId, TagDataType.Bool, TagDirection.PlcInput);
        ValidateTag(tags, _gateClosedTagId, TagDataType.Bool, TagDirection.PlcOutput);
        ValidateTag(tags, _fillValveTagId, TagDataType.Bool, TagDirection.PlcOutput);
        ValidateTag(tags, _filledVolumeTagId, TagDataType.Float32, TagDirection.PlcInput);

        ContainerX = SpawnX;
        VolumeLiters = 0f;
        BeltTravelMeters = 0d;
        SensorActive = false;
        Phase = FillingLinePhase.Approaching;
        _waitingSeconds = 0d;
        PublishProcessInputs(tags, 0);
    }

    private void StepApproaching(double seconds, bool gateClosed)
    {
        ContainerX += ConveyorSpeedMetersPerSecond * seconds;
        SensorActive = IsInsideSensor(ContainerX);

        if (SensorActive && gateClosed)
        {
            ContainerX = StationX;
            Phase = FillingLinePhase.Filling;
            return;
        }

        if (ContainerX > StationX + _sensorHalfWidthMeters)
        {
            Phase = FillingLinePhase.Departing;
        }
    }

    private void StepFilling(double seconds, bool gateClosed, bool fillValveOpen)
    {
        ContainerX = StationX;
        SensorActive = true;

        if (!gateClosed)
        {
            Phase = FillingLinePhase.Departing;
            ContainerX += ConveyorSpeedMetersPerSecond * seconds;
            SensorActive = IsInsideSensor(ContainerX);
            return;
        }

        if (fillValveOpen)
        {
            var addedVolume = FlowLitersPerSecond * (float)seconds;
            VolumeLiters = MathF.Min(CapacityLiters, VolumeLiters + addedVolume);
        }
    }

    private void StepDeparting(double seconds)
    {
        ContainerX += ConveyorSpeedMetersPerSecond * seconds;
        SensorActive = IsInsideSensor(ContainerX);

        if (ContainerX >= ExitX)
        {
            ContainerX = ExitX;
            SensorActive = false;
            Phase = FillingLinePhase.WaitingForNextContainer;
            _waitingSeconds = 0d;
        }
    }

    private void StepWaiting(double seconds)
    {
        SensorActive = false;
        _waitingSeconds += seconds;
        if (_waitingSeconds < RespawnDelaySeconds)
        {
            return;
        }

        ContainerX = SpawnX;
        VolumeLiters = 0f;
        SensorActive = false;
        Phase = FillingLinePhase.Approaching;
        _waitingSeconds = 0d;
    }

    private bool IsInsideSensor(double position) =>
        Math.Abs(position - StationX) <= _sensorHalfWidthMeters;

    private void PublishProcessInputs(TagRegistry tags, long tick)
    {
        tags.PublishFromSimulation(
            _containerDetectedTagId,
            TagValue.FromBool(SensorActive),
            tick);
        tags.PublishFromSimulation(
            _filledVolumeTagId,
            TagValue.FromFloat32(VolumeLiters),
            tick);
    }

    private static void ValidateTag(
        TagRegistry tags,
        Guid id,
        TagDataType expectedType,
        TagDirection expectedDirection)
    {
        var definition = tags.Get(id).Definition;
        if (definition.DataType != expectedType || definition.Direction != expectedDirection)
        {
            throw new InvalidOperationException(
                $"Tag '{definition.CanonicalName}' must be {expectedDirection}/{expectedType}.");
        }
    }

    private static void EnsureDistinctTagIds(params Guid[] ids)
    {
        if (ids.Any(id => id == Guid.Empty))
        {
            throw new ArgumentException("Filling-line tag ids cannot be empty.", nameof(ids));
        }

        if (ids.Distinct().Count() != ids.Length)
        {
            throw new ArgumentException("Each filling-line binding must use a distinct tag.", nameof(ids));
        }
    }
}
