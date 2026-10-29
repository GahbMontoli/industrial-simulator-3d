using SimuladorIndustrial.Core.Runtime;
using SimuladorIndustrial.Core.Tags;

namespace SimuladorIndustrial.Core.Scenarios;

public sealed class DemoPlcControllerSystem : ISimulationSystem
{
    private readonly Guid _containerDetectedTagId;
    private readonly Guid _filledVolumeTagId;
    private readonly Guid _gateClosedTagId;
    private readonly Guid _fillValveTagId;
    private readonly float _targetVolumeLiters;
    private bool _releaseInProgress;

    public DemoPlcControllerSystem(
        Guid containerDetectedTagId,
        Guid filledVolumeTagId,
        Guid gateClosedTagId,
        Guid fillValveTagId,
        float targetVolumeLiters = 1f)
    {
        var ids = new[]
        {
            containerDetectedTagId,
            filledVolumeTagId,
            gateClosedTagId,
            fillValveTagId
        };
        if (ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length)
        {
            throw new ArgumentException("Demo PLC bindings must use distinct, non-empty tag ids.");
        }

        if (!float.IsFinite(targetVolumeLiters) || targetVolumeLiters <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetVolumeLiters),
                "The target volume must be finite and positive.");
        }

        _containerDetectedTagId = containerDetectedTagId;
        _filledVolumeTagId = filledVolumeTagId;
        _gateClosedTagId = gateClosedTagId;
        _fillValveTagId = fillValveTagId;
        _targetVolumeLiters = targetVolumeLiters;
    }

    public bool Enabled { get; set; } = true;

    public bool ReleaseInProgress => _releaseInProgress;

    public void Step(SimulationTick tick, TagRegistry tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        if (!Enabled)
        {
            return;
        }

        var containerDetected = tags.Get(_containerDetectedTagId).EffectiveValue.AsBool();
        var volumeLiters = tags.Get(_filledVolumeTagId).EffectiveValue.AsFloat32();

        if (_releaseInProgress)
        {
            WriteOutputs(tags, tick.Number, gateClosed: false, fillValveOpen: false);
            if (!containerDetected)
            {
                _releaseInProgress = false;
            }

            return;
        }

        if (volumeLiters >= _targetVolumeLiters)
        {
            _releaseInProgress = true;
            WriteOutputs(tags, tick.Number, gateClosed: false, fillValveOpen: false);
            return;
        }

        WriteOutputs(
            tags,
            tick.Number,
            gateClosed: containerDetected,
            fillValveOpen: containerDetected);
    }

    public void Reset(TagRegistry tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        ValidateTag(tags, _containerDetectedTagId, TagDataType.Bool, TagDirection.PlcInput);
        ValidateTag(tags, _filledVolumeTagId, TagDataType.Float32, TagDirection.PlcInput);
        ValidateTag(tags, _gateClosedTagId, TagDataType.Bool, TagDirection.PlcOutput);
        ValidateTag(tags, _fillValveTagId, TagDataType.Bool, TagDirection.PlcOutput);

        _releaseInProgress = false;
        if (Enabled)
        {
            WriteOutputs(tags, 0, gateClosed: false, fillValveOpen: false);
        }
    }

    private void WriteOutputs(
        TagRegistry tags,
        long tick,
        bool gateClosed,
        bool fillValveOpen)
    {
        tags.WriteFromPlc(_gateClosedTagId, TagValue.FromBool(gateClosed), tick);
        tags.WriteFromPlc(_fillValveTagId, TagValue.FromBool(fillValveOpen), tick);
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
}
