using System.Security.Cryptography;
using Lumyte.Diagnostics.UI.Automation.Contracts;

namespace Lumyte.Diagnostics.UI.Automation.Client;

/// <summary>A low-level job handle; cancellation admission and terminal host cleanup are distinct.</summary>
public sealed class UiRemoteJob
{
    private readonly UiAutomationDiagnosticsClient _client;
    private readonly bool _action;
    private readonly UiRemoteControl? _control;
    private bool _inputPossible;

    internal UiRemoteJob(UiAutomationDiagnosticsClient client, Guid id, bool action, UiRemoteControl? control)
    {
        _client = client;
        Id = id;
        _action = action;
        _control = control;
        _inputPossible = action;
    }

    /// <summary>Gets the admitted job identity.</summary>
    public Guid Id { get; }

    /// <summary>Reads a point-in-time status without extending server retention.</summary>
    /// <param name="cancellationToken">Cancels the RPC.</param>
    /// <returns>The status.</returns>
    public async Task<UiRemoteJobStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, DiagnosticValue> values = await CallAsync("get", cancellationToken);
        string state = UiAutomationDiagnosticsClient.Text(values, "state");
        if (state is not ("Queued" or "Running" or "CancelRequested" or "Succeeded" or "Failed" or "Canceled" or "TimedOut")
            || !values.TryGetValue("input-may-have-been-sent", out DiagnosticValue inputValue) || inputValue.Kind != DiagnosticValueKind.Boolean)
        {
            throw new UiRemoteAutomationException("invalid-response", Id, _inputPossible);
        }

        bool input = inputValue.Boolean;
        if (Terminal(state))
        {
            _control?.Completed();
        }

        _inputPossible = (!Terminal(state) && _action) || input;
        return new(Id, state, UiAutomationDiagnosticsClient.Text(values, "code"), input, OptionalId(values, "result-artifact-id"), OptionalId(values, "trace-artifact-id"), UiAutomationDiagnosticsClient.Number(values, "revision"));
    }

    /// <summary>Polls at most four times a second until terminal host cleanup; does not resend input.</summary>
    /// <param name="cancellationToken">Cancels waiting and attempts bounded cancellation of this same job.</param>
    /// <returns>The terminal status.</returns>
    public async Task<UiRemoteJobStatus> WaitAsync(CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_client.Options.OperationTimeout);
        try
        {
            while (true)
            {
                _control?.ThrowIfUnavailable();
                UiRemoteJobStatus status = await GetStatusAsync(deadline.Token);
                if (Terminal(status.State))
                {
                    return status;
                }

                await Task.Delay(_client.Options.PollInterval, deadline.Token);
            }
        }
        catch
        {
            using var cleanup = new CancellationTokenSource(_client.Options.CleanupTimeout);
            try
            {
                await CancelAsync(cleanup.Token);
            }
            catch
            {
                // Cancellation could not be confirmed; never turn this into a new input job.
            }

            throw;
        }
    }

    /// <summary>Requests cancellation; use WaitAsync to confirm terminal cleanup.</summary>
    /// <param name="cancellationToken">Cancels the RPC.</param>
    /// <returns>Cancellation admission.</returns>
    public async Task CancelAsync(CancellationToken cancellationToken = default) => await CallAsync("cancel", cancellationToken);

    /// <summary>Downloads fixed bytes in bounded chunks and validates identity, offsets, length and SHA-256.</summary>
    /// <param name="artifactId">The artifact belonging to this job.</param>
    /// <param name="cancellationToken">Cancels downloading.</param>
    /// <returns>Verified caller-owned bytes.</returns>
    public async Task<byte[]> DownloadArtifactAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_client.Options.OperationTimeout);
        using var output = new MemoryStream();
        long? total = null;
        string? hash = null;
        string? contentType = null;
        do
        {
            Dictionary<string, DiagnosticValue> args = new()
            {
                ["job-id"] = DiagnosticValue.From(Id.ToString("D")),
                ["artifact-id"] = DiagnosticValue.From(artifactId.ToString("D")),
                ["offset"] = DiagnosticValue.From(output.Length),
                ["count"] = DiagnosticValue.From(65536L),
            };
            IReadOnlyDictionary<string, DiagnosticValue> values;
            try
            {
                values = await _client.InvokeAsync("artifact-read", args, null, deadline.Token);
            }
            catch (UiRemoteAutomationException failure) when (failure.Code == "busy")
            {
                await Task.Delay(_client.Options.PollInterval, deadline.Token);
                continue;
            }

            long length = UiAutomationDiagnosticsClient.Number(values, "total-bytes");
            string digest = UiAutomationDiagnosticsClient.Text(values, "sha256");
            string type = UiAutomationDiagnosticsClient.Text(values, "content-type");
            if (UiAutomationDiagnosticsClient.Text(values, "artifact-id") != artifactId.ToString("D") || UiAutomationDiagnosticsClient.Number(values, "offset") != output.Length
                || length < 0 || length > 8 * 1024 * 1024 || (total is not null && total != length) || (hash is not null && hash != digest) || (contentType is not null && contentType != type))
            {
                throw new UiRemoteAutomationException("invalid-artifact", Id, _inputPossible);
            }

            total = length;
            hash = digest;
            contentType = type;
            byte[] bytes = Convert.FromBase64String(UiAutomationDiagnosticsClient.Text(values, "base64"));
            if (bytes.LongLength != UiAutomationDiagnosticsClient.Number(values, "bytes") || bytes.Length > 65536 || bytes.LongLength > length - output.Length || (bytes.Length == 0 && output.Length != length))
            {
                throw new UiRemoteAutomationException("invalid-artifact", Id, _inputPossible);
            }

            output.Write(bytes);
        }
        while (total is null || output.Length < total);

        byte[] result = output.ToArray();
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(result)), hash, StringComparison.OrdinalIgnoreCase))
        {
            throw new UiRemoteAutomationException("artifact-hash-mismatch", Id, _inputPossible);
        }

        return result;
    }

    /// <summary>Returns terminal job and artifact capacity; does not reset the sequence high-water mark.</summary>
    /// <param name="cancellationToken">Cancels the RPC.</param>
    /// <returns>Forget confirmation.</returns>
    public async Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        await CallAsync("forget", cancellationToken);
        _control?.Completed();
    }

    internal static bool Terminal(string state) => state is "Succeeded" or "Failed" or "Canceled" or "TimedOut";

    private static Guid? OptionalId(IReadOnlyDictionary<string, DiagnosticValue> values, string key) => UiAutomationDiagnosticsClient.Text(values, key) is { Length: > 0 } id ? Guid.Parse(id) : null;

    private async Task<IReadOnlyDictionary<string, DiagnosticValue>> CallAsync(string action, CancellationToken token)
    {
        Dictionary<string, DiagnosticValue> args = new() { ["job-id"] = DiagnosticValue.From(Id.ToString("D")) };
        try
        {
            return await _client.InvokeAsync((_action ? "action-" : "observe-") + action, args, null, token);
        }
        catch (UiRemoteAutomationException failure) when (failure.Code is "not-found" or "transport-error")
        {
            throw new UiRemoteAutomationException("outcome-unknown", Id, _inputPossible, failure);
        }
    }
}
