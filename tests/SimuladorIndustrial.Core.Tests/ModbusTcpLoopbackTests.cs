using System.Net;
using System.Net.Sockets;
using SimuladorIndustrial.Core.Diagnostics;
using SimuladorIndustrial.Core.Drivers.Modbus;
using SimuladorIndustrial.Core.Persistence;
using SimuladorIndustrial.Core.Tags;

namespace SimuladorIndustrial.Core.Tests;

public sealed class ModbusTcpLoopbackTests
{
    [Fact]
    public async Task SupportsAllMvpFunctionsDirectionsAndExplicitReconnect()
    {
        var rig = CreateRegistry();
        await using var server = new ModbusTcpServer(
            rig.Registry,
            rig.Journal,
            IPAddress.Loopback,
            0,
            unitId: 17,
            ioTimeout: TimeSpan.FromSeconds(3));
        await server.StartAsync();

        await using var client = new ModbusTcpClient(
            IPAddress.Loopback.ToString(),
            server.BoundPort,
            rig.Journal,
            unitId: 17,
            ioTimeout: TimeSpan.FromSeconds(3));
        await client.ConnectAsync();

        Assert.Equal(new[] { false, false }, await client.ReadCoilsAsync(0, 2));
        Assert.Equal(new[] { true, false }, await client.ReadDiscreteInputsAsync(0, 2));
        Assert.Equal(
            new ushort[] { 0x0102, 0x0304, 0x3FC0, 0x0000 },
            await client.ReadHoldingRegistersAsync(10, 4));
        Assert.Equal(
            new ushort[] { 0x1234, 0x5678, 0xC148, 0x0000 },
            await client.ReadInputRegistersAsync(10, 4));

        await client.WriteSingleCoilAsync(0, true);
        Assert.True(rig.Registry.Get(rig.Coil0.Id).RawValue.AsBool());

        await client.WriteSingleRegisterAsync(10, 0xA1B2);
        Assert.Equal(
            unchecked((int)0xA1B20304u),
            rig.Registry.Get(rig.HoldingInt.Id).RawValue.AsInt32());

        await client.WriteMultipleCoilsAsync(0, new[] { false, true });
        Assert.Equal(new[] { false, true }, await client.ReadCoilsAsync(0, 2));
        Assert.False(rig.Registry.Get(rig.Coil0.Id).RawValue.AsBool());
        Assert.True(rig.Registry.Get(rig.Coil1.Id).RawValue.AsBool());

        await client.WriteMultipleRegistersAsync(
            10,
            new ushort[] { 0x1122, 0x3344, 0xC020, 0x0000 });
        Assert.Equal(0x11223344, rig.Registry.Get(rig.HoldingInt.Id).RawValue.AsInt32());
        Assert.Equal(-2.5f, rig.Registry.Get(rig.HoldingFloat.Id).RawValue.AsFloat32());

        await client.DisconnectAsync();
        Assert.False(client.IsConnected);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ReadCoilsAsync(0, 1));

        await client.ReconnectAsync();
        Assert.True(client.IsConnected);
        Assert.Equal(new[] { false, true }, await client.ReadCoilsAsync(0, 2));

        Assert.Contains(rig.Journal.Events, item => item.Category == "modbus.client.connected");
        Assert.Contains(rig.Journal.Events, item => item.Category == "modbus.client.reconnected");
        Assert.Contains(rig.Journal.Events, item => item.Category == "modbus.server.client-connected");
        Assert.Contains(rig.Journal.Events, item => item.Category == "modbus.tag-change");
    }

    [Fact]
    public async Task ReturnsCorrectExceptionsWithoutCorruptingTheConnectionOrAtomicWrites()
    {
        var rig = CreateRegistry();
        await using var server = new ModbusTcpServer(
            rig.Registry,
            rig.Journal,
            IPAddress.Loopback,
            0,
            unitId: 7,
            ioTimeout: TimeSpan.FromSeconds(3));
        await server.StartAsync();

        await using var client = new ModbusTcpClient(
            IPAddress.Loopback.ToString(),
            server.BoundPort,
            rig.Journal,
            unitId: 7,
            ioTimeout: TimeSpan.FromSeconds(3));
        await client.ConnectAsync();

        await AssertServerExceptionAsync(
            ModbusExceptionCode.IllegalFunction,
            () => client.SendRawRequestAsync(new byte[] { 0x11 }));
        await AssertServerExceptionAsync(
            ModbusExceptionCode.IllegalDataAddress,
            () => client.ReadCoilsAsync(500, 1));
        await AssertServerExceptionAsync(
            ModbusExceptionCode.IllegalDataAddress,
            () => client.WriteSingleCoilAsync(5, true));
        await AssertServerExceptionAsync(
            ModbusExceptionCode.IllegalDataValue,
            () => client.SendRawRequestAsync(new byte[] { 0x05, 0x00, 0x00, 0x12, 0x34 }));
        await AssertServerExceptionAsync(
            ModbusExceptionCode.IllegalDataValue,
            () => client.SendRawRequestAsync(new byte[] { 0x01, 0x00, 0x00, 0x07, 0xD1 }));
        await AssertServerExceptionAsync(
            ModbusExceptionCode.IllegalDataValue,
            () => client.SendRawRequestAsync(new byte[] { 0x01, 0x00, 0x00 }));

        var intBefore = rig.Registry.Get(rig.HoldingInt.Id).RawValue;
        var floatBefore = rig.Registry.Get(rig.HoldingFloat.Id).RawValue;
        await AssertServerExceptionAsync(
            ModbusExceptionCode.IllegalDataValue,
            () => client.SendRawRequestAsync(
                new byte[] { 0x10, 0x00, 0x0A, 0x00, 0x04, 0x08, 0xDE, 0xAD, 0xBE, 0xEF, 0x7F, 0x80, 0x00, 0x00 }));
        Assert.Equal(intBefore, rig.Registry.Get(rig.HoldingInt.Id).RawValue);
        Assert.Equal(floatBefore, rig.Registry.Get(rig.HoldingFloat.Id).RawValue);

        Assert.True(client.IsConnected);
        Assert.Equal(new[] { false, false }, await client.ReadCoilsAsync(0, 2));

        await using var wrongUnitClient = new ModbusTcpClient(
            IPAddress.Loopback.ToString(),
            server.BoundPort,
            rig.Journal,
            unitId: 8,
            ioTimeout: TimeSpan.FromSeconds(3));
        await wrongUnitClient.ConnectAsync();
        await AssertServerExceptionAsync(
            ModbusExceptionCode.GatewayTargetDeviceFailedToRespond,
            () => wrongUnitClient.ReadCoilsAsync(0, 1));
    }

    [Fact]
    public async Task TimeoutAndCancellationInvalidateAnIncompleteTcpExchange()
    {
        await AssertIncompleteExchangeAsync(
            TimeSpan.FromMilliseconds(150),
            static client => client.ReadCoilsAsync(0, 1),
            expectTimeout: true);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await AssertIncompleteExchangeAsync(
            Timeout.InfiniteTimeSpan,
            client => client.ReadCoilsAsync(0, 1, cancellation.Token),
            expectTimeout: false);
    }

    [Fact]
    public async Task SelectsRoleFromSceneConnectionAndExposesQualityAndMonitorState()
    {
        var rig = CreateRegistry();
        var port = ReserveEphemeralPort();
        var serverConnection = new ConnectionDocument(
            Guid.NewGuid(),
            "Servidor local",
            "modbusTcp",
            "server",
            IPAddress.Loopback.ToString(),
            port,
            23);
        var clientConnection = serverConnection with
        {
            Id = Guid.NewGuid(),
            Name = "Cliente local",
            Role = "client"
        };

        await using var serverDriver = ModbusTcpDriver.FromConnection(
            serverConnection,
            rig.Registry,
            rig.Journal,
            TimeSpan.FromSeconds(3));
        await using var clientDriver = ModbusTcpDriver.FromConnection(
            clientConnection,
            rig.Registry,
            rig.Journal,
            TimeSpan.FromSeconds(3));

        Assert.Equal(ModbusConnectionRole.Server, serverDriver.Role);
        Assert.NotNull(serverDriver.Server);
        Assert.Null(serverDriver.Client);
        Assert.Equal(ModbusConnectionRole.Client, clientDriver.Role);
        Assert.NotNull(clientDriver.Client);
        Assert.Null(clientDriver.Server);

        await serverDriver.StartAsync();
        Assert.Equal(TagQuality.Disconnected, rig.Registry.Get(rig.Coil0.Id).Quality);
        Assert.Equal(ModbusConnectionState.Listening, serverDriver.Monitor.State);

        await clientDriver.StartAsync();
        Assert.Equal(new[] { false, false }, await clientDriver.Client!.ReadCoilsAsync(0, 2));
        await WaitUntilAsync(
            () => rig.Registry.Get(rig.Coil0.Id).Quality == TagQuality.Good &&
                  serverDriver.Monitor.ActiveConnections == 1);

        Assert.Equal(ModbusConnectionState.Connected, clientDriver.Monitor.State);
        Assert.Equal(1, clientDriver.Monitor.ActiveConnections);
        Assert.Equal(1, clientDriver.Monitor.TotalRequests);
        Assert.Equal(0, clientDriver.Monitor.FailedRequests);
        Assert.Equal(ModbusConnectionState.Connected, serverDriver.Monitor.State);
        Assert.Equal(1, serverDriver.Monitor.TotalRequests);

        await clientDriver.StopAsync();
        await WaitUntilAsync(
            () => rig.Registry.Get(rig.Coil0.Id).Quality == TagQuality.Disconnected &&
                  serverDriver.Monitor.ActiveConnections == 0);
        Assert.Equal(ModbusConnectionState.Disconnected, clientDriver.Monitor.State);
        Assert.Equal(ModbusConnectionState.Listening, serverDriver.Monitor.State);
        Assert.Contains(rig.Journal.Events, item => item.Category == "tag.quality");

        await serverDriver.ReconnectAsync();
        Assert.Equal(ModbusConnectionState.Listening, serverDriver.Monitor.State);
    }

    [Fact]
    public async Task ClientRoleSynchronizesAllTagAreasAndRecoversAfterReconnect()
    {
        var remote = CreateRegistry();
        var port = ReserveEphemeralPort();
        await using var server = new ModbusTcpServer(
            remote.Registry,
            remote.Journal,
            IPAddress.Loopback,
            port,
            unitId: 31,
            ioTimeout: TimeSpan.FromSeconds(3));
        await server.StartAsync();

        var localJournal = new EventJournal();
        var localRegistry = new TagRegistry(localJournal);
        foreach (var tag in remote.Registry.GetAll())
        {
            localRegistry.Register(tag.Definition);
        }

        var connection = new ConnectionDocument(
            Guid.NewGuid(),
            "Espelho Modbus",
            "modbusTcp",
            "client",
            IPAddress.Loopback.ToString(),
            port,
            31);
        await using var driver = ModbusTcpDriver.FromConnection(
            connection,
            localRegistry,
            localJournal,
            TimeSpan.FromSeconds(3));

        await driver.StartAsync();
        var first = await driver.SynchronizeOnceAsync(10);

        Assert.Equal(9, first.SynchronizedTags);
        Assert.Equal(6, first.ChangedValues);
        foreach (var expected in remote.Registry.GetAll())
        {
            var actual = localRegistry.Get(expected.Definition.Id);
            Assert.Equal(expected.RawValue, actual.RawValue);
            Assert.Equal(TagQuality.Good, actual.Quality);
        }

        await server.StopAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => driver.SynchronizeOnceAsync(11));
        Assert.All(
            localRegistry.GetAll(),
            tag => Assert.Equal(TagQuality.Disconnected, tag.Quality));

        await server.StartAsync();
        await driver.ReconnectAsync();
        var recovered = await driver.SynchronizeOnceAsync(12);

        Assert.Equal(9, recovered.SynchronizedTags);
        Assert.All(localRegistry.GetAll(), tag => Assert.Equal(TagQuality.Good, tag.Quality));
        Assert.Contains(localJournal.Events, item => item.Category == "modbus.client.sync-failed");
        Assert.Contains(localJournal.Events, item => item.Category == "modbus.client.sync-succeeded");
        Assert.Contains(localJournal.Events, item => item.Category == "modbus.client.reconnected");
    }

    private static async Task AssertIncompleteExchangeAsync(
        TimeSpan timeout,
        Func<ModbusTcpClient, Task<bool[]>> operation,
        bool expectTimeout)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var acceptTask = listener.AcceptTcpClientAsync();
            await using var client = new ModbusTcpClient(
                IPAddress.Loopback.ToString(),
                port,
                new EventJournal(),
                ioTimeout: timeout);
            await client.ConnectAsync();
            using var acceptedClient = await acceptTask;

            if (expectTimeout)
            {
                await Assert.ThrowsAsync<TimeoutException>(() => operation(client));
            }
            else
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation(client));
            }

            Assert.False(client.IsConnected);
            Assert.Equal(ModbusConnectionState.Faulted, client.Monitor.State);
            Assert.Equal(1, client.Monitor.TotalRequests);
            Assert.Equal(1, client.Monitor.FailedRequests);
            Assert.NotNull(client.Monitor.LastError);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task AssertServerExceptionAsync(
        ModbusExceptionCode expected,
        Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<ModbusServerException>(action);
        Assert.Equal(expected, exception.ExceptionCode);
    }

    private static int ReserveEphemeralPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail("The expected asynchronous Modbus state transition did not occur.");
            }

            await Task.Delay(20);
        }
    }

    private static RegistryRig CreateRegistry()
    {
        var journal = new EventJournal();
        var registry = new TagRegistry(journal);
        var coil0 = CreateTag("motor_0", TagDataType.Bool, TagDirection.PlcOutput, 0);
        var coil1 = CreateTag("motor_1", TagDataType.Bool, TagDirection.PlcOutput, 1);
        var discrete0 = CreateTag("sensor_0", TagDataType.Bool, TagDirection.PlcInput, 0);
        var discrete1 = CreateTag("sensor_1", TagDataType.Bool, TagDirection.PlcInput, 1);
        var discreteOnly = CreateTag("sensor_5", TagDataType.Bool, TagDirection.PlcInput, 5);
        var holdingInt = CreateTag("setpoint_int", TagDataType.Int32, TagDirection.PlcOutput, 10);
        var holdingFloat = CreateTag("setpoint_float", TagDataType.Float32, TagDirection.PlcOutput, 12);
        var inputInt = CreateTag("medicao_int", TagDataType.Int32, TagDirection.PlcInput, 10);
        var inputFloat = CreateTag("medicao_float", TagDataType.Float32, TagDirection.PlcInput, 12);

        foreach (var tag in new[]
                 {
                     coil0,
                     coil1,
                     discrete0,
                     discrete1,
                     discreteOnly,
                     holdingInt,
                     holdingFloat,
                     inputInt,
                     inputFloat
                 })
        {
            registry.Register(tag);
        }

        registry.PublishFromSimulation(discrete0.Id, TagValue.FromBool(true), 1);
        registry.PublishFromSimulation(discrete1.Id, TagValue.FromBool(false), 1);
        registry.PublishFromSimulation(discreteOnly.Id, TagValue.FromBool(true), 1);
        registry.PublishFromSimulation(inputInt.Id, TagValue.FromInt32(0x12345678), 1);
        registry.PublishFromSimulation(inputFloat.Id, TagValue.FromFloat32(-12.5f), 1);
        registry.WriteFromPlc(holdingInt.Id, TagValue.FromInt32(0x01020304), 1);
        registry.WriteFromPlc(holdingFloat.Id, TagValue.FromFloat32(1.5f), 1);

        return new RegistryRig(
            journal,
            registry,
            coil0,
            coil1,
            holdingInt,
            holdingFloat);
    }

    private static TagDefinition CreateTag(
        string canonicalName,
        TagDataType dataType,
        TagDirection direction,
        int address) => new(
            Guid.NewGuid(),
            canonicalName,
            canonicalName,
            dataType,
            direction,
            address,
            $"test.{canonicalName}");

    private sealed record RegistryRig(
        EventJournal Journal,
        TagRegistry Registry,
        TagDefinition Coil0,
        TagDefinition Coil1,
        TagDefinition HoldingInt,
        TagDefinition HoldingFloat);
}
