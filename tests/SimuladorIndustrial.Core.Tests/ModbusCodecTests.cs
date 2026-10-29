using SimuladorIndustrial.Core.Drivers.Modbus;

namespace SimuladorIndustrial.Core.Tests;

public sealed class ModbusCodecTests
{
    [Fact]
    public void EncodesAndDecodesStrictMbapFrames()
    {
        byte[] pdu = [0x03, 0x00, 0x6B, 0x00, 0x03];

        var encoded = ModbusCodec.EncodeApplicationDataUnit(0x1234, 0x11, pdu);

        Assert.Equal(
            new byte[] { 0x12, 0x34, 0x00, 0x00, 0x00, 0x06, 0x11, 0x03, 0x00, 0x6B, 0x00, 0x03 },
            encoded);
        var decoded = ModbusCodec.DecodeApplicationDataUnit(encoded);
        Assert.Equal((ushort)0x1234, decoded.TransactionId);
        Assert.Equal((byte)0x11, decoded.UnitId);
        Assert.Equal(pdu, decoded.ProtocolDataUnit);
    }

    [Fact]
    public void RejectsInvalidProtocolLengthAndTrailingBytes()
    {
        byte[] invalidProtocol = [0, 1, 0, 1, 0, 2, 1, 3];
        byte[] invalidLength = [0, 1, 0, 0, 0, 1, 1, 3];
        byte[] trailingByte = [0, 1, 0, 0, 0, 2, 1, 3, 0];

        Assert.Throws<ModbusProtocolException>(
            () => ModbusCodec.DecodeApplicationDataUnit(invalidProtocol));
        Assert.Throws<ModbusProtocolException>(
            () => ModbusCodec.DecodeApplicationDataUnit(invalidLength));
        Assert.Throws<ModbusProtocolException>(
            () => ModbusCodec.DecodeApplicationDataUnit(trailingByte));
    }

    [Fact]
    public void PacksBitsLeastSignificantBitFirstAndRequiresZeroPadding()
    {
        bool[] values = [true, false, true, false, false, false, false, false, true];

        var packed = ModbusCodec.PackBits(values);

        Assert.Equal(new byte[] { 0x05, 0x01 }, packed);
        Assert.Equal(values, ModbusCodec.UnpackBits(packed, values.Length));
        Assert.Throws<ModbusProtocolException>(
            () => ModbusCodec.UnpackBits(new byte[] { 0x05, 0x81 }, values.Length));
    }

    [Fact]
    public void EncodesRegistersInNetworkByteOrder()
    {
        ushort[] registers = [0x1234, 0xABCD, 0x0001];

        var encoded = ModbusCodec.EncodeRegisters(registers);

        Assert.Equal(new byte[] { 0x12, 0x34, 0xAB, 0xCD, 0x00, 0x01 }, encoded);
        Assert.Equal(registers, ModbusCodec.DecodeRegisters(encoded));
        Assert.Throws<ModbusProtocolException>(
            () => ModbusCodec.DecodeRegisters(new byte[] { 0x00 }));
    }
}
