using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using SimuladorIndustrial.Core.Diagnostics;
using SimuladorIndustrial.Core.Tags;

namespace SimuladorIndustrial.Core.Drivers.Modbus;

public sealed class ModbusTcpServer : IAsyncDisposable
{
    private static readonly TimeSpan DefaultIoTimeout = TimeSpan.FromSeconds(10);

    private readonly object _sync = new();
    private readonly IPAddress _listenAddress;
    private readonly int _configuredPort;
    private readonly TagRegistry _registry;
    private readonly EventJournal _journal;
    private readonly ModbusTagDataStore _dataStore;
    private readonly TimeSpan _ioTimeout;
    private readonly HashSet<TcpClient> _clients = [];
    private readonly HashSet<Task> _clientTasks = [];

    private TcpListener? _listener;
    private CancellationTokenSource? _lifetimeCancellation;
    private Task? _acceptTask;
    private long _eventTick;
    private long _totalRequests;
    private long _failedRequests;
    private int _activeConnections;
    private int _monitorState = (int)ModbusConnectionState.Disconnected;
    private string? _lastError;
    private bool _disposed;

    public ModbusTcpServer(
        TagRegistry registry,
        EventJournal journal,
        IPAddress? listenAddress = null,
        int port = 502,
        byte unitId = 1,
        TimeSpan? ioTimeout = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        if (port is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        _listenAddress = listenAddress ?? IPAddress.Loopback;
        _configuredPort = port;
        UnitId = unitId;
        _ioTimeout = ioTimeout ?? DefaultIoTimeout;
        if (_ioTimeout <= TimeSpan.Zero && _ioTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(ioTimeout));
        }

        _dataStore = new ModbusTagDataStore(registry, journal);
    }

    public byte UnitId { get; }

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _listener is not null;
            }
        }
    }

    public int BoundPort
    {
        get
        {
            lock (_sync)
            {
                return _listener?.LocalEndpoint is IPEndPoint endpoint
                    ? endpoint.Port
                    : 0;
            }
        }
    }

    public ModbusConnectionSnapshot Monitor => new(
        ModbusConnectionRole.Server,
        (ModbusConnectionState)Volatile.Read(ref _monitorState),
        $"{_listenAddress}:{GetMonitorPort()}",
        UnitId,
        Volatile.Read(ref _activeConnections),
        Interlocked.Read(ref _totalRequests),
        Interlocked.Read(ref _failedRequests),
        Volatile.Read(ref _lastError));

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            ThrowIfDisposed();
            if (_listener is not null)
            {
                throw new InvalidOperationException("The Modbus TCP server is already running.");
            }

            var listener = new TcpListener(_listenAddress, _configuredPort);
            listener.Start();
            var lifetimeCancellation = new CancellationTokenSource();
            _listener = listener;
            _lifetimeCancellation = lifetimeCancellation;
            _acceptTask = AcceptLoopAsync(listener, lifetimeCancellation.Token);

            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            Volatile.Write(ref _lastError, null);
            SetMonitorState(ModbusConnectionState.Listening);
            Record("modbus.server.connected", $"Modbus TCP server listening on {endpoint} with unit id {UnitId}.");
        }

        SetPlcOutputQuality(TagQuality.Disconnected);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        TcpListener? listener;
        CancellationTokenSource? lifetimeCancellation;
        Task? acceptTask;

        lock (_sync)
        {
            listener = _listener;
            lifetimeCancellation = _lifetimeCancellation;
            acceptTask = _acceptTask;
            if (listener is null)
            {
                return;
            }

            _listener = null;
            _lifetimeCancellation = null;
            _acceptTask = null;
        }

        SetMonitorState(ModbusConnectionState.Disconnected);
        SetPlcOutputQuality(TagQuality.Disconnected);

        lifetimeCancellation!.Cancel();
        listener.Stop();

        if (acceptTask is not null)
        {
            await IgnoreExpectedShutdownAsync(acceptTask).ConfigureAwait(false);
        }

        TcpClient[] clients;
        Task[] clientTasks;
        lock (_sync)
        {
            clients = _clients.ToArray();
            clientTasks = _clientTasks.ToArray();
        }

        foreach (var client in clients)
        {
            client.Dispose();
        }

        if (clientTasks.Length > 0)
        {
            await Task.WhenAll(clientTasks).WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        lifetimeCancellation.Dispose();
        Record("modbus.server.disconnected", "Modbus TCP server stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _disposed = true;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                RecordError("modbus.server.error", "The Modbus TCP accept loop failed.", exception);
                return;
            }

            client.NoDelay = true;
            Task clientTask;
            var becameFirstClient = false;
            lock (_sync)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    client.Dispose();
                    return;
                }

                _clients.Add(client);
                becameFirstClient = Interlocked.Increment(ref _activeConnections) == 1;
                SetMonitorState(ModbusConnectionState.Connected);
                clientTask = HandleClientAsync(client, cancellationToken);
                _clientTasks.Add(clientTask);
            }

            if (becameFirstClient)
            {
                SetPlcOutputQuality(TagQuality.Good);
            }

            _ = ObserveClientAsync(client, clientTask);
        }
    }

    private async Task ObserveClientAsync(TcpClient client, Task clientTask)
    {
        try
        {
            await clientTask.ConfigureAwait(false);
        }
        finally
        {
            client.Dispose();
            lock (_sync)
            {
                _clients.Remove(client);
                _clientTasks.Remove(clientTask);
            }


            var remainingClients = Interlocked.Decrement(ref _activeConnections);
            if (remainingClients == 0)
            {
                SetMonitorState(IsRunning
                    ? ModbusConnectionState.Listening
                    : ModbusConnectionState.Disconnected);
                SetPlcOutputQuality(TagQuality.Disconnected);
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken serverCancellationToken)
    {
        var remoteEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown endpoint";
        Record("modbus.server.client-connected", $"Modbus client connected from {remoteEndpoint}.");

        try
        {
            using var stream = client.GetStream();
            while (!serverCancellationToken.IsCancellationRequested)
            {
                ModbusApplicationDataUnit? request;
                try
                {
                    request = await ReadWithTimeoutAsync(stream, serverCancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException exception)
                {
                    RecordError("modbus.server.timeout", $"Timed out waiting for {remoteEndpoint}.", exception);
                    return;
                }

                if (request is null)
                {
                    return;
                }

                var responsePdu = ProcessRequest(request);
                var response = new ModbusApplicationDataUnit(
                    request.TransactionId,
                    request.UnitId,
                    responsePdu);
                await WriteWithTimeoutAsync(stream, response, serverCancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (serverCancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException exception) when (serverCancellationToken.IsCancellationRequested)
        {
            _ = exception;
        }
        catch (ObjectDisposedException) when (serverCancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            RecordError("modbus.server.error", $"Modbus client {remoteEndpoint} failed.", exception);
        }
        finally
        {
            Record("modbus.server.client-disconnected", $"Modbus client {remoteEndpoint} disconnected.");
        }
    }

    private byte[] ProcessRequest(ModbusApplicationDataUnit request)
    {
        Interlocked.Increment(ref _totalRequests);
        var functionCode = request.ProtocolDataUnit[0];
        if (request.UnitId != UnitId)
        {
            RecordRequestFailure($"Rejected unit id {request.UnitId}; this server uses unit id {UnitId}.");
            Record(
                "modbus.server.request-error",
                $"Rejected unit id {request.UnitId}; this server uses unit id {UnitId}.");
            return CreateExceptionResponse(
                functionCode,
                ModbusExceptionCode.GatewayTargetDeviceFailedToRespond);
        }

        try
        {
            return ProcessProtocolDataUnit(request.ProtocolDataUnit);
        }
        catch (ModbusRequestException exception)
        {
            RecordRequestFailure(exception.Message);
            Record(
                "modbus.server.request-error",
                $"Function 0x{exception.FunctionCode:X2}: {exception.Message}");
            return CreateExceptionResponse(exception.FunctionCode, exception.ExceptionCode);
        }
        catch (Exception exception)
        {
            RecordRequestFailure(exception.Message);
            RecordError(
                "modbus.server.error",
                $"Function 0x{functionCode:X2} caused an internal server error.",
                exception);
            return CreateExceptionResponse(functionCode, ModbusExceptionCode.ServerDeviceFailure);
        }
    }

    private byte[] ProcessProtocolDataUnit(byte[] pdu)
    {
        var functionCode = pdu[0];
        return functionCode switch
        {
            (byte)ModbusFunctionCode.ReadCoils => ProcessReadBits(pdu, false),
            (byte)ModbusFunctionCode.ReadDiscreteInputs => ProcessReadBits(pdu, true),
            (byte)ModbusFunctionCode.ReadHoldingRegisters => ProcessReadRegisters(pdu, false),
            (byte)ModbusFunctionCode.ReadInputRegisters => ProcessReadRegisters(pdu, true),
            (byte)ModbusFunctionCode.WriteSingleCoil => ProcessWriteSingleCoil(pdu),
            (byte)ModbusFunctionCode.WriteSingleRegister => ProcessWriteSingleRegister(pdu),
            (byte)ModbusFunctionCode.WriteMultipleCoils => ProcessWriteMultipleCoils(pdu),
            (byte)ModbusFunctionCode.WriteMultipleRegisters => ProcessWriteMultipleRegisters(pdu),
            _ => throw new ModbusRequestException(
                functionCode,
                ModbusExceptionCode.IllegalFunction,
                $"Function 0x{functionCode:X2} is not supported.")
        };
    }

    private byte[] ProcessReadBits(byte[] pdu, bool discreteInputs)
    {
        RequireExactLength(pdu, 5);
        var functionCode = pdu[0];
        var startAddress = ReadUInt16(pdu, 1);
        var quantity = ReadUInt16(pdu, 3);
        ValidateQuantity(functionCode, quantity, ModbusLimits.MaximumReadBits);
        ValidateAddressRange(functionCode, startAddress, quantity);

        var values = discreteInputs
            ? _dataStore.ReadDiscreteInputs(startAddress, quantity)
            : _dataStore.ReadCoils(startAddress, quantity);
        var packed = ModbusCodec.PackBits(values);
        var response = new byte[2 + packed.Length];
        response[0] = functionCode;
        response[1] = checked((byte)packed.Length);
        packed.CopyTo(response, 2);
        return response;
    }

    private byte[] ProcessReadRegisters(byte[] pdu, bool inputRegisters)
    {
        RequireExactLength(pdu, 5);
        var functionCode = pdu[0];
        var startAddress = ReadUInt16(pdu, 1);
        var quantity = ReadUInt16(pdu, 3);
        ValidateQuantity(functionCode, quantity, ModbusLimits.MaximumReadRegisters);
        ValidateAddressRange(functionCode, startAddress, quantity);

        var values = inputRegisters
            ? _dataStore.ReadInputRegisters(startAddress, quantity)
            : _dataStore.ReadHoldingRegisters(startAddress, quantity);
        var payload = ModbusCodec.EncodeRegisters(values);
        var response = new byte[2 + payload.Length];
        response[0] = functionCode;
        response[1] = checked((byte)payload.Length);
        payload.CopyTo(response, 2);
        return response;
    }

    private byte[] ProcessWriteSingleCoil(byte[] pdu)
    {
        RequireExactLength(pdu, 5);
        var address = ReadUInt16(pdu, 1);
        var encodedValue = ReadUInt16(pdu, 3);
        var value = encodedValue switch
        {
            0xFF00 => true,
            0x0000 => false,
            _ => throw new ModbusRequestException(
                pdu[0],
                ModbusExceptionCode.IllegalDataValue,
                $"0x{encodedValue:X4} is not a valid single-coil value.")
        };

        try
        {
            _dataStore.WriteCoils(address, [value], NextTick());
        }
        catch (ModbusRequestException exception)
        {
            throw Recode(exception, pdu[0]);
        }

        return pdu.ToArray();
    }

    private byte[] ProcessWriteSingleRegister(byte[] pdu)
    {
        RequireExactLength(pdu, 5);
        var address = ReadUInt16(pdu, 1);
        var value = ReadUInt16(pdu, 3);
        try
        {
            _dataStore.WriteHoldingRegisters(address, [value], NextTick());
        }
        catch (ModbusRequestException exception)
        {
            throw Recode(exception, pdu[0]);
        }

        return pdu.ToArray();
    }

    private byte[] ProcessWriteMultipleCoils(byte[] pdu)
    {
        RequireMinimumLength(pdu, 6);
        var functionCode = pdu[0];
        var startAddress = ReadUInt16(pdu, 1);
        var quantity = ReadUInt16(pdu, 3);
        ValidateQuantity(functionCode, quantity, ModbusLimits.MaximumWriteCoils);
        ValidateAddressRange(functionCode, startAddress, quantity);
        var expectedByteCount = (quantity + 7) / 8;
        if (pdu[5] != expectedByteCount || pdu.Length != 6 + expectedByteCount)
        {
            throw IllegalValue(functionCode, "The multiple-coil byte count does not match the quantity.");
        }

        bool[] values;
        try
        {
            values = ModbusCodec.UnpackBits(pdu.AsSpan(6), quantity);
        }
        catch (ModbusProtocolException exception)
        {
            throw new ModbusRequestException(
                functionCode,
                ModbusExceptionCode.IllegalDataValue,
                exception.Message,
                exception);
        }

        try
        {
            _dataStore.WriteCoils(startAddress, values, NextTick());
        }
        catch (ModbusRequestException exception)
        {
            throw Recode(exception, functionCode);
        }

        return CreateWriteMultipleResponse(functionCode, startAddress, quantity);
    }

    private byte[] ProcessWriteMultipleRegisters(byte[] pdu)
    {
        RequireMinimumLength(pdu, 6);
        var functionCode = pdu[0];
        var startAddress = ReadUInt16(pdu, 1);
        var quantity = ReadUInt16(pdu, 3);
        ValidateQuantity(functionCode, quantity, ModbusLimits.MaximumWriteRegisters);
        ValidateAddressRange(functionCode, startAddress, quantity);
        var expectedByteCount = quantity * 2;
        if (pdu[5] != expectedByteCount || pdu.Length != 6 + expectedByteCount)
        {
            throw IllegalValue(functionCode, "The multiple-register byte count does not match the quantity.");
        }

        var values = ModbusCodec.DecodeRegisters(pdu.AsSpan(6));
        try
        {
            _dataStore.WriteHoldingRegisters(startAddress, values, NextTick());
        }
        catch (ModbusRequestException exception)
        {
            throw Recode(exception, functionCode);
        }

        return CreateWriteMultipleResponse(functionCode, startAddress, quantity);
    }

    private async ValueTask<ModbusApplicationDataUnit?> ReadWithTimeoutAsync(
        Stream stream,
        CancellationToken serverCancellationToken)
    {
        using var timeoutCancellation = CreateTimeoutCancellation(serverCancellationToken);
        try
        {
            return await ModbusTcpTransport.ReadAsync(
                stream,
                timeoutCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!serverCancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out while reading a Modbus TCP frame.");
        }
    }

    private async ValueTask WriteWithTimeoutAsync(
        Stream stream,
        ModbusApplicationDataUnit response,
        CancellationToken serverCancellationToken)
    {
        using var timeoutCancellation = CreateTimeoutCancellation(serverCancellationToken);
        try
        {
            await ModbusTcpTransport.WriteAsync(
                stream,
                response,
                timeoutCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!serverCancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out while writing a Modbus TCP frame.");
        }
    }

    private CancellationTokenSource CreateTimeoutCancellation(CancellationToken cancellationToken)
    {
        var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_ioTimeout != Timeout.InfiniteTimeSpan)
        {
            timeoutCancellation.CancelAfter(_ioTimeout);
        }

        return timeoutCancellation;
    }

    private static void RequireExactLength(byte[] pdu, int expectedLength)
    {
        if (pdu.Length != expectedLength)
        {
            throw IllegalValue(pdu[0], $"Function 0x{pdu[0]:X2} requires exactly {expectedLength} PDU bytes.");
        }
    }

    private static void RequireMinimumLength(byte[] pdu, int minimumLength)
    {
        if (pdu.Length < minimumLength)
        {
            throw IllegalValue(pdu[0], $"Function 0x{pdu[0]:X2} requires at least {minimumLength} PDU bytes.");
        }
    }

    private static void ValidateQuantity(byte functionCode, ushort quantity, int maximum)
    {
        if (quantity == 0 || quantity > maximum)
        {
            throw IllegalValue(
                functionCode,
                $"Quantity {quantity} is outside the supported range 1..{maximum}.");
        }
    }

    private static void ValidateAddressRange(byte functionCode, ushort startAddress, ushort quantity)
    {
        if (startAddress + quantity - 1 > ushort.MaxValue)
        {
            throw new ModbusRequestException(
                functionCode,
                ModbusExceptionCode.IllegalDataAddress,
                "The requested address range exceeds address 65535.");
        }
    }

    private static ModbusRequestException IllegalValue(byte functionCode, string message) =>
        new(functionCode, ModbusExceptionCode.IllegalDataValue, message);

    private static ModbusRequestException Recode(ModbusRequestException exception, byte functionCode) =>
        new(functionCode, exception.ExceptionCode, exception.Message, exception);

    private static byte[] CreateExceptionResponse(byte functionCode, ModbusExceptionCode exceptionCode) =>
        [(byte)(functionCode | 0x80), (byte)exceptionCode];

    private static byte[] CreateWriteMultipleResponse(
        byte functionCode,
        ushort startAddress,
        ushort quantity)
    {
        var response = new byte[5];
        response[0] = functionCode;
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(1, 2), startAddress);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(3, 2), quantity);
        return response;
    }

    private static ushort ReadUInt16(byte[] pdu, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(offset, 2));

    private static async Task IgnoreExpectedShutdownAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException)
        {
        }
    }

    private void Record(string category, string message) =>
        _journal.Record(NextTick(), category, message);

    private void RecordError(string category, string message, Exception exception) =>
        RecordConnectionError(category, $"{message} {exception.GetType().Name}: {exception.Message}");

    private void RecordConnectionError(string category, string message)
    {
        Volatile.Write(ref _lastError, message);
        Record(category, message);
    }

    private void RecordRequestFailure(string message)
    {
        Interlocked.Increment(ref _failedRequests);
        Volatile.Write(ref _lastError, message);
    }

    private void SetPlcOutputQuality(TagQuality quality)
    {
        foreach (var tag in _registry.GetAll().Where(tag =>
                     tag.Definition.Direction == TagDirection.PlcOutput &&
                     tag.Quality != quality))
        {
            _registry.SetQuality(tag.Definition.Id, quality, NextTick());
        }
    }

    private int GetMonitorPort()
    {
        lock (_sync)
        {
            return _listener?.LocalEndpoint is IPEndPoint endpoint
                ? endpoint.Port
                : _configuredPort;
        }
    }

    private void SetMonitorState(ModbusConnectionState state) =>
        Volatile.Write(ref _monitorState, (int)state);

    private long NextTick() => Interlocked.Increment(ref _eventTick);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
