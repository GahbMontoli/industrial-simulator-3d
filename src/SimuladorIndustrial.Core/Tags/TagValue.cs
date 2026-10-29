using System.Globalization;

namespace SimuladorIndustrial.Core.Tags;

public readonly struct TagValue : IEquatable<TagValue>
{
    private readonly int _storage;

    private TagValue(TagDataType dataType, int storage)
    {
        DataType = dataType;
        _storage = storage;
    }

    public TagDataType DataType { get; }

    public static TagValue FromBool(bool value) =>
        new(TagDataType.Bool, value ? 1 : 0);

    public static TagValue FromInt32(int value) =>
        new(TagDataType.Int32, value);

    public static TagValue FromFloat32(float value)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Float tag values must be finite.");
        }

        return new TagValue(TagDataType.Float32, BitConverter.SingleToInt32Bits(value));
    }

    public static TagValue Zero(TagDataType dataType) => dataType switch
    {
        TagDataType.Bool => FromBool(false),
        TagDataType.Int32 => FromInt32(0),
        TagDataType.Float32 => FromFloat32(0f),
        _ => throw new ArgumentOutOfRangeException(nameof(dataType))
    };

    public static TagValue One(TagDataType dataType) => dataType switch
    {
        TagDataType.Bool => FromBool(true),
        TagDataType.Int32 => FromInt32(1),
        TagDataType.Float32 => FromFloat32(1f),
        _ => throw new ArgumentOutOfRangeException(nameof(dataType))
    };

    public bool AsBool()
    {
        EnsureType(TagDataType.Bool);
        return _storage != 0;
    }

    public int AsInt32()
    {
        EnsureType(TagDataType.Int32);
        return _storage;
    }

    public float AsFloat32()
    {
        EnsureType(TagDataType.Float32);
        return BitConverter.Int32BitsToSingle(_storage);
    }

    public object ToObject() => DataType switch
    {
        TagDataType.Bool => AsBool(),
        TagDataType.Int32 => AsInt32(),
        TagDataType.Float32 => AsFloat32(),
        _ => throw new InvalidOperationException($"Unsupported tag data type: {DataType}.")
    };

    public bool Equals(TagValue other) =>
        DataType == other.DataType && _storage == other._storage;

    public override bool Equals(object? obj) =>
        obj is TagValue other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(DataType, _storage);

    public override string ToString() => DataType switch
    {
        TagDataType.Bool => AsBool().ToString(CultureInfo.InvariantCulture),
        TagDataType.Int32 => AsInt32().ToString(CultureInfo.InvariantCulture),
        TagDataType.Float32 => AsFloat32().ToString("R", CultureInfo.InvariantCulture),
        _ => string.Empty
    };

    public static bool operator ==(TagValue left, TagValue right) => left.Equals(right);

    public static bool operator !=(TagValue left, TagValue right) => !left.Equals(right);

    private void EnsureType(TagDataType expected)
    {
        if (DataType != expected)
        {
            throw new InvalidOperationException(
                $"Tag value contains {DataType}, not {expected}.");
        }
    }
}
