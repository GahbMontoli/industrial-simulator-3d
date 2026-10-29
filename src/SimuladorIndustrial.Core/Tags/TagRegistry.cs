using SimuladorIndustrial.Core.Diagnostics;

namespace SimuladorIndustrial.Core.Tags;

public sealed class TagRegistry
{
    private readonly Dictionary<Guid, TagState> _states = [];
    private readonly Dictionary<string, Guid> _idsByCanonicalName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<AddressKey, Guid> _idsByAddress = [];
    private readonly object _sync = new();
    private readonly EventJournal _journal;
    private readonly TimeProvider _timeProvider;

    public TagRegistry(EventJournal journal, TimeProvider? timeProvider = null)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _states.Count;
            }
        }
    }

    public void Register(TagDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        lock (_sync)
        {
            if (_states.ContainsKey(definition.Id))
            {
                throw new InvalidOperationException($"Tag id '{definition.Id}' is already registered.");
            }

            if (_idsByCanonicalName.ContainsKey(definition.CanonicalName))
            {
                throw new InvalidOperationException(
                    $"Tag canonical name '{definition.CanonicalName}' is already registered.");
            }

            var addressKeys = AddressKey.From(definition);
            foreach (var addressKey in addressKeys)
            {
                if (_idsByAddress.ContainsKey(addressKey))
                {
                    throw new InvalidOperationException(
                        $"Address {addressKey.Address} is already used in the {addressKey.Space} space.");
                }
            }

            var initialValue = TagValue.Zero(definition.DataType);
            var state = new TagState(definition, initialValue, _timeProvider.GetUtcNow());

            _states.Add(definition.Id, state);
            _idsByCanonicalName.Add(definition.CanonicalName, definition.Id);
            foreach (var addressKey in addressKeys)
            {
                _idsByAddress.Add(addressKey, definition.Id);
            }
            _journal.Record(0, "tag.registered", $"Registered tag '{definition.CanonicalName}'.", definition.Id);
        }
    }

    public TagSnapshot PublishFromSimulation(Guid id, TagValue value, long tick)
    {
        return UpdateRawValue(id, value, tick, TagDirection.PlcInput, "simulation");
    }

    public TagSnapshot WriteFromPlc(Guid id, TagValue value, long tick)
    {
        return UpdateRawValue(id, value, tick, TagDirection.PlcOutput, "plc");
    }

    public TagSnapshot Force(Guid id, TagValue value, long tick)
    {
        ValidateTick(tick);

        lock (_sync)
        {
            var state = GetState(id);
            EnsureValueType(state.Definition, value);

            if (!state.IsForced || state.ForcedValue != value)
            {
                state.IsForced = true;
                state.ForcedValue = value;
                MarkChanged(state, tick);
                _journal.Record(tick, "tag.force", $"Forced tag '{state.Definition.CanonicalName}'.", id);
            }

            return state.Snapshot();
        }
    }

    public TagSnapshot ReleaseForce(Guid id, long tick)
    {
        ValidateTick(tick);

        lock (_sync)
        {
            var state = GetState(id);
            if (state.IsForced)
            {
                state.IsForced = false;
                state.ForcedValue = null;
                MarkChanged(state, tick);
                _journal.Record(tick, "tag.force-released", $"Released tag '{state.Definition.CanonicalName}'.", id);
            }

            return state.Snapshot();
        }
    }

    public TagSnapshot SetQuality(Guid id, TagQuality quality, long tick)
    {
        ValidateTick(tick);
        if (!Enum.IsDefined(quality))
        {
            throw new ArgumentOutOfRangeException(nameof(quality));
        }

        lock (_sync)
        {
            var state = GetState(id);
            if (state.Quality != quality)
            {
                state.Quality = quality;
                MarkChanged(state, tick);
                _journal.Record(
                    tick,
                    "tag.quality",
                    $"Changed tag '{state.Definition.CanonicalName}' quality to {quality}.",
                    id);
            }

            return state.Snapshot();
        }
    }

    public TagSnapshot SetFault(Guid id, TagFault fault, long tick)
    {
        ValidateTick(tick);
        if (!Enum.IsDefined(fault))
        {
            throw new ArgumentOutOfRangeException(nameof(fault));
        }

        lock (_sync)
        {
            var state = GetState(id);
            if (state.Fault != fault)
            {
                var valueBeforeChange = state.EffectiveValue;
                state.Fault = fault;
                state.StuckValue = fault == TagFault.Stuck ? valueBeforeChange : null;
                state.NoiseValue = fault == TagFault.Noise
                    ? ApplyNoise(state.Definition, valueBeforeChange, tick)
                    : null;
                MarkChanged(state, tick);
                _journal.Record(
                    tick,
                    "tag.fault",
                    $"Changed tag '{state.Definition.CanonicalName}' fault to {fault}.",
                    id);
            }

            return state.Snapshot();
        }
    }

    public TagSnapshot Get(Guid id)
    {
        lock (_sync)
        {
            return GetState(id).Snapshot();
        }
    }

    public IReadOnlyList<TagSnapshot> GetAll()
    {
        lock (_sync)
        {
            return _states.Values
                .OrderBy(state => state.Definition.CanonicalName, StringComparer.OrdinalIgnoreCase)
                .Select(state => state.Snapshot())
                .ToArray();
        }
    }

    public void ResetRuntimeState(long tick)
    {
        ValidateTick(tick);

        lock (_sync)
        {
            foreach (var state in _states.Values)
            {
                state.RawValue = TagValue.Zero(state.Definition.DataType);
                state.Quality = TagQuality.Good;
                state.IsForced = false;
                state.ForcedValue = null;
                state.Fault = TagFault.None;
                state.StuckValue = null;
                state.NoiseValue = null;
                MarkChanged(state, tick);
            }

            _journal.Record(tick, "tags.reset", "Reset all tag runtime state.");
        }
    }

    private TagSnapshot UpdateRawValue(
        Guid id,
        TagValue value,
        long tick,
        TagDirection requiredDirection,
        string source)
    {
        ValidateTick(tick);

        lock (_sync)
        {
            var state = GetState(id);
            if (state.Definition.Direction != requiredDirection)
            {
                throw new InvalidOperationException(
                    $"Tag '{state.Definition.CanonicalName}' has direction {state.Definition.Direction}; " +
                    $"it cannot receive a value from {source}.");
            }

            EnsureValueType(state.Definition, value);
            var rawChanged = state.RawValue != value;
            if (rawChanged)
            {
                state.RawValue = value;
                MarkChanged(state, tick);
                _journal.Record(
                    tick,
                    "tag.value",
                    $"Updated tag '{state.Definition.CanonicalName}' from {source}.",
                    id);
            }

            if (state.Fault == TagFault.Noise)
            {
                var noisyValue = ApplyNoise(state.Definition, value, tick);
                if (state.NoiseValue != noisyValue)
                {
                    state.NoiseValue = noisyValue;
                    MarkChanged(state, tick);
                }
            }

            return state.Snapshot();
        }
    }

    private TagState GetState(Guid id)
    {
        if (!_states.TryGetValue(id, out var state))
        {
            throw new KeyNotFoundException($"Tag id '{id}' is not registered.");
        }

        return state;
    }

    private void MarkChanged(TagState state, long tick)
    {
        state.LastChangedTick = tick;
        state.LastChangedAtUtc = _timeProvider.GetUtcNow();
    }

    private static TagValue ApplyNoise(TagDefinition definition, TagValue value, long tick)
    {
        var bytes = definition.Id.ToByteArray();
        var seed = BitConverter.ToUInt64(bytes, 0) ^ unchecked((ulong)tick * 0x9E3779B97F4A7C15UL);
        seed ^= seed >> 12;
        seed ^= seed << 25;
        seed ^= seed >> 27;
        var sample = seed * 0x2545F4914F6CDD1DUL;

        return value.DataType switch
        {
            TagDataType.Bool => TagValue.FromBool(value.AsBool() ^ ((sample & 3UL) == 0UL)),
            TagDataType.Int32 => TagValue.FromInt32(checked((int)Math.Clamp(
                (long)value.AsInt32() + (long)(sample % 7UL) - 3L,
                int.MinValue,
                int.MaxValue))),
            TagDataType.Float32 => TagValue.FromFloat32(ApplyFloatNoise(value.AsFloat32(), sample)),
            _ => throw new InvalidOperationException($"Unsupported tag data type: {value.DataType}.")
        };
    }

    private static float ApplyFloatNoise(float value, ulong sample)
    {
        var normalized = (sample & 0xFFFFUL) / (double)ushort.MaxValue;
        var amplitude = Math.Max(Math.Abs(value) * 0.05d, 0.01d);
        var noisyValue = value + (normalized * 2d - 1d) * amplitude;
        return (float)Math.Clamp(noisyValue, -float.MaxValue, float.MaxValue);
    }

    private static void EnsureValueType(TagDefinition definition, TagValue value)
    {
        if (definition.DataType != value.DataType)
        {
            throw new ArgumentException(
                $"Tag '{definition.CanonicalName}' expects {definition.DataType}, not {value.DataType}.",
                nameof(value));
        }
    }

    private static void ValidateTick(long tick) =>
        ArgumentOutOfRangeException.ThrowIfNegative(tick);

    private enum AddressSpace
    {
        DiscreteInput,
        Coil,
        InputRegister,
        HoldingRegister
    }

    private readonly record struct AddressKey(AddressSpace Space, int Address)
    {
        public static IReadOnlyList<AddressKey> From(TagDefinition definition)
        {
            var isBoolean = definition.DataType == TagDataType.Bool;
            var space = (definition.Direction, isBoolean) switch
            {
                (TagDirection.PlcInput, true) => AddressSpace.DiscreteInput,
                (TagDirection.PlcOutput, true) => AddressSpace.Coil,
                (TagDirection.PlcInput, false) => AddressSpace.InputRegister,
                (TagDirection.PlcOutput, false) => AddressSpace.HoldingRegister,
                _ => throw new InvalidOperationException("The tag direction is invalid.")
            };

            return isBoolean
                ? [new AddressKey(space, definition.Address)]
                :
                [
                    new AddressKey(space, definition.Address),
                    new AddressKey(space, definition.Address + 1)
                ];
        }
    }

    private sealed class TagState
    {
        public TagState(TagDefinition definition, TagValue initialValue, DateTimeOffset changedAtUtc)
        {
            Definition = definition;
            RawValue = initialValue;
            Quality = TagQuality.Good;
            LastChangedAtUtc = changedAtUtc;
        }

        public TagDefinition Definition { get; }

        public TagValue RawValue { get; set; }

        public TagQuality Quality { get; set; }

        public bool IsForced { get; set; }

        public TagValue? ForcedValue { get; set; }

        public TagFault Fault { get; set; }

        public TagValue? StuckValue { get; set; }

        public TagValue? NoiseValue { get; set; }

        public long LastChangedTick { get; set; }

        public DateTimeOffset LastChangedAtUtc { get; set; }

        // Fault injection intentionally has higher precedence than diagnostic forcing.
        public TagValue EffectiveValue => Fault switch
        {
            TagFault.AlwaysOff => TagValue.Zero(Definition.DataType),
            TagFault.AlwaysOn => TagValue.One(Definition.DataType),
            TagFault.Stuck => StuckValue ?? RawValue,
            TagFault.Noise => NoiseValue ?? RawValue,
            _ when IsForced => ForcedValue ?? RawValue,
            _ => RawValue
        };

        public TagSnapshot Snapshot() => new(
            Definition,
            RawValue,
            EffectiveValue,
            Quality,
            IsForced,
            ForcedValue,
            Fault,
            LastChangedTick,
            LastChangedAtUtc);
    }
}
