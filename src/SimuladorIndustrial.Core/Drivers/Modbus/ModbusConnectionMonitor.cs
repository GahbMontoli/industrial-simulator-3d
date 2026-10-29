namespace SimuladorIndustrial.Core.Drivers.Modbus;

public enum ModbusConnectionRole
{
    Client,
    Server
}

public enum ModbusConnectionState
{
    Disconnected,
    Connecting,
    Listening,
    Connected,
    Reconnecting,
    Faulted
}

public sealed record ModbusConnectionSnapshot(
    ModbusConnectionRole Role,
    ModbusConnectionState State,
    string Endpoint,
    byte UnitId,
    int ActiveConnections,
    long TotalRequests,
    long FailedRequests,
    string? LastError)
{
    public bool IsAvailable => State is ModbusConnectionState.Listening or ModbusConnectionState.Connected;
}
