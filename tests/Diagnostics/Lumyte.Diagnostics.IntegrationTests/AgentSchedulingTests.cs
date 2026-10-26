using System.Buffers;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Lumyte.Diagnostics.IntegrationTests;

/// <summary>Checks command reception remains live while operations await the owning thread.</summary>
public sealed class AgentSchedulingTests
{
    /// <summary>Checks pending ordinary and stream work cannot hide a later stop command.</summary>
    /// <returns>The asynchronous scheduling check.</returns>
    [Fact]
    public async Task PendingOperationsDoNotBlockControlAndDuplicatesAreCoalescedAsync()
    {
        var pump = new TestPump();
        var connection = new TestConnection();
        using ServiceProvider provider = Services();
        using IServiceScope scope = provider.CreateScope();
        await using DiagnosticAgent<Point> agent = Agent(scope.ServiceProvider, pump, connection);
        await agent.ConnectAsync();
        _ = agent.RunAsync();
        DiagnosticCommand standard = Command("standard");
        DiagnosticCommand stream = Command("stream");
        DiagnosticCommand control = Command("control");
        connection.Commands.Writer.TryWrite(standard);
        connection.Commands.Writer.TryWrite(stream);
        connection.Commands.Writer.TryWrite(standard);
        connection.Commands.Writer.TryWrite(control);
        DiagnosticMessage response = await connection.Results.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(control.RequestId, response.RequestId);
        Assert.Equal(3, pump.Submitted.Count);
        Assert.Equal(2, pump.Pending.Count);
        Assert.All(pump.Pending.Values, pending => Assert.False(pending.Task.IsCompleted));
        await agent.DisposeAsync();
        Assert.All(pump.Submitted, request => Assert.True(request.Context.CancellationToken.IsCancellationRequested));
    }

    /// <summary>Checks oversized aggregate or escaped results are rejected without losing later commands.</summary>
    /// <param name="escaped">Whether escaping makes a single field exceed the encoded budget.</param>
    /// <returns>The asynchronous result check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedResultPreservesConnectionAsync(bool escaped)
    {
        var fields = new Dictionary<string, DiagnosticValue>();
        for (int index = 0; index < (escaped ? 1 : 5); index++)
        {
            fields.Add("part-" + index, DiagnosticValue.From(new string(escaped ? '日' : 'x', 1024 * 1024)));
        }

        var pump = new TestPump { LargeResult = DiagnosticOperationResult.Success(fields) };
        var connection = new TestConnection();
        using ServiceProvider provider = Services();
        using IServiceScope scope = provider.CreateScope();
        await using DiagnosticAgent<Point> agent = Agent(scope.ServiceProvider, pump, connection);
        await agent.ConnectAsync();
        Task run = agent.RunAsync();
        connection.Commands.Writer.TryWrite(Command("large"));
        DiagnosticMessage rejected = await connection.Results.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("result-too-large", rejected.Result!.Code);
        connection.Commands.Writer.TryWrite(Command("control"));
        DiagnosticMessage success = await connection.Results.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("success", success.Result!.Status);
        Assert.False(run.IsCompleted);
    }

    /// <summary>Checks a declared one-megabyte ASCII field fits both encoded publication budgets.</summary>
    /// <returns>The asynchronous result check.</returns>
    [Fact]
    public async Task LargeAsciiResultIsNotOverestimatedAsSixBytesPerCharacterAsync()
    {
        string payload = new('x', 1024 * 1024);
        var pump = new TestPump { LargeResult = DiagnosticOperationResult.Success(new Dictionary<string, DiagnosticValue> { ["part"] = DiagnosticValue.From(payload) }) };
        var connection = new TestConnection();
        using ServiceProvider provider = Services();
        using IServiceScope scope = provider.CreateScope();
        await using DiagnosticAgent<Point> agent = Agent(scope.ServiceProvider, pump, connection);
        await agent.ConnectAsync();
        _ = agent.RunAsync();
        connection.Commands.Writer.TryWrite(Command("large"));
        DiagnosticMessage response = await connection.Results.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(payload, response.Result!.Values!["part"].String);
    }

    /// <summary>Checks a failed concurrent publication is observed and cancels pending pump requests.</summary>
    /// <returns>The asynchronous failure check.</returns>
    [Fact]
    public async Task ConcurrentFailureCancelsOtherPendingRequestsAsync()
    {
        var pump = new TestPump();
        var connection = new TestConnection { RejectResults = true };
        using ServiceProvider provider = Services();
        using IServiceScope scope = provider.CreateScope();
        await using DiagnosticAgent<Point> agent = Agent(scope.ServiceProvider, pump, connection);
        await agent.ConnectAsync();
        Task run = agent.RunAsync();
        connection.Commands.Writer.TryWrite(Command("standard"));
        connection.Commands.Writer.TryWrite(Command("control"));
        await Assert.ThrowsAsync<DiagnosticTransportException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.All(pump.Submitted, request => Assert.True(request.Context.CancellationToken.IsCancellationRequested));
    }

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddLumyteDiagnostics(options => options.Enabled = true);
        return services.BuildServiceProvider();
    }

    private static DiagnosticAgent<Point> Agent(IServiceProvider services, TestPump pump, TestConnection connection)
        => new(new TestFactory(connection), pump, services.GetRequiredService<DiagnosticTelemetry>(), services.GetRequiredService<IGameExecutionIdentity>(), TimeProvider.System);

    private static DiagnosticCommand Command(string operation)
        => new(Guid.NewGuid(), "test", operation, "actor", null, DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeMilliseconds(), []);

    private sealed class Point;

    private sealed class TestPump : IDiagnosticPump<Point>
    {
        public IReadOnlyList<DiagnosticSubsystemCatalog> Catalog { get; } = [new(new("test", "Test", 1), [
            new("standard", "Standard", DiagnosticPermission.Observe, [], []),
            new("stream", "Stream", DiagnosticPermission.Observe, [], [], Traffic: DiagnosticOperationTraffic.Stream),
            new("control", "Control", DiagnosticPermission.Observe, [], [], Traffic: DiagnosticOperationTraffic.Control),
        ])];

        public ConcurrentQueue<DiagnosticRequest> Submitted { get; } = new();

        public ConcurrentDictionary<Guid, TaskCompletionSource<DiagnosticOperationResult>> Pending { get; } = new();

        public DiagnosticOperationResult? LargeResult { get; init; }

        public void Activate() => throw new NotSupportedException();

        public Task<DiagnosticOperationResult> SubmitAsync(DiagnosticRequest request)
        {
            Submitted.Enqueue(request);
            if (request.OperationId is "control" or "large")
            {
                return Task.FromResult(request.OperationId == "large" ? LargeResult! : DiagnosticOperationResult.Success(new Dictionary<string, DiagnosticValue>()));
            }

            var completion = new TaskCompletionSource<DiagnosticOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.True(Pending.TryAdd(request.Context.RequestId, completion));
            return completion.Task;
        }

        public void Pump(DiagnosticFrame frame, DiagnosticBudget budget) => throw new NotSupportedException();

        public void Deactivate() => throw new NotSupportedException();
    }

    private sealed class TestFactory(TestConnection connection) : IDiagnosticTransportFactory
    {
        public ValueTask<IDiagnosticConnection> OpenAsync(ClientHello hello, CancellationToken cancellationToken) => ValueTask.FromResult<IDiagnosticConnection>(connection);
    }

    private sealed class TestConnection : IDiagnosticConnection
    {
        public SessionWelcome Welcome { get; } = new(Guid.NewGuid(), "test", [DiagnosticPermission.Observe]);

        public Channel<DiagnosticCommand> Commands { get; } = Channel.CreateUnbounded<DiagnosticCommand>();

        public Channel<DiagnosticMessage> Results { get; } = Channel.CreateUnbounded<DiagnosticMessage>();

        public bool RejectResults { get; init; }

        public IAsyncEnumerable<DiagnosticCommand> ReadCommandsAsync(CancellationToken cancellationToken) => Commands.Reader.ReadAllAsync(cancellationToken);

        public ValueTask<PublishReceipt> PublishAsync(DiagnosticMessage message, CancellationToken cancellationToken)
        {
            if (message.Kind == DiagnosticMessageKind.CommandResult)
            {
                var json = new ArrayBufferWriter<byte>();
                DiagnosticJsonMessageEncoder.Write(json, message);
                Assert.InRange(json.WrittenCount, 1, 4 * 1024 * 1024);
                Assert.InRange(MessagePackSerializer.Serialize(message, DiagnosticMessagePack.Options).Length, 1, 4 * 1024 * 1024);
                Results.Writer.TryWrite(message);
            }

            return ValueTask.FromResult(new PublishReceipt(message.MessageId, !RejectResults, RejectResults ? "invalid-result" : null));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
