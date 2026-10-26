using System.Buffers;
using System.Text.Json;
using Lumyte.Diagnostics.Server;

namespace Lumyte.Diagnostics.IntegrationTests;

/// <summary>Checks feature negotiation and append-only operation metadata across both encodings.</summary>
public sealed class TrafficProtocolTests
{
    /// <summary>Checks extended operations require version two while ordinary catalogs retain version one.</summary>
    /// <param name="traffic">The operation scheduling class.</param>
    /// <param name="maximumLength">The declared string limit.</param>
    /// <param name="resultField">Whether the extended field is an output.</param>
    /// <param name="expectedVersion">The required protocol version.</param>
    /// <returns>The connection test.</returns>
    [Theory]
    [InlineData(DiagnosticOperationTraffic.Standard, 4096, false, 1)]
    [InlineData(DiagnosticOperationTraffic.Standard, 4096, true, 1)]
    [InlineData(DiagnosticOperationTraffic.Stream, 4096, false, 2)]
    [InlineData(DiagnosticOperationTraffic.Control, 4096, false, 2)]
    [InlineData(DiagnosticOperationTraffic.Standard, 32768, false, 2)]
    [InlineData(DiagnosticOperationTraffic.Standard, 32768, true, 2)]
    public async Task AgentRequiresProtocolForCatalogFeaturesAsync(DiagnosticOperationTraffic traffic, int maximumLength, bool resultField, int expectedVersion)
    {
        DiagnosticField[] fields = [new("payload", DiagnosticValueKind.String, MaxLength: maximumLength)];
        var operation = new OperationDescriptor("operation", "Operation", DiagnosticPermission.Observe, resultField ? [] : fields, resultField ? fields : [], Traffic: traffic);
        var factory = new CaptureFactory();
        var services = new ServiceCollection();
        services.AddLumyteDiagnostics(options => options.Enabled = true);
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        await using var agent = new DiagnosticAgent<Point>(factory, new CatalogPump(operation), scope.ServiceProvider.GetRequiredService<DiagnosticTelemetry>(), scope.ServiceProvider.GetRequiredService<IGameExecutionIdentity>(), TimeProvider.System);
        await agent.ConnectAsync();
        Assert.Equal(expectedVersion, factory.Hello!.ProtocolVersion);
        Assert.Equal(traffic, Assert.Single(Assert.Single(factory.Hello.Catalog).Operations).Traffic);
    }

    /// <summary>Checks an older server cannot silently downgrade stream semantics.</summary>
    /// <returns>The rejected connection test.</returns>
    [Fact]
    public async Task UnsupportedVersionDoesNotRetryWithLegacySemanticsAsync()
    {
        var operation = new OperationDescriptor("stream", "Stream", DiagnosticPermission.Observe, [], [], Traffic: DiagnosticOperationTraffic.Stream);
        var factory = new CaptureFactory(reject: true);
        var services = new ServiceCollection();
        services.AddLumyteDiagnostics(options => options.Enabled = true);
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        await using var agent = new DiagnosticAgent<Point>(factory, new CatalogPump(operation), scope.ServiceProvider.GetRequiredService<DiagnosticTelemetry>(), scope.ServiceProvider.GetRequiredService<IGameExecutionIdentity>(), TimeProvider.System);
        await Assert.ThrowsAsync<DiagnosticTransportException>(() => agent.ConnectAsync());
        Assert.Equal(2, factory.Hello!.ProtocolVersion);
        Assert.Equal(1, factory.Attempts);
    }

    /// <summary>Checks HTTP and MessagePack metadata retain trusted traffic classification.</summary>
    /// <param name="traffic">The operation scheduling class.</param>
    [Theory]
    [InlineData(DiagnosticOperationTraffic.Standard)]
    [InlineData(DiagnosticOperationTraffic.Stream)]
    [InlineData(DiagnosticOperationTraffic.Control)]
    public void CatalogTrafficRoundTrips(DiagnosticOperationTraffic traffic)
    {
        var operation = new OperationDescriptor("operation", "Operation", DiagnosticPermission.Observe, [new("payload", DiagnosticValueKind.String, MaxLength: 32768)], [], Traffic: traffic);
        var hello = new ClientHello(Guid.NewGuid(), 2, [new(new("test", "Test", 1), [operation])]);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(hello, DiagnosticJson.Context.ClientHello);
        ClientHello decodedJson = JsonSerializer.Deserialize(json, DiagnosticJson.Context.ClientHello)!;
        byte[] messagePack = MessagePackSerializer.Serialize(WireMapper.ToWire(hello), DiagnosticMessagePack.Options);
        ClientHello decodedPack = WireMapper.FromWire(MessagePackSerializer.Deserialize<WireHello>(messagePack, DiagnosticMessagePack.Options));
        foreach (ClientHello decoded in new[] { decodedJson, decodedPack })
        {
            Assert.Equal(2, decoded.ProtocolVersion);
            OperationDescriptor decodedOperation = Assert.Single(Assert.Single(decoded.Catalog).Operations);
            Assert.Equal(traffic, decodedOperation.Traffic);
            Assert.Equal(32768, Assert.Single(decodedOperation.Arguments).MaxLength);
        }
    }

    /// <summary>Checks catalogs written before the appended traffic field retain standard scheduling.</summary>
    [Fact]
    public void LegacyOperationDefaultsToStandardTraffic()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteArrayHeader(6);
        writer.Write("legacy");
        writer.Write("Legacy");
        writer.Write((int)DiagnosticPermission.Observe);
        writer.WriteArrayHeader(0);
        writer.WriteArrayHeader(0);
        writer.Write(false);
        writer.Flush();
        WireOperation legacy = MessagePackSerializer.Deserialize<WireOperation>(buffer.WrittenMemory, DiagnosticMessagePack.Options);
        var hello = new WireHello
        {
            InstanceId = Guid.NewGuid().ToString("D"),
            ProtocolVersion = 1,
            Catalog = [new() { Id = "test", DisplayName = "Test", SchemaVersion = 1, Operations = [legacy] }],
        };
        OperationDescriptor operation = Assert.Single(Assert.Single(WireMapper.FromWire(hello).Catalog).Operations);
        Assert.Equal(DiagnosticOperationTraffic.Standard, operation.Traffic);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new ClientHello(Guid.NewGuid(), 1, [new(new("test", "Test", 1), [operation])]), DiagnosticJson.Context.ClientHello);
        string legacyJson = System.Text.Encoding.UTF8.GetString(json).Replace(",\"traffic\":0", string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("\"traffic\"", legacyJson, StringComparison.Ordinal);
        ClientHello decoded = JsonSerializer.Deserialize(legacyJson, DiagnosticJson.Context.ClientHello)!;
        Assert.Equal(DiagnosticOperationTraffic.Standard, Assert.Single(Assert.Single(decoded.Catalog).Operations).Traffic);
    }

    /// <summary>Checks the server rejects extended catalogs until the client requires version two.</summary>
    /// <param name="traffic">The operation scheduling class.</param>
    /// <param name="resultField">Whether to declare the extended field as an output.</param>
    [Theory]
    [InlineData(DiagnosticOperationTraffic.Stream, false)]
    [InlineData(DiagnosticOperationTraffic.Control, false)]
    [InlineData(DiagnosticOperationTraffic.Standard, false)]
    [InlineData(DiagnosticOperationTraffic.Standard, true)]
    public void ServerRequiresVersionTwoForExtendedCatalog(DiagnosticOperationTraffic traffic, bool resultField)
    {
        var registry = new DiagnosticSessionRegistry(new(), TimeProvider.System);
        DiagnosticField[] fields = [new("payload", DiagnosticValueKind.String, MaxLength: traffic == DiagnosticOperationTraffic.Standard ? 32768 : 4096)];
        var operation = new OperationDescriptor("operation", "Operation", DiagnosticPermission.Observe, resultField ? [] : fields, resultField ? fields : [], Traffic: traffic);
        var hello = new ClientHello(Guid.NewGuid(), 1, [new(new("test", "Test", 1), [operation])]);
        Assert.Throws<ArgumentException>(() => registry.Open(hello));
        SessionWelcome welcome = registry.Open(hello with { ProtocolVersion = 2 });
        Assert.NotEqual(Guid.Empty, welcome.SessionId);
        Assert.Equal(traffic, Assert.Single(Assert.Single(Assert.Single(registry.List()).Catalog).Operations).Traffic);
    }

    private sealed class Point;

    private sealed class CatalogPump(OperationDescriptor operation) : IDiagnosticPump<Point>
    {
        public IReadOnlyList<DiagnosticSubsystemCatalog> Catalog { get; } = [new(new("test", "Test", 1), [operation])];

        public void Activate() => throw new NotSupportedException();

        public Task<DiagnosticOperationResult> SubmitAsync(DiagnosticRequest request) => throw new NotSupportedException();

        public void Pump(DiagnosticFrame frame, DiagnosticBudget budget) => throw new NotSupportedException();

        public void Deactivate() => throw new NotSupportedException();
    }

    private sealed class CaptureFactory(bool reject = false) : IDiagnosticTransportFactory
    {
        public ClientHello? Hello { get; private set; }

        public int Attempts { get; private set; }

        public ValueTask<IDiagnosticConnection> OpenAsync(ClientHello hello, CancellationToken cancellationToken)
        {
            Hello = hello;
            Attempts++;
            if (reject)
            {
                throw new DiagnosticTransportException("Unsupported protocol version.");
            }

            return ValueTask.FromResult<IDiagnosticConnection>(new CaptureConnection());
        }
    }

    private sealed class CaptureConnection : IDiagnosticConnection
    {
        public SessionWelcome Welcome { get; } = new(Guid.NewGuid(), "test", [DiagnosticPermission.Observe]);

        public IAsyncEnumerable<DiagnosticCommand> ReadCommandsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<PublishReceipt> PublishAsync(DiagnosticMessage message, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
