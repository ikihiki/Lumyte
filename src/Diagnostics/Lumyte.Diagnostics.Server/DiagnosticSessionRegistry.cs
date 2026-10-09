using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using Lumyte.Diagnostics.Transport;

namespace Lumyte.Diagnostics.Server;

/// <summary>Bounded in-memory sessions shared by HTTP and MagicOnion endpoints.</summary>
/// <param name="options">The server capability policy.</param>
/// <param name="clock">The server monotonic clock.</param>
public sealed class DiagnosticSessionRegistry(DiagnosticServerOptions options, TimeProvider clock)
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Session> _sessions = [];

    /// <summary>Opens a negotiated session with optional pushed command delivery.</summary>
    /// <param name="hello">The game hello.</param>
    /// <param name="delivery">A hub command callback, or null for polling.</param>
    /// <returns>The session and capability grant.</returns>
    public SessionWelcome Open(ClientHello hello, Action<DiagnosticCommand>? delivery = null)
    {
        DiagnosticProtocol.Validate(hello);
        byte[] catalogBytes = JsonSerializer.SerializeToUtf8Bytes(hello, DiagnosticJson.Context.ClientHello);
        if (catalogBytes.Length > 256 * 1024)
        {
            throw new ArgumentException("Catalog exceeds the byte budget.", nameof(hello));
        }

        hello = JsonSerializer.Deserialize(catalogBytes, DiagnosticJson.Context.ClientHello)!;
        lock (_gate)
        {
            if (_sessions.Count >= 64)
            {
                throw new InvalidOperationException("Session capacity reached.");
            }

            var welcome = new SessionWelcome(Guid.NewGuid(), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), [.. options.GamePermissions]);
            _sessions.Add(welcome.SessionId, new(hello, welcome, delivery, clock.GetTimestamp()));
            return welcome;
        }
    }

    /// <summary>Gets operator-visible snapshots without session credentials.</summary>
    /// <returns>The active sessions.</returns>
    public SessionSnapshot[] List()
    {
        lock (_gate)
        {
            return _sessions.Values.Select(session => new SessionSnapshot(
                session.Welcome.SessionId,
                session.Hello.InstanceId,
                JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(session.Hello, DiagnosticJson.Context.ClientHello), DiagnosticJson.Context.ClientHello)!.Catalog,
                session.Commands.Values.Count(entry => !entry.Completion.Task.IsCompleted),
                session.Received,
                session.Dropped)).ToArray();
        }
    }

    /// <summary>Checks the HTTP session capability in addition to enrollment authentication.</summary>
    /// <param name="id">The session.</param>
    /// <param name="secret">The private session capability.</param>
    public void Authenticate(Guid id, string secret)
    {
        lock (_gate)
        {
            if (!DiagnosticAuthenticationHandler.Match(secret, Get(id).Welcome.SessionSecret))
            {
                throw new UnauthorizedAccessException("Wrong session capability.");
            }
        }
    }

    /// <summary>Waits for one unacknowledged command, allowing HTTP redelivery after response loss.</summary>
    /// <param name="id">The session.</param>
    /// <param name="cancellationToken">The request cancellation.</param>
    /// <returns>A command or an empty batch on poll timeout.</returns>
    public async Task<DiagnosticCommand[]> PollAsync(Guid id, CancellationToken cancellationToken)
    {
        Session session;
        lock (_gate)
        {
            session = Get(id);
            if (session.Delivery != null || !session.TryBeginPolling())
            {
                throw new InvalidOperationException("Only one HTTP poll is allowed.");
            }

            session.LastSeen = clock.GetTimestamp();
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            while (true)
            {
                lock (_gate)
                {
                    Entry? pending = session.Commands.Values.FirstOrDefault(entry => !entry.Completion.Task.IsCompleted);
                    if (pending != null)
                    {
                        return [pending.Command];
                    }
                }

                await session.Signal.Reader.ReadAsync(timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && session.Lifetime.IsCancellationRequested)
        {
            throw new KeyNotFoundException("Diagnostic session closed.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !session.Lifetime.IsCancellationRequested)
        {
            return [];
        }
        finally
        {
            session.EndPolling();
        }
    }

    /// <summary>Authorizes and queues an operator request; duplicate IDs share the same result.</summary>
    /// <param name="id">The target session.</param>
    /// <param name="invocation">The operator input.</param>
    /// <param name="actor">The authenticated actor, never an input field.</param>
    /// <param name="cancellationToken">The operator request cancellation.</param>
    /// <returns>The correlated result or explicit rejection.</returns>
    public async Task<DiagnosticOperationResult> InvokeAsync(Guid id, OperationInvocation invocation, string actor, CancellationToken cancellationToken)
    {
        DiagnosticProtocol.Validate(invocation);
        invocation = invocation with { Arguments = invocation.Arguments.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal) };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(invocation, DiagnosticJson.Context.OperationInvocation);
        if (bytes.Length > 64 * 1024)
        {
            throw new ArgumentException("Invocation exceeds the byte budget.", nameof(invocation));
        }

        invocation = JsonSerializer.Deserialize(bytes, DiagnosticJson.Context.OperationInvocation)!;
        string fingerprint = actor + Convert.ToHexString(SHA256.HashData(bytes));
        Entry entry;
        Action<DiagnosticCommand>? delivery = null;
        lock (_gate)
        {
            Session session = Get(id);
            OperationDescriptor? operation = session.Hello.Catalog.FirstOrDefault(catalog => catalog.Subsystem.Id == invocation.SubsystemId)?.Operations.FirstOrDefault(operation => operation.Id == invocation.OperationId);
            if (operation == null)
            {
                return DiagnosticOperationResult.Reject("not-found", "Unknown published operation.");
            }

            if (!options.OperatorPermissions.Contains(operation.RequiredPermission) || !session.Welcome.Permissions.Contains(operation.RequiredPermission))
            {
                return DiagnosticOperationResult.Reject("forbidden", "Operation is not permitted.");
            }

            foreach (Guid key in session.Commands.Where(pair => pair.Value.RetainUntil <= clock.GetTimestamp() && pair.Value.Completion.Task.IsCompleted).Select(pair => pair.Key).ToArray())
            {
                session.CommandBytes -= session.Commands[key].Bytes;
                session.Commands.Remove(key);
            }

            if (session.Commands.TryGetValue(invocation.RequestId, out Entry? existing))
            {
                if (existing.Fingerprint != fingerprint)
                {
                    return DiagnosticOperationResult.Reject("request-id-conflict", "Request ID was reused with different input.");
                }

                entry = existing;
            }
            else
            {
                if (session.Commands.Count >= 256 || session.CommandBytes + bytes.Length > 1024 * 1024
                    || session.Commands.Values.Count(entry => !entry.Completion.Task.IsCompleted) >= 64)
                {
                    return DiagnosticOperationResult.Reject("busy", "Command capacity reached.");
                }

                long expires = clock.GetUtcNow().ToUnixTimeMilliseconds() + invocation.TimeoutMilliseconds;
                entry = new(
                    new(invocation.RequestId, invocation.SubsystemId, invocation.OperationId, actor, invocation.ExpectedRevision, expires, invocation.Arguments),
                    fingerprint,
                    clock.GetTimestamp() + ((invocation.TimeoutMilliseconds + 30000L) * clock.TimestampFrequency / 1000),
                    bytes.Length);
                session.Commands.Add(invocation.RequestId, entry);
                session.CommandBytes += bytes.Length;
                delivery = session.Delivery;
                session.Signal.Writer.TryWrite(0);
            }
        }

        if (delivery != null)
        {
            try
            {
                delivery(entry.Command);
            }
            catch (Exception)
            {
                Close(id);
            }
        }

        var remaining = TimeSpan.FromMilliseconds(Math.Max(0, entry.Command.ExpiresUnixMilliseconds - clock.GetUtcNow().ToUnixTimeMilliseconds()));
        try
        {
            return await entry.Completion.Task.WaitAsync(remaining, clock, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            entry.Completion.TrySetResult(DiagnosticOperationResult.Reject("expired", "Operation result is unknown after the deadline."));
            return await entry.Completion.Task.ConfigureAwait(false);
        }
    }

    /// <summary>Accepts and deduplicates a closed message in the established session.</summary>
    /// <param name="id">The authenticated session.</param>
    /// <param name="message">The message.</param>
    /// <returns>The acceptance receipt.</returns>
    public PublishReceipt Publish(Guid id, DiagnosticMessage message)
    {
        DiagnosticProtocol.Validate(message);
        message = message with
        {
            Result = message.Result == null ? null : message.Result with
            {
                Values = message.Result.Values?.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            },
            Events = message.Events.Select(item => item with
            {
                Fields = item.Fields.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            }).ToArray(),
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message, DiagnosticJson.Context.DiagnosticMessage);
        if (bytes.Length > 4 * 1024 * 1024)
        {
            throw new ArgumentException("Message exceeds the byte budget.", nameof(message));
        }

        message = JsonSerializer.Deserialize(bytes, DiagnosticJson.Context.DiagnosticMessage)!;
        string fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
        lock (_gate)
        {
            Session session = Get(id);
            if (message.SessionId != id)
            {
                throw new UnauthorizedAccessException("Wrong message session.");
            }

            session.LastSeen = clock.GetTimestamp();
            if (session.Messages.TryGetValue(message.MessageId, out (string Fingerprint, PublishReceipt Receipt) previous))
            {
                return previous.Fingerprint == fingerprint ? previous.Receipt : new(message.MessageId, false, "message-id-conflict");
            }

            var receipt = new PublishReceipt(message.MessageId, true);
            if (message.Kind == DiagnosticMessageKind.CommandResult)
            {
                if (!session.Commands.TryGetValue(message.RequestId!.Value, out Entry? entry) || entry.Completion.Task.IsCompleted)
                {
                    receipt = new(message.MessageId, false, "expired");
                }
                else
                {
                    if (entry.Command.ExpiresUnixMilliseconds <= clock.GetUtcNow().ToUnixTimeMilliseconds())
                    {
                        entry.Completion.TrySetResult(DiagnosticOperationResult.Reject("expired", "Operation result is unknown after the deadline."));
                        receipt = new(message.MessageId, false, "expired");
                    }
                    else
                    {
                        entry.Completion.TrySetResult(message.Result!);
                    }
                }
            }
            else
            {
                foreach (DiagnosticEvent item in message.Events)
                {
                    session.Received++;
                    long size = 512 + (item.Name.Length * 2L) + (item.Value.String?.Length * 2L ?? 0)
                        + item.Fields.Sum(pair => 160 + (pair.Key.Length * 2L) + (pair.Value.String?.Length * 2L ?? 0));
                    while (session.Telemetry.Count > 0 && (session.Telemetry.Count >= 1024 || session.TelemetryBytes + size > 4 * 1024 * 1024))
                    {
                        session.TelemetryBytes -= session.Telemetry.Dequeue().Bytes;
                        session.Dropped++;
                    }

                    session.Telemetry.Enqueue((item, size));
                    session.TelemetryBytes += size;
                }
            }

            if (session.Messages.Count == 1024)
            {
                session.Messages.Remove(session.Messages.Keys.First());
            }

            session.Messages.Add(message.MessageId, (fingerprint, receipt));
            return receipt;
        }
    }

    /// <summary>Gets a detached bounded telemetry snapshot.</summary>
    /// <param name="id">The session.</param>
    /// <returns>The retained events.</returns>
    public DiagnosticEvent[] Telemetry(Guid id)
    {
        lock (_gate)
        {
            return JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(Get(id).Telemetry.Select(item => item.Event).ToArray(), DiagnosticJson.Context.DiagnosticEventArray), DiagnosticJson.Context.DiagnosticEventArray)!;
        }
    }

    /// <summary>Closes a session and explicitly rejects pending work.</summary>
    /// <param name="id">The session.</param>
    public void Close(Guid id)
    {
        lock (_gate)
        {
            if (_sessions.Remove(id, out Session? session))
            {
                session.Lifetime.Cancel();
                foreach (Entry entry in session.Commands.Values)
                {
                    entry.Completion.TrySetResult(DiagnosticOperationResult.Reject("target-gone", "Game session closed."));
                }

                session.Signal.Writer.TryComplete();
            }
        }
    }

    internal void CloseAll()
    {
        lock (_gate)
        {
            foreach (Guid id in _sessions.Keys.ToArray())
            {
                Close(id);
            }
        }
    }

    internal void ExpireIdleSessions()
    {
        lock (_gate)
        {
            foreach (Entry entry in _sessions.Values.SelectMany(session => session.Commands.Values))
            {
                if (entry.Command.ExpiresUnixMilliseconds <= clock.GetUtcNow().ToUnixTimeMilliseconds())
                {
                    entry.Completion.TrySetResult(DiagnosticOperationResult.Reject("expired", "Operation result is unknown after the deadline."));
                }
            }

            foreach (Guid id in _sessions.Where(pair => clock.GetElapsedTime(pair.Value.LastSeen) > TimeSpan.FromSeconds(30)).Select(pair => pair.Key).ToArray())
            {
                Close(id);
            }
        }
    }

    private Session Get(Guid id) => _sessions.TryGetValue(id, out Session? session) ? session : throw new KeyNotFoundException("Unknown diagnostic session.");

    private sealed class Session(ClientHello hello, SessionWelcome welcome, Action<DiagnosticCommand>? delivery, long lastSeen)
    {
        private int _polling;

        public ClientHello Hello { get; } = hello;

        public SessionWelcome Welcome { get; } = welcome;

        public Action<DiagnosticCommand>? Delivery { get; } = delivery;

        public Dictionary<Guid, Entry> Commands { get; } = [];

        public Dictionary<Guid, (string Fingerprint, PublishReceipt Receipt)> Messages { get; } = [];

        public Queue<(DiagnosticEvent Event, long Bytes)> Telemetry { get; } = new();

        public Channel<byte> Signal { get; } = Channel.CreateBounded<byte>(1);

        public CancellationTokenSource Lifetime { get; } = new();

        public long LastSeen { get; set; } = lastSeen;

        public long Received { get; set; }

        public long Dropped { get; set; }

        public long TelemetryBytes { get; set; }

        public long CommandBytes { get; set; }

        public bool TryBeginPolling() => Interlocked.CompareExchange(ref _polling, 1, 0) == 0;

        public void EndPolling() => Interlocked.Exchange(ref _polling, 0);
    }

    private sealed class Entry(DiagnosticCommand command, string fingerprint, long retainUntil, long bytes)
    {
        public DiagnosticCommand Command { get; } = command;

        public string Fingerprint { get; } = fingerprint;

        public long RetainUntil { get; } = retainUntil;

        public long Bytes { get; } = bytes;

        public TaskCompletionSource<DiagnosticOperationResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
