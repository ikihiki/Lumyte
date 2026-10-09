using System.Buffers;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Lumyte.Diagnostics.Transport;
using Lumyte.Diagnostics.Transport.MagicOnion;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Diagnostics.IntegrationTests;

/// <summary>Checks publication batching through the transport-independent game agent.</summary>
public sealed class AgentTests
{
    /// <summary>Checks byte and count bounds without losing the event carried to the next batch.</summary>
    /// <param name="largeFields">Whether each event carries maximum-length escaped fields.</param>
    /// <returns>The asynchronous agent test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicationBatchesRespectWireBudgetsAsync(bool largeFields)
    {
        int count = largeFields ? 13 : 129;
        var services = new ServiceCollection();
        services.AddLumyteDiagnostics(options =>
        {
            options.Enabled = true;
            options.AllowedMeterNames = ["Lumyte.AgentTests"];
            options.QueueCapacity = 256;
        });
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        DiagnosticTelemetry telemetry = scope.ServiceProvider.GetRequiredService<DiagnosticTelemetry>();
        IGameExecutionIdentity identity = scope.ServiceProvider.GetRequiredService<IGameExecutionIdentity>();
        telemetry.Start();
        Meter meter = provider.GetRequiredService<IMeterFactory>().Create(new MeterOptions("Lumyte.AgentTests"));
        Counter<long> counter = meter.CreateCounter<long>("sequence");
        string text = new('\u0001', 4096);
        KeyValuePair<string, object?>[] tags = largeFields
            ? Enumerable.Range(0, 32).Select(index => new KeyValuePair<string, object?>($"field-{index}", text)).ToArray()
            : new KeyValuePair<string, object?>[1];
        tags[^1] = new("lumyte.instance.id", identity.InstanceId.ToString("D"));
        for (int i = 0; i < count; i++)
        {
            counter.Add(i, tags.AsSpan());
        }

        var connection = new CaptureConnection(count);
        await using var agent = new DiagnosticAgent<AgentPoint>(new CaptureFactory(connection), new InactivePump(), telemetry, identity, TimeProvider.System);
        await agent.ConnectAsync();
        _ = agent.RunAsync();
        await connection.Completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await agent.DisposeAsync();
        DiagnosticMessage[] publications = connection.Publications.ToArray();
        Assert.True(publications.Length > 1);
        Assert.Equal(Enumerable.Range(0, count).Select(value => (long)value), publications.SelectMany(message => message.Events).Select(item => item.Value.Int64));
        Assert.Equal(0, telemetry.Dropped);
        Assert.False(telemetry.TryRead(out _));
        foreach (DiagnosticMessage message in publications)
        {
            Assert.Equal(DiagnosticMessageKind.Telemetry, message.Kind);
            Assert.InRange(message.Events.Length, 1, 128);
            var json = new ArrayBufferWriter<byte>();
            DiagnosticJsonMessageEncoder.Write(json, message);
            Assert.InRange(json.WrittenCount, 1, 4 * 1024 * 1024);
            byte[] messagePack = MessagePackSerializer.Serialize(message, DiagnosticMessagePack.Options);
            Assert.InRange(messagePack.Length, 1, (4 * 1024 * 1024) - 1024);
            if (largeFields)
            {
                Assert.All(message.Events, item => Assert.Equal(text, item.Fields["field-0"].String));
            }
        }
    }

    private sealed class AgentPoint;

    private sealed class CaptureFactory(CaptureConnection connection) : IDiagnosticTransportFactory
    {
        public ValueTask<IDiagnosticConnection> OpenAsync(ClientHello hello, CancellationToken cancellationToken) => ValueTask.FromResult<IDiagnosticConnection>(connection);
    }

    private sealed class CaptureConnection(int expectedEvents) : IDiagnosticConnection
    {
        private int _received;

        public SessionWelcome Welcome { get; } = new(Guid.NewGuid(), "test", [DiagnosticPermission.Observe]);

        public List<DiagnosticMessage> Publications { get; } = [];

        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<DiagnosticCommand> ReadCommandsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }

        public ValueTask<PublishReceipt> PublishAsync(DiagnosticMessage message, CancellationToken cancellationToken)
        {
            Publications.Add(message);
            _received += message.Events.Length;
            if (_received == expectedEvents)
            {
                Completed.TrySetResult();
            }

            return ValueTask.FromResult(new PublishReceipt(message.MessageId, true));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class InactivePump : IDiagnosticPump<AgentPoint>
    {
        public IReadOnlyList<DiagnosticSubsystemCatalog> Catalog { get; } = [new(new("test", "Test", 1), [])];

        public void Activate() => throw new NotSupportedException();

        public Task<DiagnosticOperationResult> SubmitAsync(DiagnosticRequest request) => throw new NotSupportedException();

        public void Pump(DiagnosticFrame frame, DiagnosticBudget budget) => throw new NotSupportedException();

        public void Deactivate() => throw new NotSupportedException();
    }
}
