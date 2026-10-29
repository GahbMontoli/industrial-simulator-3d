using System.Buffers.Binary;

namespace SimuladorIndustrial.Core.Drivers.Modbus;

public enum ModbusFunctionCode : byte
{
    ReadCoils = 1,
    ReadDiscreteInputs = 2,
    ReadHoldingRegisters = 3,
    ReadInputRegisters = 4,
    WriteSingleCoil = 5,
    WriteSingleRegister = 6,
    WriteMultipleCoils = 15,
    WriteMultipleRegisters = 16
}

public enum ModbusExceptionCode : byte
{
    IllegalFunction = 1,
    IllegalDataAddress = 2,
    IllegalDataValue = 3,
    ServerDeviceFailure = 4,
    GatewayTargetDeviceFailedToRespond = 11
}

public static class ModbusLimits
{
    public const int MaximumPduLength = 253;
    public const int MaximumReadBits = 2000;
    public const int MaximumReadRegisters = 125;
    public const int MaximumWriteCoils = 1968;
    public const int MaximumWriteRegisters = 123;
}

public sealed record ModbusApplicationDataUnit(
    ushort TransactionId,
    byte UnitId,
    byte[] ProtocolDataUnit);

public sealed class ModbusProtocolException : IOException
{
    public ModbusProtocolException(string message)
        : base(message)
    {
    }
}

public sealed class ModbusServerException : IOException
{
    public ModbusServerException(byte functionCode, ModbusExceptionCode exceptionCode)
        : base($"Modbus function 0x{functionCode:X2} failed with exception {exceptionCode} (0x{(byte)exceptionCode:X2}).")
    {
        FunctionCode = functionCode;
        ExceptionCode = exceptionCode;
    }

    public byte FunctionCode { get; }

    public ModbusExceptionCode ExceptionCode { get; }
}

public static class ModbusCodec
{
    public static byte[] EncodeApplicationDataUnit(
        ushort transactionId,
        byte unitId,
        ReadOnlySpan<byte> protocolDataUnit)
    {
        if (protocolDataUnit.Length is < 1 or > ModbusLimits.MaximumPduLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(protocolDataUnit),
                $"A Modbus PDU must contain between 1 and {ModbusLimits.MaximumPduLength} bytes.");
        }

        var length = checked((ushort)(protocolDataUnit.Length + 1));
        var result = new byte[6 + length];
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(0, 2), transactionId);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(4, 2), length);
        result[6] = unitId;
        protocolDataUnit.CopyTo(result.AsSpan(7));
        return result;
    }

    public static ModbusApplicationDataUnit DecodeApplicationDataUnit(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
        {
            throw new ModbusProtocolException("A Modbus TCP ADU must contain an MBAP header and a non-empty PDU.");
        }

        var protocolId = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(2, 2));
        if (protocolId != 0)
        {
            throw new ModbusProtocolException($"Unsupported MBAP protocol identifier {protocolId}.");
        }

        var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(4, 2));
        if (length is < 2 or > ModbusLimits.MaximumPduLength + 1)
        {
            throw new ModbusProtocolException($"Invalid MBAP length {length}.");
        }

        if (data.Length != 6 + length)
        {
            throw new ModbusProtocolException(
                $"MBAP length {length} does not match the received ADU length {data.Length}.");
        }

        return new ModbusApplicationDataUnit(
            BinaryPrimitives.ReadUInt16BigEndian(data.Slice(0, 2)),
            data[6],
            data.Slice(7).ToArray());
    }

    public static byte[] PackBits(IReadOnlyList<bool> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            return [];
        }

        var result = new byte[(values.Count + 7) / 8];
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index])
            {
                result[index / 8] |= (byte)(1 << (index % 8));
            }
        }

        return result;
    }

    public static bool[] UnpackBits(ReadOnlySpan<byte> data, int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        var expectedLength = (quantity + 7) / 8;
        if (data.Length != expectedLength)
        {
            throw new ModbusProtocolException(
                $"Bit payload length {data.Length} does not match quantity {quantity}.");
        }

        var unusedBitCount = (8 - (quantity % 8)) % 8;
        if (unusedBitCount > 0)
        {
            var usedMask = (1 << (8 - unusedBitCount)) - 1;
            if ((data[^1] & ~usedMask) != 0)
            {
                throw new ModbusProtocolException("Unused bits in the final payload byte must be zero.");
            }
        }

        var result = new bool[quantity];
        for (var index = 0; index < quantity; index++)
        {
            result[index] = (data[index / 8] & (1 << (index % 8))) != 0;
        }

        return result;
    }

    public static byte[] EncodeRegisters(IReadOnlyList<ushort> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var result = new byte[checked(values.Count * 2)];
        for (var index = 0; index < values.Count; index++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(index * 2, 2), values[index]);
        }

        return result;
    }

    public static ushort[] DecodeRegisters(ReadOnlySpan<byte> data)
    {
        if (data.Length % 2 != 0)
        {
            throw new ModbusProtocolException("A register payload must contain an even number of bytes.");
        }

        var result = new ushort[data.Length / 2];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(index * 2, 2));
        }

        return result;
    }
}

internal sealed class ModbusRequestException : Exception
{
    public ModbusRequestException(
        byte functionCode,
        ModbusExceptionCode exceptionCode,
        string message)
        : base(message)
    {
        FunctionCode = functionCode;
        ExceptionCode = exceptionCode;
    }

    public ModbusRequestException(
        byte functionCode,
        ModbusExceptionCode exceptionCode,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        FunctionCode = functionCode;
        ExceptionCode = exceptionCode;
    }

    public byte FunctionCode { get; }

    public ModbusExceptionCode ExceptionCode { get; }
}

internal static class ModbusTcpTransport
{
    public static async ValueTask<ModbusApplicationDataUnit?> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var header = new byte[7];
        var hasData = await ReadExactlyOrEndAsync(stream, header, cancellationToken).ConfigureAwait(false);
        if (!hasData)
        {
            return null;
        }

        var protocolId = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2));
        if (protocolId != 0)
        {
            throw new ModbusProtocolException($"Unsupported MBAP protocol identifier {protocolId}.");
        }

        var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
        if (length is < 2 or > ModbusLimits.MaximumPduLength + 1)
        {
            throw new ModbusProtocolException($"Invalid MBAP length {length}.");
        }

        var pdu = new byte[length - 1];
        await ReadExactlyAsync(stream, pdu, cancellationToken).ConfigureAwait(false);
        return new ModbusApplicationDataUnit(
            BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(0, 2)),
            header[6],
            pdu);
    }

    public static async ValueTask WriteAsync(
        Stream stream,
        ModbusApplicationDataUnit applicationDataUnit,
        CancellationToken cancellationToken)
    {
        var buffer = ModbusCodec.EncodeApplicationDataUnit(
            applicationDataUnit.TransactionId,
            applicationDataUnit.UnitId,
            applicationDataUnit.ProtocolDataUnit);
        await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<bool> ReadExactlyOrEndAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(
                buffer.AsMemory(offset),
                cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                if (offset == 0)
                {
                    return false;
                }

                throw new ModbusProtocolException("The TCP connection ended in the middle of an MBAP header.");
            }

            offset += bytesRead;
        }

        return true;
    }

    private static async ValueTask ReadExactlyAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(
                buffer.AsMemory(offset),
                cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                throw new ModbusProtocolException("The TCP connection ended in the middle of a Modbus frame.");
            }

            offset += bytesRead;
        }
    }
}
