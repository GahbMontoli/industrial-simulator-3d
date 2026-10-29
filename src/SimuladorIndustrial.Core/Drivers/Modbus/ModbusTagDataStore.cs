using SimuladorIndustrial.Core.Diagnostics;
using SimuladorIndustrial.Core.Tags;

namespace SimuladorIndustrial.Core.Drivers.Modbus;

public sealed class ModbusTagDataStore
{
    private readonly TagRegistry _registry;
    private readonly EventJournal _journal;
    private readonly object _writeSync = new();

    public ModbusTagDataStore(TagRegistry registry, EventJournal journal)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
    }

    public bool[] ReadCoils(ushort startAddress, ushort quantity) =>
        ReadBits(startAddress, quantity, TagDirection.PlcOutput, (byte)ModbusFunctionCode.ReadCoils);

    public bool[] ReadDiscreteInputs(ushort startAddress, ushort quantity) =>
        ReadBits(startAddress, quantity, TagDirection.PlcInput, (byte)ModbusFunctionCode.ReadDiscreteInputs);

    public ushort[] ReadHoldingRegisters(ushort startAddress, ushort quantity) =>
        ReadRegisters(
            startAddress,
            quantity,
            TagDirection.PlcOutput,
            (byte)ModbusFunctionCode.ReadHoldingRegisters);

    public ushort[] ReadInputRegisters(ushort startAddress, ushort quantity) =>
        ReadRegisters(
            startAddress,
            quantity,
            TagDirection.PlcInput,
            (byte)ModbusFunctionCode.ReadInputRegisters);

    public void WriteCoils(ushort startAddress, IReadOnlyList<bool> values, long tick)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentOutOfRangeException.ThrowIfNegative(tick);

        lock (_writeSync)
        {
            var map = CreateBooleanMap(TagDirection.PlcOutput);
            var pending = new List<(TagSnapshot Snapshot, bool Value)>(values.Count);
            for (var index = 0; index < values.Count; index++)
            {
                var address = startAddress + index;
                if (!map.TryGetValue(address, out var snapshot))
                {
                    throw IllegalAddress((byte)ModbusFunctionCode.WriteMultipleCoils, address);
                }

                pending.Add((snapshot, values[index]));
            }

            foreach (var item in pending)
            {
                var value = TagValue.FromBool(item.Value);
                var updated = _registry.WriteFromPlc(item.Snapshot.Definition.Id, value, tick);
                RecordChange(item.Snapshot, updated, tick);
            }
        }
    }

    public void WriteHoldingRegisters(ushort startAddress, IReadOnlyList<ushort> values, long tick)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentOutOfRangeException.ThrowIfNegative(tick);

        lock (_writeSync)
        {
            var map = CreateRegisterMap(TagDirection.PlcOutput);
            var pending = new Dictionary<Guid, PendingRegisterWrite>();

            for (var index = 0; index < values.Count; index++)
            {
                var address = startAddress + index;
                if (!map.TryGetValue(address, out var register))
                {
                    throw IllegalAddress((byte)ModbusFunctionCode.WriteMultipleRegisters, address);
                }

                if (!pending.TryGetValue(register.Snapshot.Definition.Id, out var write))
                {
                    write = new PendingRegisterWrite(
                        register.Snapshot,
                        GetStorageBits(register.Snapshot.RawValue));
                    pending.Add(register.Snapshot.Definition.Id, write);
                }

                write.SetWord(register.WordOffset, values[index]);
            }

            (TagSnapshot Snapshot, TagValue Value)[] prepared;
            try
            {
                prepared = pending.Values
                    .Select(write => (write.Snapshot, ToTagValue(write.Snapshot.Definition.DataType, write.Bits)))
                    .ToArray();
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new ModbusRequestException(
                    (byte)ModbusFunctionCode.WriteMultipleRegisters,
                    ModbusExceptionCode.IllegalDataValue,
                    "The register payload does not represent a finite tag value.",
                    exception);
            }

            foreach (var item in prepared)
            {
                var updated = _registry.WriteFromPlc(item.Snapshot.Definition.Id, item.Value, tick);
                RecordChange(item.Snapshot, updated, tick);
            }
        }
    }

    private bool[] ReadBits(
        ushort startAddress,
        ushort quantity,
        TagDirection direction,
        byte functionCode)
    {
        var map = CreateBooleanMap(direction);
        var result = new bool[quantity];
        for (var index = 0; index < quantity; index++)
        {
            var address = startAddress + index;
            if (!map.TryGetValue(address, out var snapshot))
            {
                throw IllegalAddress(functionCode, address);
            }

            result[index] = snapshot.EffectiveValue.AsBool();
        }

        return result;
    }

    private ushort[] ReadRegisters(
        ushort startAddress,
        ushort quantity,
        TagDirection direction,
        byte functionCode)
    {
        var map = CreateRegisterMap(direction);
        var result = new ushort[quantity];
        for (var index = 0; index < quantity; index++)
        {
            var address = startAddress + index;
            if (!map.TryGetValue(address, out var register))
            {
                throw IllegalAddress(functionCode, address);
            }

            var bits = GetStorageBits(register.Snapshot.EffectiveValue);
            result[index] = register.WordOffset == 0
                ? (ushort)(bits >> 16)
                : (ushort)bits;
        }

        return result;
    }

    private Dictionary<int, TagSnapshot> CreateBooleanMap(TagDirection direction) =>
        _registry.GetAll()
            .Where(snapshot =>
                snapshot.Definition.Direction == direction &&
                snapshot.Definition.DataType == TagDataType.Bool)
            .ToDictionary(snapshot => snapshot.Definition.Address);

    private Dictionary<int, RegisterReference> CreateRegisterMap(TagDirection direction)
    {
        var map = new Dictionary<int, RegisterReference>();
        foreach (var snapshot in _registry.GetAll().Where(snapshot =>
                     snapshot.Definition.Direction == direction &&
                     snapshot.Definition.DataType != TagDataType.Bool))
        {
            map.Add(snapshot.Definition.Address, new RegisterReference(snapshot, 0));
            map.Add(snapshot.Definition.Address + 1, new RegisterReference(snapshot, 1));
        }

        return map;
    }

    private void RecordChange(TagSnapshot before, TagSnapshot after, long tick)
    {
        if (before.RawValue != after.RawValue)
        {
            _journal.Record(
                tick,
                "modbus.tag-change",
                $"Modbus updated tag '{after.Definition.CanonicalName}'.",
                after.Definition.Id);
        }
    }

    private static uint GetStorageBits(TagValue value) => value.DataType switch
    {
        TagDataType.Int32 => unchecked((uint)value.AsInt32()),
        TagDataType.Float32 => unchecked((uint)BitConverter.SingleToInt32Bits(value.AsFloat32())),
        _ => throw new InvalidOperationException("Boolean tags do not use register storage.")
    };

    private static TagValue ToTagValue(TagDataType dataType, uint bits) => dataType switch
    {
        TagDataType.Int32 => TagValue.FromInt32(unchecked((int)bits)),
        TagDataType.Float32 => TagValue.FromFloat32(BitConverter.Int32BitsToSingle(unchecked((int)bits))),
        _ => throw new InvalidOperationException("Boolean tags do not use register storage.")
    };

    private static ModbusRequestException IllegalAddress(byte functionCode, int address) =>
        new(
            functionCode,
            ModbusExceptionCode.IllegalDataAddress,
            $"Address {address} is not mapped in the requested Modbus data area.");

    private readonly record struct RegisterReference(TagSnapshot Snapshot, int WordOffset);

    private sealed class PendingRegisterWrite
    {
        public PendingRegisterWrite(TagSnapshot snapshot, uint bits)
        {
            Snapshot = snapshot;
            Bits = bits;
        }

        public TagSnapshot Snapshot { get; }

        public uint Bits { get; private set; }

        public void SetWord(int wordOffset, ushort value)
        {
            Bits = wordOffset switch
            {
                0 => (Bits & 0x0000FFFFu) | ((uint)value << 16),
                1 => (Bits & 0xFFFF0000u) | value,
                _ => throw new ArgumentOutOfRangeException(nameof(wordOffset))
            };
        }
    }
}
