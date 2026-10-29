namespace SimuladorIndustrial.Core.Tags;

public sealed record TagDefinition
{
    public TagDefinition(
        Guid id,
        string name,
        string canonicalName,
        TagDataType dataType,
        TagDirection direction,
        int address,
        string source)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Tag id cannot be empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalName);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        if (!Enum.IsDefined(dataType))
        {
            throw new ArgumentOutOfRangeException(nameof(dataType));
        }

        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        var maximumStartAddress = dataType == TagDataType.Bool
            ? ushort.MaxValue
            : ushort.MaxValue - 1;

        if (address < 0 || address > maximumStartAddress)
        {
            throw new ArgumentOutOfRangeException(
                nameof(address),
                $"Tag address must be between 0 and {maximumStartAddress} for {dataType}.");
        }

        Id = id;
        Name = name.Trim();
        CanonicalName = canonicalName.Trim();
        DataType = dataType;
        Direction = direction;
        Address = address;
        Source = source.Trim();
    }

    public Guid Id { get; }

    public string Name { get; }

    public string CanonicalName { get; }

    public TagDataType DataType { get; }

    public TagDirection Direction { get; }

    public int Address { get; }

    public string Source { get; }
}
