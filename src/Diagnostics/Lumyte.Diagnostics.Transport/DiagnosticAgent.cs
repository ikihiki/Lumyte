namespace Lumyte.Diagnostics.Transport;

/// <summary>Routes authenticated network commands to a scoped owning-thread pump.</summary>
/// <param name="factory">The selected transport.</param>
/// <param name="pump">The scoped execution queue.</param>
/// <param name="telemetry">The scoped collection buffer.</param>
/// <param name="identity">The game identity.</param>
/// <param name="clock">The monotonic clock.</param>
/// <typeparam name="TPoint">The engine execution point.</typeparam>
public sealed class DiagnosticAgent<TPoint>(IDiagnosticTransportFactory factory, IDiagnosticPump<TPoint> pump, DiagnosticTelemetry telemetry, IGameExecutionIdentity identity, TimeProvider clock) : IAsyncDisposable
    where TPoint : class
{
    private readonly CancellationTokenSource _stop = new();
    private IDiagnosticConnection? _connection;
    private Task? _run;
    private int _connected;
    private int _disposed;

    /// <summary>Gets the connected session; read after ConnectAsync completes.</summary>
    public Guid SessionId => _connection?.Welcome.SessionId ?? Guid.Empty;

    /// <summary>Connects after the owning thread has activated the pump.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The connection task.</returns>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _connected, 1) != 0)
        {
            throw new InvalidOperationException("An agent connects only once.");
        }

        if (pump.Catalog.Count == 0)
        {
            throw new InvalidOperationException("Activate the execution point before connecting.");
        }

        _connection = await factory.OpenAsync(new(identity.InstanceId, 1, pump.Catalog.ToArray()), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Starts command reception and bounded telemetry publication.</summary>
    /// <returns>The task that ends on cancellation or connection failure.</returns>
    public Task RunAsync()
    {
        if (_connection == null || _run != null)
        {
            throw new InvalidOperationException("Connect first; run only once.");
        }

        return _run = RunCoreAsync(_connection);
    }

    /// <summary>Stops network work; the engine releases leases and deactivates on its owning thread.</summary>
    /// <returns>The shutdown task.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        if (_run != null)
        {
            try
            {
                await _run.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (DiagnosticTransportException)
            {
            }
        }

        if (_connection != null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }

        _stop.Dispose();
    }

    private async Task RunCoreAsync(IDiagnosticConnection connection)
    {
        Task commands = ReadAsync(connection, _stop.Token);
        Task events = SendAsync(connection, _stop.Token);
        await Task.WhenAny(commands, events).ConfigureAwait(false);
        await _stop.CancelAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(commands, events).ConfigureAwait(false);
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task ReadAsync(IDiagnosticConnection connection, CancellationToken cancellationToken)
    {
        var permissions = new HashSet<DiagnosticPermission>(connection.Welcome.Permissions);
        await foreach (DiagnosticCommand command in connection.ReadCommandsAsync(cancellationToken).ConfigureAwait(false))
        {
            long remaining = Math.Clamp(command.ExpiresUnixMilliseconds - clock.GetUtcNow().ToUnixTimeMilliseconds(), 0, 30000);
            var context = new DiagnosticOperationContext(command.RequestId, SessionId, default, command.ActorId, command.ExpectedRevision, cancellationToken);
            long deadline = clock.GetTimestamp() + ((remaining * clock.TimestampFrequency) / 1000);
            DiagnosticOperationResult result = await pump.SubmitAsync(new(command.SubsystemId, command.OperationId, context, command.Arguments, permissions, deadline))
                .WaitAsync(cancellationToken).ConfigureAwait(false);
            PublishReceipt receipt = await connection.PublishAsync(new(Guid.NewGuid(), SessionId, DiagnosticMessageKind.CommandResult, command.RequestId, result, []), cancellationToken).ConfigureAwait(false);
            if (!receipt.Accepted && receipt.ErrorCode != "expired")
            {
                throw new DiagnosticTransportException("The server rejected an operation result: " + receipt.ErrorCode);
            }
        }
    }

    private async Task SendAsync(IDiagnosticConnection connection, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        int idle = 0;
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var events = new List<DiagnosticEvent>(128);
            while (events.Count < 128 && telemetry.TryRead(out DiagnosticEvent? item))
            {
                events.Add(item!);
            }

            if (events.Count == 0 && ++idle < 10)
            {
                continue;
            }

            idle = 0;
            DiagnosticMessageKind kind = events.Count == 0 ? DiagnosticMessageKind.Heartbeat : DiagnosticMessageKind.Telemetry;
            PublishReceipt receipt = await connection.PublishAsync(new(Guid.NewGuid(), SessionId, kind, null, null, events.ToArray()), cancellationToken).ConfigureAwait(false);
            if (!receipt.Accepted)
            {
                throw new DiagnosticTransportException("The server rejected telemetry: " + receipt.ErrorCode);
            }
        }
    }
}
