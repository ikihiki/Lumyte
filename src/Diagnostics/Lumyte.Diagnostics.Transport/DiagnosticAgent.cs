using System.Text.Encodings.Web;

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
    private const int MaximumPublicationBytes = 4 * 1024 * 1024;
    private const int EnvelopeBytes = 1024;
    private const int MaximumConcurrentCommands = 128;
    private readonly CancellationTokenSource _stop = new();
    private IDiagnosticConnection? _connection;
    private Task? _run;
    private int _connected;
    private int _disposed;
    private int _protocolVersion;

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

        DiagnosticSubsystemCatalog[] catalog = pump.Catalog.ToArray();
        bool requiresVersionTwo = catalog.Any(static subsystem => subsystem.Operations.Any(static operation =>
            operation.Traffic != DiagnosticOperationTraffic.Standard
            || operation.Arguments.Any(static field => field.MaxLength > 4096)
            || operation.Results.Any(static field => field.MaxLength > 4096)));

        // Refuse older servers rather than silently losing the catalog's scheduling or payload limits.
        _protocolVersion = requiresVersionTwo ? 2 : 1;
        _connection = await factory.OpenAsync(new(identity.InstanceId, _protocolVersion, catalog), cancellationToken).ConfigureAwait(false);
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

    private static long EstimateEventBytes(DiagnosticEvent item)
    {
        // JSON may escape each UTF-16 code unit as six bytes. Fixed allowances cover
        // property names, scalar envelopes and numeric values, and also bound MessagePack.
        long characters = item.Kind.Length + (long)item.Name.Length + (item.Value.String?.Length ?? 0)
            + (item.TraceId?.Length ?? 0) + (item.SpanId?.Length ?? 0) + (item.ParentSpanId?.Length ?? 0);
        if (item.Fields is Dictionary<string, DiagnosticValue> fields)
        {
            foreach ((string name, DiagnosticValue value) in fields)
            {
                characters += name.Length + (long)(value.String?.Length ?? 0);
            }
        }
        else
        {
            foreach ((string name, DiagnosticValue value) in item.Fields)
            {
                characters += name.Length + (long)(value.String?.Length ?? 0);
            }
        }

        return 1024 + (item.Fields.Count * 128L) + (characters * 6);
    }

    private static long EstimateTextBytes(string? value)
    {
        long bytes = 0;
        if (value != null)
        {
            foreach (char character in value)
            {
                // The default JSON encoder escapes non-ASCII UTF-16 units. Six bytes also
                // conservatively cover short escapes and all MessagePack UTF-8 encodings.
                bytes += character is >= ' ' and <= '~' && !JavaScriptEncoder.Default.WillEncode(character) ? 1 : 6;
            }
        }

        return bytes;
    }

    private static bool ResultFitsPublication(DiagnosticOperationResult result)
    {
        ResultSizeWriter writer = default;
        if (result.Values != null)
        {
            DiagnosticValueWriting.WriteTo(result.Values, ref writer);
        }

        return EnvelopeBytes + writer.Bytes + EstimateTextBytes(result.Status) + EstimateTextBytes(result.Code) + EstimateTextBytes(result.Message) <= MaximumPublicationBytes - EnvelopeBytes;
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

    private Task ReadAsync(IDiagnosticConnection connection, CancellationToken cancellationToken)
        => _protocolVersion == 1 ? ReadSerialAsync(connection, cancellationToken) : ReadConcurrentAsync(connection, cancellationToken);

    private async Task ReadSerialAsync(IDiagnosticConnection connection, CancellationToken cancellationToken)
    {
        var permissions = new HashSet<DiagnosticPermission>(connection.Welcome.Permissions);
        await foreach (DiagnosticCommand command in connection.ReadCommandsAsync(cancellationToken).ConfigureAwait(false))
        {
            await ExecuteCommandAsync(connection, command, permissions, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReadConcurrentAsync(IDiagnosticConnection connection, CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var permissions = new HashSet<DiagnosticPermission>(connection.Welcome.Permissions);
        var pending = new Dictionary<Guid, Task>();
        await using IAsyncEnumerator<DiagnosticCommand> commands = connection.ReadCommandsAsync(stop.Token).GetAsyncEnumerator(stop.Token);
        Task<bool>? next = null;
        try
        {
            while (true)
            {
                if (next == null && pending.Count < MaximumConcurrentCommands)
                {
                    next = commands.MoveNextAsync().AsTask();
                }

                if (pending.Count != 0)
                {
                    await Task.WhenAny(next == null ? pending.Values : pending.Values.Append(next)).ConfigureAwait(false);
                    foreach (Guid requestId in pending.Where(static pair => pair.Value.IsCompleted).Select(static pair => pair.Key).ToArray())
                    {
                        await pending[requestId].ConfigureAwait(false);
                        pending.Remove(requestId);
                    }
                }

                if (next != null && (pending.Count == 0 || next.IsCompleted))
                {
                    if (!await next.ConfigureAwait(false))
                    {
                        return;
                    }

                    DiagnosticCommand command = commands.Current;
                    next = null;
                    if (!pending.ContainsKey(command.RequestId))
                    {
                        pending.Add(command.RequestId, ExecuteCommandAsync(connection, command, permissions, stop.Token));
                    }
                }
            }
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            try
            {
                await Task.WhenAll(next == null ? pending.Values : pending.Values.Append(next)).ConfigureAwait(false);
            }
            catch (Exception) when (stop.IsCancellationRequested)
            {
                // Observe all remaining operations without hiding the exception that ended reception.
            }
        }
    }

    private async Task ExecuteCommandAsync(IDiagnosticConnection connection, DiagnosticCommand command, HashSet<DiagnosticPermission> permissions, CancellationToken cancellationToken)
    {
        long remaining = Math.Clamp(command.ExpiresUnixMilliseconds - clock.GetUtcNow().ToUnixTimeMilliseconds(), 0, 30000);
        var context = new DiagnosticOperationContext(command.RequestId, SessionId, default, command.ActorId, command.ExpectedRevision, cancellationToken);
        long deadline = clock.GetTimestamp() + ((remaining * clock.TimestampFrequency) / 1000);
        DiagnosticOperationResult result = await pump.SubmitAsync(new(command.SubsystemId, command.OperationId, context, command.Arguments, permissions, deadline))
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!ResultFitsPublication(result))
        {
            result = DiagnosticOperationResult.Reject("result-too-large", "The encoded operation result exceeds the publication byte budget.");
        }

        PublishReceipt receipt = await connection.PublishAsync(new(Guid.NewGuid(), SessionId, DiagnosticMessageKind.CommandResult, command.RequestId, result, []), cancellationToken).ConfigureAwait(false);
        if (!receipt.Accepted && receipt.ErrorCode != "expired")
        {
            throw new DiagnosticTransportException("The server rejected an operation result: " + receipt.ErrorCode);
        }
    }

    private async Task SendAsync(IDiagnosticConnection connection, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        List<DiagnosticEvent>? events = null;
        DiagnosticEvent? pending = null;
        int idle = 0;
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            long bytes = EnvelopeBytes;
            while ((events?.Count ?? 0) < 128 && (pending != null || telemetry.TryRead(out pending)))
            {
                long eventBytes = EstimateEventBytes(pending!);
                if (eventBytes > MaximumPublicationBytes - EnvelopeBytes)
                {
                    throw new DiagnosticTransportException("A telemetry event exceeds the publication byte budget.");
                }

                if (bytes + eventBytes > MaximumPublicationBytes)
                {
                    break;
                }

                (events ??= new(128)).Add(pending!);
                bytes += eventBytes;
                pending = null;
            }

            int count = events?.Count ?? 0;
            if (count == 0 && ++idle < 10)
            {
                continue;
            }

            idle = 0;
            DiagnosticMessageKind kind = count == 0 ? DiagnosticMessageKind.Heartbeat : DiagnosticMessageKind.Telemetry;
            DiagnosticEvent[] batch = count == 0 ? [] : events!.ToArray();
            events?.Clear();
            PublishReceipt receipt = await connection.PublishAsync(new(Guid.NewGuid(), SessionId, kind, null, null, batch), cancellationToken).ConfigureAwait(false);
            if (!receipt.Accepted)
            {
                throw new DiagnosticTransportException("The server rejected telemetry: " + receipt.ErrorCode);
            }
        }
    }

    private struct ResultSizeWriter : IDiagnosticValueWriter
    {
        public long Bytes { get; private set; }

        public void Write(string name, bool value) => Bytes += 128 + EstimateTextBytes(name);

        public void Write(string name, long value) => Bytes += 128 + EstimateTextBytes(name);

        public void Write(string name, double value) => Bytes += 128 + EstimateTextBytes(name);

        public void Write(string name, string value) => Bytes += 128 + EstimateTextBytes(name) + EstimateTextBytes(value);
    }
}
