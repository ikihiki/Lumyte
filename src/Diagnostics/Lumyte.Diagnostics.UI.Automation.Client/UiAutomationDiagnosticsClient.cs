using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Lumyte.Diagnostics.Transport;
using Lumyte.Diagnostics.UI.Automation.Contracts;

namespace Lumyte.Diagnostics.UI.Automation.Client;

/// <summary>A typed operator client using the existing authenticated diagnostic service.</summary>
public sealed partial class UiAutomationDiagnosticsClient : IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly Guid _session;
    private readonly HashSet<UiRemoteControl> _controls = [];
    private readonly SemaphoreSlim _catalogGate = new(1, 1);
    private bool _catalogChecked;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="UiAutomationDiagnosticsClient"/> class.</summary>
    /// <param name="http">A borrowed HttpClient with operator authentication and BaseAddress.</param>
    /// <param name="sessionId">The game session, never a game enrollment credential.</param>
    /// <param name="options">Optional bounded deadlines.</param>
    public UiAutomationDiagnosticsClient(HttpClient http, Guid sessionId, UiAutomationDiagnosticsClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        Options = options ?? new();
        if (http.BaseAddress is null || sessionId == Guid.Empty || Options.JobTimeoutMilliseconds is < 100 or > 10000 || Options.RpcTimeoutMilliseconds is < 100 or > 30000
            || Options.OperationTimeout <= TimeSpan.Zero || Options.OperationTimeout.TotalMilliseconds > uint.MaxValue - 1
            || Options.CleanupTimeout <= TimeSpan.Zero || Options.CleanupTimeout.TotalMilliseconds > uint.MaxValue - 1 || Options.PollInterval < TimeSpan.FromMilliseconds(250) || Options.PollInterval > Options.OperationTimeout)
        {
            throw new ArgumentException("Invalid client address, session or deadlines.");
        }

        _http = http;
        _session = sessionId;
    }

    internal UiAutomationDiagnosticsClientOptions Options { get; }

    /// <summary>Discovers explicitly registered surfaces after validating the operation catalog.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Detached target descriptions.</returns>
    public async Task<IReadOnlyList<UiRemoteTarget>> ListTargetsAsync(CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Options.OperationTimeout);
        IReadOnlyDictionary<string, DiagnosticValue> values = await InvokeAsync("targets", [], null, deadline.Token);
        return JsonSerializer.Deserialize(Text(values, "json"), UiAutomationJsonContext.Default.UiRemoteTargetArray) ?? throw new UiRemoteAutomationException("invalid-response");
    }

    /// <summary>Creates an observation-only page; its input methods fail before transmission.</summary>
    /// <param name="targetId">The current registration identity.</param>
    /// <returns>A remote page.</returns>
    public UiRemotePage Page(Guid targetId) => new(this, targetId, null);

    /// <summary>Reopens a retained job handle in the same authenticated session to recover status and trace artifacts.</summary>
    /// <param name="jobId">The known admitted job identity.</param>
    /// <param name="inputJob">Whether the job was an input action.</param>
    /// <returns>A low-level retained job handle.</returns>
    public UiRemoteJob Job(Guid jobId, bool inputJob) => new(this, jobId, inputJob, null);

    /// <summary>Acquires a five-second control lease and starts bounded renewal.</summary>
    /// <param name="targetId">The current registration identity.</param>
    /// <param name="cancellationToken">Cancels acquisition.</param>
    /// <returns>A control whose page may send input.</returns>
    public async Task<UiRemoteControl> AcquireControlAsync(Guid targetId, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Options.OperationTimeout);
        Dictionary<string, DiagnosticValue> arguments = new() { ["target-id"] = DiagnosticValue.From(targetId.ToString("D")), ["duration-ms"] = DiagnosticValue.From(5000L) };
        IReadOnlyDictionary<string, DiagnosticValue> receipt = await InvokeAsync("control-acquire", arguments, null, deadline.Token);
        var control = new UiRemoteControl(this, targetId, Guid.Parse(Text(receipt, "control-id")), Number(receipt, "next-sequence"));
        lock (_controls)
        {
            if (_disposed || _controls.Any(current => current.Id == control.Id))
            {
                control.StopRenewal();
                throw new InvalidOperationException("This client already owns that control or is closing.");
            }

            _controls.Add(control);
        }

        return control;
    }

    /// <summary>Stops all renewals and attempts bounded release, without disposing HttpClient or the game connection.</summary>
    /// <returns>Completion after all controls were given a cleanup attempt.</returns>
    public async ValueTask DisposeAsync()
    {
        UiRemoteControl[] controls;
        lock (_controls)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            controls = _controls.ToArray();
        }

        List<Exception>? failures = null;
        foreach (UiRemoteControl control in controls)
        {
            try
            {
                await control.DisposeAsync();
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("Remote control cleanup was uncertain.", failures);
        }
    }

    internal static string Text(IReadOnlyDictionary<string, DiagnosticValue> values, string key) => values.TryGetValue(key, out DiagnosticValue value) && value.Kind == DiagnosticValueKind.String && value.String is not null ? value.String : throw new UiRemoteAutomationException("invalid-response");

    internal static long Number(IReadOnlyDictionary<string, DiagnosticValue> values, string key) => values.TryGetValue(key, out DiagnosticValue value) && value.Kind == DiagnosticValueKind.Int64 ? value.Int64 : throw new UiRemoteAutomationException("invalid-response");

    internal void Released(UiRemoteControl control)
    {
        lock (_controls)
        {
            _controls.Remove(control);
        }
    }

    internal async Task<IReadOnlyDictionary<string, DiagnosticValue>> InvokeAsync(string operation, Dictionary<string, DiagnosticValue> arguments, long? revision, CancellationToken token, bool maySendInput = false)
    {
        if (!operation.StartsWith("control-", StringComparison.Ordinal))
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        await CheckCatalogAsync(token);
        int timeout = operation is "action-get" or "observe-get" ? Math.Min(1000, Options.RpcTimeoutMilliseconds) : Options.RpcTimeoutMilliseconds;
        var invocation = new OperationInvocation(Guid.NewGuid(), "ui.automation", operation, arguments, revision, timeout);
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(invocation, DiagnosticJson.Context.OperationInvocation);
        for (int attempt = 0; ; attempt++)
        {
            using var rpc = CancellationTokenSource.CreateLinkedTokenSource(token);
            rpc.CancelAfter(TimeSpan.FromMilliseconds(timeout));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"diagnostics/v1/sessions/{_session:D}/operations");
                request.Content = new ByteArrayContent(body);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, rpc.Token);
                response.EnsureSuccessStatusCode();
                DiagnosticOperationResult receipt = await ReadAsync(response, DiagnosticJson.Context.DiagnosticOperationResult, rpc.Token);
                if (receipt.Status != "success" || receipt.Values is null)
                {
                    throw new UiRemoteAutomationException(receipt.Status == "conflict" ? "revision-conflict" : receipt.Code ?? "invalid-response", inputMayHaveBeenSent: maySendInput && (attempt != 0 || receipt.Code is "action-forgotten" or "expired"));
                }

                return receipt.Values;
            }
            catch (Exception failure) when (failure is HttpRequestException or OperationCanceledException or JsonException)
            {
                if (token.IsCancellationRequested)
                {
                    if (maySendInput)
                    {
                        throw new UiRemoteAutomationException("outcome-unknown", inputMayHaveBeenSent: true, inner: failure);
                    }

                    token.ThrowIfCancellationRequested();
                }

                if (attempt != 0)
                {
                    throw new UiRemoteAutomationException(maySendInput ? "outcome-unknown" : "transport-error", inputMayHaveBeenSent: maySendInput, inner: failure);
                }

                // Same bytes, RequestId, plan, sequence and RPC deadline: never create a replacement action.
            }
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> type, CancellationToken token)
    {
        const int Limit = 4 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > Limit)
        {
            throw new UiRemoteAutomationException("result-too-large");
        }

        using Stream stream = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        byte[] buffer = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) != 0)
        {
            if (output.Length + read > Limit)
            {
                throw new UiRemoteAutomationException("result-too-large");
            }

            output.Write(buffer, 0, read);
        }

        return JsonSerializer.Deserialize(output.ToArray(), type) ?? throw new UiRemoteAutomationException("invalid-response");
    }

    private async Task CheckCatalogAsync(CancellationToken token)
    {
        await _catalogGate.WaitAsync(token);
        try
        {
            if (_catalogChecked)
            {
                return;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(Options.RpcTimeoutMilliseconds));
            using HttpResponseMessage response = await _http.GetAsync("diagnostics/v1/sessions", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            SessionSnapshot[] sessions = await ReadAsync(response, DiagnosticJson.Context.SessionSnapshotArray, timeout.Token);
            DiagnosticSubsystemCatalog? catalog = sessions.SingleOrDefault(session => session.SessionId == _session)?.Catalog.SingleOrDefault(entry => entry.Subsystem.Id == "ui.automation");
            if (!Supports(catalog))
            {
                throw new UiRemoteAutomationException("unsupported-catalog");
            }

            _catalogChecked = true;
        }
        finally
        {
            _catalogGate.Release();
        }
    }
}
