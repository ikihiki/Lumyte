using Lumyte.Diagnostics.UI.Automation.Contracts;

namespace Lumyte.Diagnostics.UI.Automation.Client;

/// <summary>Owns remote input authority and renewal; independent logical input operations are never reordered.</summary>
public sealed class UiRemoteControl : IAsyncDisposable
{
    private readonly UiAutomationDiagnosticsClient _client;

    private readonly CancellationTokenSource _renewalStop = new();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Task _heartbeat;
    private long _sequence;
    private int _active;
    private int _disposed;
    private Exception? _renewalFailure;
    private bool _uncertain;

    internal UiRemoteControl(UiAutomationDiagnosticsClient client, Guid target, Guid id, long nextSequence)
    {
        _client = client;
        Id = id;
        _sequence = nextSequence;
        Page = new(client, target, this);
        _heartbeat = RenewAsync();
    }

    /// <summary>Gets the control identity.</summary>
    public Guid Id { get; }

    /// <summary>Gets the page allowed to submit input plans.</summary>
    public UiRemotePage Page { get; }

    /// <summary>Submits one logical input plan, with a stable sequence and no replacement action on uncertain delivery.</summary>
    /// <param name="action">A validated input-only plan.</param>
    /// <param name="cancellationToken">Cancels submission.</param>
    /// <returns>A low-level job; observe terminal status and ReleaseAsync its retained result.</returns>
    public Task<UiRemoteJob> StartAsync(UiRemotePlan action, CancellationToken cancellationToken = default) => StartTargetAsync(Page.TargetId, action, cancellationToken);

    /// <summary>Creates a page sharing this control's heartbeat and sequence; the server enforces live group membership on every action.</summary>
    /// <param name="targetId">A target in this control's root group.</param>
    /// <returns>A lazy page using this control's input sequence.</returns>
    public UiRemotePage PageFor(Guid targetId)
    {
        ThrowIfUnavailable();
        return new(_client, targetId, this);
    }

    /// <summary>Stops renewal, reconciles revision, requests release and waits a bounded time for host cleanup.</summary>
    /// <returns>Release confirmation, or an explicit uncertain outcome on communication loss.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _renewalStop.Cancel();
        using var cleanup = new CancellationTokenSource(_client.Options.CleanupTimeout);
        bool acquired = false;
        try
        {
            await _heartbeat.WaitAsync(cleanup.Token);
            await _gate.WaitAsync(cleanup.Token);
            acquired = true;
            Dictionary<string, DiagnosticValue> args = new() { ["control-id"] = DiagnosticValue.From(Id.ToString("D")) };
            IReadOnlyDictionary<string, DiagnosticValue> current = await _client.InvokeAsync("control-get", args, null, cleanup.Token);
            if (UiAutomationDiagnosticsClient.Text(current, "state") != "Released")
            {
                try
                {
                    current = await _client.InvokeAsync("control-release", args, UiAutomationDiagnosticsClient.Number(current, "revision"), cleanup.Token);
                }
                catch (UiRemoteAutomationException failure) when (failure.Code == "revision-conflict")
                {
                    current = await _client.InvokeAsync("control-get", args, null, cleanup.Token);
                    current = await _client.InvokeAsync("control-release", args, UiAutomationDiagnosticsClient.Number(current, "revision"), cleanup.Token);
                }
            }

            while (UiAutomationDiagnosticsClient.Text(current, "state") != "Released")
            {
                await Task.Delay(_client.Options.PollInterval, cleanup.Token);
                current = await _client.InvokeAsync("control-get", args, null, cleanup.Token);
            }
        }
        catch (UiRemoteAutomationException failure) when (failure.Code == "not-found")
        {
            // The runtime removes a control only after its host cleanup has completed.
        }
        catch (Exception failure)
        {
            throw new UiRemoteAutomationException("outcome-unknown", inputMayHaveBeenSent: Volatile.Read(ref _active) != 0 || _uncertain, inner: failure);
        }
        finally
        {
            if (acquired)
            {
                _gate.Release();
            }

            _client.Released(this);
        }
    }

    internal async Task<UiRemoteJob> StartTargetAsync(Guid targetId, UiRemotePlan action, CancellationToken cancellationToken)
    {
        string plan = UiRemoteSchema.SerializePlan(action);
        if (!UiRemoteSchema.IsAction(UiRemoteSchema.ParsePlan(plan)))
        {
            throw new ArgumentException("The control accepts only input plans.", nameof(action));
        }

        ThrowIfUnavailable();
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
        {
            throw new InvalidOperationException("A control cannot run parallel input operations.");
        }

        bool acquired = false;
        try
        {
            await _gate.WaitAsync(cancellationToken);
            acquired = true;
            ThrowIfUnavailable();
            Dictionary<string, DiagnosticValue> args = new()
            {
                ["target-id"] = DiagnosticValue.From(targetId.ToString("D")),
                ["control-id"] = DiagnosticValue.From(Id.ToString("D")),
                ["sequence"] = DiagnosticValue.From(_sequence),
                ["plan-json"] = DiagnosticValue.From(plan),
                ["job-timeout-ms"] = DiagnosticValue.From((long)_client.Options.JobTimeoutMilliseconds),
            };
            IReadOnlyDictionary<string, DiagnosticValue> receipt;
            try
            {
                receipt = await _client.InvokeAsync("action-start", args, null, cancellationToken, maySendInput: true);
            }
            catch (UiRemoteAutomationException failure)
            {
                _uncertain = failure.InputMayHaveBeenSent || failure.Code == "invalid-response";
                throw;
            }
            catch
            {
                _uncertain = true;
                throw;
            }

            UiRemoteJob job;
            try
            {
                job = new(_client, Guid.ParseExact(UiAutomationDiagnosticsClient.Text(receipt, "job-id"), "D"), true, this);
            }
            catch (Exception failure)
            {
                _uncertain = true;
                throw new UiRemoteAutomationException("outcome-unknown", inputMayHaveBeenSent: true, inner: failure);
            }

            _sequence = checked(_sequence + 1);
            return job;
        }
        catch
        {
            if (!_uncertain)
            {
                Interlocked.Exchange(ref _active, 0);
            }

            throw;
        }
        finally
        {
            if (acquired)
            {
                _gate.Release();
            }
        }
    }

    internal void StopRenewal() => _renewalStop.Cancel();

    internal void Completed() => Interlocked.Exchange(ref _active, 0);

    internal void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_uncertain || _renewalFailure is not null)
        {
            throw new UiRemoteAutomationException("outcome-unknown", inputMayHaveBeenSent: true, inner: _renewalFailure);
        }
    }

    private async Task RenewAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), _renewalStop.Token);
                await _gate.WaitAsync(_renewalStop.Token);
                try
                {
                    Dictionary<string, DiagnosticValue> args = new() { ["control-id"] = DiagnosticValue.From(Id.ToString("D")) };
                    IReadOnlyDictionary<string, DiagnosticValue> current = await _client.InvokeAsync("control-get", args, null, _renewalStop.Token);
                    if (UiAutomationDiagnosticsClient.Text(current, "state") != "Active")
                    {
                        throw new UiRemoteAutomationException("lease-gone");
                    }

                    args["duration-ms"] = DiagnosticValue.From(5000L);
                    await _client.InvokeAsync("control-renew", args, UiAutomationDiagnosticsClient.Number(current, "revision"), _renewalStop.Token);
                }
                finally
                {
                    _gate.Release();
                }
            }
        }
        catch (OperationCanceledException) when (_renewalStop.IsCancellationRequested)
        {
        }
        catch (Exception failure)
        {
            _renewalFailure = failure;
        }
    }
}
