namespace SimuladorIndustrial.Core.Tags;

public enum TagDataType
{
    Bool,
    Int32,
    Float32
}

public enum TagDirection
{
    PlcInput,
    PlcOutput
}

public enum TagQuality
{
    Good,
    Stale,
    Bad,
    Disconnected
}

public enum TagFault
{
    None,
    AlwaysOff,
    AlwaysOn,
    Stuck,
    Noise
}
