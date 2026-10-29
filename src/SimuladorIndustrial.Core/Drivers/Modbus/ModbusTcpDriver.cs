using System.Net;
using SimuladorIndustrial.Core.Diagnostics;
using SimuladorIndustrial.Core.Persistence;
using SimuladorIndustrial.Core.Tags;

namespace SimuladorIndustrial.Core.Drivers.Modbus;

public sealed class ModbusTcpDriver : IAsyncDisposable
{
    private readonly ModbusTcpClient? _client;
    private readonly ModbusTcpServer? _server;
    private readonly ModbusTcpClientSynchronizer? _synchronizer;
    private bool _disposed;

    private ModbusTcpDriver(
        ModbusTcpClient client,
        ModbusTcpClientSynchronizer synchronizer)
    {
        _client = client;
        _synchronizer = synchronizer;
        Role = ModbusConnectionRole.Client;
    }

    private ModbusTcpDriver(ModbusTcpServer server)
    {
        _server = server;
        Role = ModbusConnectionRole.Server;
    }

    public ModbusConnectionRole Role { get; }

    public ModbusTcpClient? Client => _client;

    public ModbusTcpServer? Server => _server;

    public ModbusTcpClientSynchronizer? Synchronizer => _synchronizer;

    public ModbusConnectionSnapshot Monitor => _client?.Monitor ?? _server!.Monitor;

    public static ModbusTcpDriver FromConnection(
        ConnectionDocument connection,
        TagRegistry registry,
        EventJournal journal,
        TimeSpan? ioTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(journal);

        if (!string.Equals(connection.Driver, "modbusTcp", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Connection driver '{connection.Driver}' is not Modbus TCP.",
                nameof(connection));
        }

        if (connection.Port is < 1 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(connection), "Port must be between 1 and 65535.");
        }

        if (connection.UnitId is < byte.MinValue or > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(connection), "Unit id must be between 0 and 255.");
        }

        var unitId = checked((byte)connection.UnitId);
        if (string.Equals(connection.Role, "client", StringComparison.OrdinalIgnoreCase))
        {
            var client = new ModbusTcpClient(
                connection.Host,
                connection.Port,
                journal,
                unitId,
                ioTimeout);
            return new ModbusTcpDriver(
                client,
                new ModbusTcpClientSynchronizer(client, registry, journal));
        }

        if (string.Equals(connection.Role, "server", StringComparison.OrdinalIgnoreCase))
        {
            return new ModbusTcpDriver(new ModbusTcpServer(
                registry,
                journal,
                ParseListenAddress(connection.Host),
                connection.Port,
                unitId,
                ioTimeout));
        }

        throw new ArgumentException(
            $"Connection role '{connection.Role}' must be 'client' or 'server'.",
            nameof(connection));
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return Role == ModbusConnectionRole.Client
            ? _client!.ConnectAsync(cancellationToken)
            : _server!.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return Role == ModbusConnectionRole.Client
            ? _client!.DisconnectAsync(cancellationToken)
            : _server!.StopAsync(cancellationToken);
    }

    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (Role == ModbusConnectionRole.Client)
        {
            await _client!.ReconnectAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await _server!.StopAsync(cancellationToken).ConfigureAwait(false);
        await _server.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<ModbusSynchronizationResult> SynchronizeOnceAsync(
        long tick,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_synchronizer is null)
        {
            throw new InvalidOperationException(
                "Synchronization cycles are available only for a Modbus TCP client connection.");
        }

        return _synchronizer.SynchronizeOnceAsync(tick, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            await _server!.DisposeAsync().ConfigureAwait(false);
        }

        _disposed = true;
    }

    private static IPAddress ParseListenAddress(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.Loopback;
        }

        if (IPAddress.TryParse(host, out var address))
        {
            return address;
        }

        throw new ArgumentException(
            "A Modbus TCP server host must be 'localhost' or a numeric IP address.",
            nameof(host));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
