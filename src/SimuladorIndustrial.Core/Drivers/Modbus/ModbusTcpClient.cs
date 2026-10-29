using System.Buffers.Binary;
using System.Net.Sockets;
using SimuladorIndustrial.Core.Diagnostics;

namespace SimuladorIndustrial.Core.Drivers.Modbus;

public sealed class ModbusTcpClient : IAsyncDisposable
{
    private static readonly TimeSpan DefaultIoTimeout = TimeSpan.FromSeconds(5);

    private readonly string _host;
    private readonly int _port;
    private readonly EventJournal _journal;
    private readonly TimeSpan _ioTimeout;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    private TcpClient? _client;
    private NetworkStream? _stream;
    private ushort _nextTransactionId;
    private long _eventTick;
    private long _totalRequests;
    private long _failedRequests;
    private int _monitorState = (int)ModbusConnectionState.Disconnected;
    private string? _lastError;
    private bool _disposed;

    public ModbusTcpClient(
        string host,
        int port,
        EventJournal journal,
        byte unitId = 1,
        TimeSpan? ioTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (port is < 1 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        _host = host.Trim();
        _port = port;
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        UnitId = unitId;
        _ioTimeout = ioTimeout ?? DefaultIoTimeout;
        if (_ioTimeout <= TimeSpan.Zero && _ioTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(ioTimeout));
        }
    }

    public byte UnitId { get; }

    public bool IsConnected => _client is not null;

    public ModbusConnectionSnapshot Monitor => new(
        ModbusConnectionRole.Client,
        (ModbusConnectionState)Volatile.Read(ref _monitorState),
        $"{_host}:{_port}",
        UnitId,
        IsConnected ? 1 : 0,
        Interlocked.Read(ref _totalRequests),
        Interlocked.Read(ref _failedRequests),
        Volatile.Read(ref _lastError));

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_client is not null)
            {
                throw new InvalidOperationException("The Modbus TCP client is already connected.");
            }

            SetMonitorState(ModbusConnectionState.Connecting);
            await ConnectCoreAsync("modbus.client.connected", cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            DisconnectCore("modbus.client.disconnected", "Disconnected before explicit reconnect.");
            SetMonitorState(ModbusConnectionState.Reconnecting);
            await ConnectCoreAsync("modbus.client.reconnected", cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DisconnectCore("modbus.client.disconnected", "Modbus TCP client disconnected explicitly.");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task<bool[]> ReadCoilsAsync(
        ushort startAddress,
        ushort quantity,
        CancellationToken cancellationToken = default) =>
        ReadBitsAsync(
            ModbusFunctionCode.ReadCoils,
            startAddress,
            quantity,
            cancellationToken);

    public Task<bool[]> ReadDiscreteInputsAsync(
        ushort startAddress,
        ushort quantity,
        CancellationToken cancellationToken = default) =>
        ReadBitsAsync(
            ModbusFunctionCode.ReadDiscreteInputs,
            startAddress,
            quantity,
            cancellationToken);

    public Task<ushort[]> ReadHoldingRegistersAsync(
        ushort startAddress,
        ushort quantity,
        CancellationToken cancellationToken = default) =>
        ReadRegistersAsync(
            ModbusFunctionCode.ReadHoldingRegisters,
            startAddress,
            quantity,
            cancellationToken);

    public Task<ushort[]> ReadInputRegistersAsync(
        ushort startAddress,
        ushort quantity,
        CancellationToken cancellationToken = default) =>
        ReadRegistersAsync(
            ModbusFunctionCode.ReadInputRegisters,
            startAddress,
            quantity,
            cancellationToken);

    public async Task WriteSingleCoilAsync(
        ushort address,
        bool value,
        CancellationToken cancellationToken = default)
    {
        var request = CreateAddressValueRequest(
            ModbusFunctionCode.WriteSingleCoil,
            address,
            value ? (ushort)0xFF00 : (ushort)0x0000);
        var response = await SendRawRequestAsync(request, cancellationToken).ConfigureAwait(false);
        RequireEcho(request, response);
    }

    public async Task WriteSingleRegisterAsync(
        ushort address,
        ushort value,
        CancellationToken cancellationToken = default)
    {
        var request = CreateAddressValueRequest(ModbusFunctionCode.WriteSingleRegister, address, value);
        var response = await SendRawRequestAsync(request, cancellationToken).ConfigureAwait(false);
        RequireEcho(request, response);
    }

    public async Task WriteMultipleCoilsAsync(
        ushort startAddress,
        IReadOnlyList<bool> values,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        ValidateQuantityAndRange(
            startAddress,
            values.Count,
            ModbusLimits.MaximumWriteCoils,
            nameof(values));

        var packed = ModbusCodec.PackBits(values);
        var request = new byte[6 + packed.Length];
        request[0] = (byte)ModbusFunctionCode.WriteMultipleCoils;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(1, 2), startAddress);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(3, 2), checked((ushort)values.Count));
        request[5] = checked((byte)packed.Length);
        packed.CopyTo(request, 6);

        var response = await SendRawRequestAsync(request, cancellationToken).ConfigureAwait(false);
        RequireWriteMultipleResponse(request, response);
    }

    public async Task WriteMultipleRegistersAsync(
        ushort startAddress,
        IReadOnlyList<ushort> values,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        ValidateQuantityAndRange(
            startAddress,
            values.Count,
            ModbusLimits.MaximumWriteRegisters,
            nameof(values));

        var payload = ModbusCodec.EncodeRegisters(values);
        var request = new byte[6 + payload.Length];
        request[0] = (byte)ModbusFunctionCode.WriteMultipleRegisters;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(1, 2), startAddress);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(3, 2), checked((ushort)values.Count));
        request[5] = checked((byte)payload.Length);
        payload.CopyTo(request, 6);

        var response = await SendRawRequestAsync(request, cancellationToken).ConfigureAwait(false);
        RequireWriteMultipleResponse(request, response);
    }

    public async Task<byte[]> SendRawRequestAsync(
        ReadOnlyMemory<byte> protocolDataUnit,
        CancellationToken cancellationToken = default)
    {
        var request = protocolDataUnit.ToArray();
        if (request.Length is < 1 or > ModbusLimits.MaximumPduLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(protocolDataUnit),
                $"A Modbus PDU must contain between 1 and {ModbusLimits.MaximumPduLength} bytes.");
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_client is null || _stream is null)
            {
                throw new InvalidOperationException("Connect the Modbus TCP client before sending requests.");
            }

            return await ExecuteRequestCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            DisconnectCore("modbus.client.disconnected", "Modbus TCP client disposed.");
            _disposed = true;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task ConnectCoreAsync(string category, CancellationToken cancellationToken)
    {
        var client = new TcpClient { NoDelay = true };
        using var timeoutCancellation = CreateTimeoutCancellation(cancellationToken);
        try
        {
            await client.ConnectAsync(
                _host,
                _port,
                timeoutCancellation.Token).ConfigureAwait(false);
            _client = client;
            _stream = client.GetStream();
            Volatile.Write(ref _lastError, null);
            SetMonitorState(ModbusConnectionState.Connected);
            Record(category, $"Connected to Modbus TCP endpoint {_host}:{_port} with unit id {UnitId}.");
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            SetConnectionFault(exception);
            RecordError("modbus.client.timeout", "Timed out while connecting.", exception);
            throw new TimeoutException($"Timed out connecting to {_host}:{_port}.", exception);
        }
        catch (Exception exception)
        {
            client.Dispose();
            SetConnectionFault(exception);
            RecordError("modbus.client.error", "Failed to connect.", exception);
            throw;
        }
    }

    private async Task<byte[]> ExecuteRequestCoreAsync(
        byte[] requestPdu,
        CancellationToken cancellationToken)
    {
        var transactionId = _nextTransactionId++;
        var request = new ModbusApplicationDataUnit(transactionId, UnitId, requestPdu);
        using var timeoutCancellation = CreateTimeoutCancellation(cancellationToken);
        Interlocked.Increment(ref _totalRequests);

        try
        {
            await ModbusTcpTransport.WriteAsync(
                _stream!,
                request,
                timeoutCancellation.Token).ConfigureAwait(false);
            var response = await ModbusTcpTransport.ReadAsync(
                _stream!,
                timeoutCancellation.Token).ConfigureAwait(false);
            if (response is null)
            {
                throw new IOException("The Modbus TCP server closed the connection without a response.");
            }

            ValidateResponseEnvelope(request, response);
            return response.ProtocolDataUnit;
        }
        catch (ModbusServerException exception)
        {
            Interlocked.Increment(ref _failedRequests);
            Volatile.Write(ref _lastError, exception.Message);
            RecordError("modbus.client.request-error", "The server rejected a Modbus request.", exception);
            throw;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            MarkConnectionFailed("modbus.client.timeout", "Timed out during a Modbus request.", exception);
            throw new TimeoutException("Timed out during the Modbus TCP request.", exception);
        }
        catch (OperationCanceledException exception)
        {
            MarkConnectionFailed("modbus.client.error", "The Modbus request was cancelled.", exception);
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or SocketException or ModbusProtocolException)
        {
            MarkConnectionFailed("modbus.client.error", "The Modbus connection failed.", exception);
            throw;
        }
    }

    private async Task<bool[]> ReadBitsAsync(
        ModbusFunctionCode functionCode,
        ushort startAddress,
        ushort quantity,
        CancellationToken cancellationToken)
    {
        ValidateQuantityAndRange(
            startAddress,
            quantity,
            ModbusLimits.MaximumReadBits,
            nameof(quantity));
        var request = CreateReadRequest(functionCode, startAddress, quantity);
        var response = await SendRawRequestAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.Length < 2)
        {
            throw new ModbusProtocolException("A read-bits response is too short.");
        }

        var expectedByteCount = (quantity + 7) / 8;
        if (response[1] != expectedByteCount || response.Length != expectedByteCount + 2)
        {
            throw new ModbusProtocolException("The read-bits response byte count is inconsistent.");
        }

        return ModbusCodec.UnpackBits(response.AsSpan(2), quantity);
    }

    private async Task<ushort[]> ReadRegistersAsync(
        ModbusFunctionCode functionCode,
        ushort startAddress,
        ushort quantity,
        CancellationToken cancellationToken)
    {
        ValidateQuantityAndRange(
            startAddress,
            quantity,
            ModbusLimits.MaximumReadRegisters,
            nameof(quantity));
        var request = CreateReadRequest(functionCode, startAddress, quantity);
        var response = await SendRawRequestAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.Length < 2)
        {
            throw new ModbusProtocolException("A read-registers response is too short.");
        }

        var expectedByteCount = quantity * 2;
        if (response[1] != expectedByteCount || response.Length != expectedByteCount + 2)
        {
            throw new ModbusProtocolException("The read-registers response byte count is inconsistent.");
        }

        return ModbusCodec.DecodeRegisters(response.AsSpan(2));
    }

    private void ValidateResponseEnvelope(
        ModbusApplicationDataUnit request,
        ModbusApplicationDataUnit response)
    {
        if (response.TransactionId != request.TransactionId)
        {
            throw new ModbusProtocolException(
                $"Response transaction id {response.TransactionId} does not match request {request.TransactionId}.");
        }

        if (response.UnitId != UnitId)
        {
            throw new ModbusProtocolException(
                $"Response unit id {response.UnitId} does not match configured unit id {UnitId}.");
        }

        var requestFunction = request.ProtocolDataUnit[0];
        var responsePdu = response.ProtocolDataUnit;
        if (responsePdu[0] == (byte)(requestFunction | 0x80))
        {
            if (responsePdu.Length != 2)
            {
                throw new ModbusProtocolException("A Modbus exception response must contain exactly two PDU bytes.");
            }

            throw new ModbusServerException(requestFunction, (ModbusExceptionCode)responsePdu[1]);
        }

        if (responsePdu[0] != requestFunction)
        {
            throw new ModbusProtocolException(
                $"Response function 0x{responsePdu[0]:X2} does not match request 0x{requestFunction:X2}.");
        }
    }

    private void MarkConnectionFailed(string category, string message, Exception exception)
    {
        Interlocked.Increment(ref _failedRequests);
        SetConnectionFault(exception);
        RecordError(category, message, exception);
        _stream?.Dispose();
        _client?.Dispose();
        _stream = null;
        _client = null;
    }

    private void DisconnectCore(string category, string message)
    {
        var wasConnected = _client is not null;
        _stream?.Dispose();
        _client?.Dispose();
        _stream = null;
        _client = null;
        SetMonitorState(ModbusConnectionState.Disconnected);
        if (wasConnected)
        {
            Record(category, message);
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

    private static byte[] CreateReadRequest(
        ModbusFunctionCode functionCode,
        ushort startAddress,
        ushort quantity)
    {
        var request = new byte[5];
        request[0] = (byte)functionCode;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(1, 2), startAddress);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(3, 2), quantity);
        return request;
    }

    private static byte[] CreateAddressValueRequest(
        ModbusFunctionCode functionCode,
        ushort address,
        ushort value)
    {
        var request = new byte[5];
        request[0] = (byte)functionCode;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(1, 2), address);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(3, 2), value);
        return request;
    }

    private static void RequireEcho(byte[] request, byte[] response)
    {
        if (!request.AsSpan().SequenceEqual(response))
        {
            throw new ModbusProtocolException("The write response does not echo the request.");
        }
    }

    private static void RequireWriteMultipleResponse(byte[] request, byte[] response)
    {
        if (response.Length != 5 ||
            response[0] != request[0] ||
            !response.AsSpan(1, 4).SequenceEqual(request.AsSpan(1, 4)))
        {
            throw new ModbusProtocolException("The write-multiple response has an invalid address or quantity echo.");
        }
    }

    private static void ValidateQuantityAndRange(
        ushort startAddress,
        int quantity,
        int maximum,
        string parameterName)
    {
        if (quantity is < 1 || quantity > maximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Quantity must be between 1 and {maximum}.");
        }

        if (startAddress + quantity - 1 > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "The requested address range exceeds address 65535.");
        }
    }

    private void Record(string category, string message) =>
        _journal.Record(Interlocked.Increment(ref _eventTick), category, message);

    private void RecordError(string category, string message, Exception exception) =>
        Record(category, $"{message} {exception.GetType().Name}: {exception.Message}");

    private void SetConnectionFault(Exception exception)
    {
        Volatile.Write(ref _lastError, exception.Message);
        SetMonitorState(ModbusConnectionState.Faulted);
    }

    private void SetMonitorState(ModbusConnectionState state) =>
        Volatile.Write(ref _monitorState, (int)state);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
