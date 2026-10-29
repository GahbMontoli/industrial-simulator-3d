using SimuladorIndustrial.Core.Diagnostics;
using SimuladorIndustrial.Core.Tags;

namespace SimuladorIndustrial.Core.Drivers.Modbus;

public sealed record ModbusSynchronizationResult(
    long Tick,
    int SynchronizedTags,
    int ChangedValues);

public sealed class ModbusTcpClientSynchronizer
{
    private readonly ModbusTcpClient _client;
    private readonly TagRegistry _registry;
    private readonly EventJournal _journal;
    private readonly SemaphoreSlim _cycleGate = new(1, 1);

    private ModbusSynchronizationResult? _lastResult;
    private string? _lastError;
    private int _lastOutcome;

    public ModbusTcpClientSynchronizer(
        ModbusTcpClient client,
        TagRegistry registry,
        EventJournal journal)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
    }

    public ModbusSynchronizationResult? LastResult => Volatile.Read(ref _lastResult);

    public string? LastError => Volatile.Read(ref _lastError);

    public async Task<ModbusSynchronizationResult> SynchronizeOnceAsync(
        long tick,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick);
        await _cycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tags = _registry.GetAll();
            var values = new List<RemoteTagValue>(tags.Count);

            await ReadBitAreaAsync(
                tags,
                TagDirection.PlcInput,
                _client.ReadDiscreteInputsAsync,
                values,
                cancellationToken).ConfigureAwait(false);
            await ReadRegisterAreaAsync(
                tags,
                TagDirection.PlcInput,
                _client.ReadInputRegistersAsync,
                values,
                cancellationToken).ConfigureAwait(false);
            await ReadBitAreaAsync(
                tags,
                TagDirection.PlcOutput,
                _client.ReadCoilsAsync,
                values,
                cancellationToken).ConfigureAwait(false);
            await ReadRegisterAreaAsync(
                tags,
                TagDirection.PlcOutput,
                _client.ReadHoldingRegistersAsync,
                values,
                cancellationToken).ConfigureAwait(false);

            var changedValues = ApplyValues(values, tick);
            SetQuality(tags, TagQuality.Good, tick);

            var result = new ModbusSynchronizationResult(tick, values.Count, changedValues);
            Volatile.Write(ref _lastResult, result);
            Volatile.Write(ref _lastError, null);
            if (Interlocked.Exchange(ref _lastOutcome, 1) != 1)
            {
                _journal.Record(
                    tick,
                    "modbus.client.sync-succeeded",
                    $"Synchronized {values.Count} Modbus tags; {changedValues} values changed.");
            }

            return result;
        }
        catch (Exception exception)
        {
            var quality = GetFailureQuality(exception);
            SetQuality(_registry.GetAll(), quality, tick);
            Volatile.Write(ref _lastError, exception.Message);
            if (Interlocked.Exchange(ref _lastOutcome, 2) != 2)
            {
                _journal.Record(
                    tick,
                    "modbus.client.sync-failed",
                    $"Modbus synchronization failed with {exception.GetType().Name}: {exception.Message}");
            }

            throw;
        }
        finally
        {
            _cycleGate.Release();
        }
    }

    private static async Task ReadBitAreaAsync(
        IReadOnlyList<TagSnapshot> allTags,
        TagDirection direction,
        Func<ushort, ushort, CancellationToken, Task<bool[]>> read,
        ICollection<RemoteTagValue> destination,
        CancellationToken cancellationToken)
    {
        var tags = allTags
            .Where(tag =>
                tag.Definition.Direction == direction &&
                tag.Definition.DataType == TagDataType.Bool)
            .OrderBy(tag => tag.Definition.Address)
            .ToArray();

        var index = 0;
        while (index < tags.Length)
        {
            var groupStart = index;
            var startAddress = tags[index].Definition.Address;
            index++;
            while (index < tags.Length &&
                   tags[index].Definition.Address == tags[index - 1].Definition.Address + 1 &&
                   index - groupStart < ModbusLimits.MaximumReadBits)
            {
                index++;
            }

            var quantity = index - groupStart;
            var remoteValues = await read(
                checked((ushort)startAddress),
                checked((ushort)quantity),
                cancellationToken).ConfigureAwait(false);
            for (var offset = 0; offset < quantity; offset++)
            {
                destination.Add(new RemoteTagValue(
                    tags[groupStart + offset],
                    TagValue.FromBool(remoteValues[offset])));
            }
        }
    }

    private static async Task ReadRegisterAreaAsync(
        IReadOnlyList<TagSnapshot> allTags,
        TagDirection direction,
        Func<ushort, ushort, CancellationToken, Task<ushort[]>> read,
        ICollection<RemoteTagValue> destination,
        CancellationToken cancellationToken)
    {
        var tags = allTags
            .Where(tag =>
                tag.Definition.Direction == direction &&
                tag.Definition.DataType != TagDataType.Bool)
            .OrderBy(tag => tag.Definition.Address)
            .ToArray();

        var index = 0;
        while (index < tags.Length)
        {
            var groupStart = index;
            var startAddress = tags[index].Definition.Address;
            var registerCount = 2;
            index++;
            while (index < tags.Length &&
                   tags[index].Definition.Address == startAddress + registerCount &&
                   registerCount + 2 <= ModbusLimits.MaximumReadRegisters)
            {
                registerCount += 2;
                index++;
            }

            var registers = await read(
                checked((ushort)startAddress),
                checked((ushort)registerCount),
                cancellationToken).ConfigureAwait(false);
            for (var tagIndex = groupStart; tagIndex < index; tagIndex++)
            {
                var offset = tags[tagIndex].Definition.Address - startAddress;
                var bits = ((uint)registers[offset] << 16) | registers[offset + 1];
                destination.Add(new RemoteTagValue(
                    tags[tagIndex],
                    DecodeRegisterValue(tags[tagIndex].Definition.DataType, bits)));
            }
        }
    }

    private int ApplyValues(IReadOnlyCollection<RemoteTagValue> values, long tick)
    {
        var changedValues = 0;
        foreach (var item in values)
        {
            if (item.Snapshot.RawValue != item.Value)
            {
                changedValues++;
            }

            if (item.Snapshot.Definition.Direction == TagDirection.PlcInput)
            {
                _registry.PublishFromSimulation(item.Snapshot.Definition.Id, item.Value, tick);
            }
            else
            {
                _registry.WriteFromPlc(item.Snapshot.Definition.Id, item.Value, tick);
            }
        }

        return changedValues;
    }

    private void SetQuality(
        IReadOnlyList<TagSnapshot> tags,
        TagQuality quality,
        long tick)
    {
        foreach (var tag in tags.Where(tag => tag.Quality != quality))
        {
            _registry.SetQuality(tag.Definition.Id, quality, tick);
        }
    }

    private TagQuality GetFailureQuality(Exception exception)
    {
        if (!_client.IsConnected)
        {
            return TagQuality.Disconnected;
        }

        return exception is OperationCanceledException
            ? TagQuality.Stale
            : TagQuality.Bad;
    }

    private static TagValue DecodeRegisterValue(TagDataType dataType, uint bits) => dataType switch
    {
        TagDataType.Int32 => TagValue.FromInt32(unchecked((int)bits)),
        TagDataType.Float32 => TagValue.FromFloat32(
            BitConverter.Int32BitsToSingle(unchecked((int)bits))),
        _ => throw new InvalidOperationException($"Unsupported register tag type {dataType}.")
    };

    private sealed record RemoteTagValue(TagSnapshot Snapshot, TagValue Value);
}
