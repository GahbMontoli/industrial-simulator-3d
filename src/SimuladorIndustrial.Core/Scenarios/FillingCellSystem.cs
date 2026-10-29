using SimuladorIndustrial.Core.Runtime;
using SimuladorIndustrial.Core.Tags;

namespace SimuladorIndustrial.Core.Scenarios;

public sealed class FillingCellSystem : ISimulationSystem
{
    private readonly Guid _containerDetectedTagId;
    private readonly Guid _gateClosedTagId;
    private readonly Guid _fillValveTagId;
    private readonly Guid _filledVolumeTagId;
    private readonly float _flowLitersPerSecond;
    private readonly float _capacityLiters;
    private float _volumeLiters;

    public FillingCellSystem(
        Guid containerDetectedTagId,
        Guid gateClosedTagId,
        Guid fillValveTagId,
        Guid filledVolumeTagId,
        float flowLitersPerSecond = 0.25f,
        float capacityLiters = 1f)
    {
        EnsureTagId(containerDetectedTagId, nameof(containerDetectedTagId));
        EnsureTagId(gateClosedTagId, nameof(gateClosedTagId));
        EnsureTagId(fillValveTagId, nameof(fillValveTagId));
        EnsureTagId(filledVolumeTagId, nameof(filledVolumeTagId));

        if (new HashSet<Guid>
            {
                containerDetectedTagId,
                gateClosedTagId,
                fillValveTagId,
                filledVolumeTagId
            }.Count != 4)
        {
            throw new ArgumentException("Each filling-cell binding must use a distinct tag.");
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

        _containerDetectedTagId = containerDetectedTagId;
        _gateClosedTagId = gateClosedTagId;
        _fillValveTagId = fillValveTagId;
        _filledVolumeTagId = filledVolumeTagId;
        _flowLitersPerSecond = flowLitersPerSecond;
        _capacityLiters = capacityLiters;
    }

    public bool ContainerPresent { get; private set; }

    public float VolumeLiters => _volumeLiters;

    public void Step(SimulationTick tick, TagRegistry tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        var gateClosed = tags.Get(_gateClosedTagId).EffectiveValue.AsBool();
        var fillValveOpen = tags.Get(_fillValveTagId).EffectiveValue.AsBool();

        if (ContainerPresent && gateClosed && fillValveOpen)
        {
            var addedVolume = _flowLitersPerSecond * (float)tick.Step.TotalSeconds;
            _volumeLiters = MathF.Min(_capacityLiters, _volumeLiters + addedVolume);
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

        ContainerPresent = true;
        _volumeLiters = 0f;
        PublishProcessInputs(tags, 0);
    }

    private void PublishProcessInputs(TagRegistry tags, long tick)
    {
        tags.PublishFromSimulation(
            _containerDetectedTagId,
            TagValue.FromBool(ContainerPresent),
            tick);
        tags.PublishFromSimulation(
            _filledVolumeTagId,
            TagValue.FromFloat32(_volumeLiters),
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

    private static void EnsureTagId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Tag id cannot be empty.", parameterName);
        }
    }
}
