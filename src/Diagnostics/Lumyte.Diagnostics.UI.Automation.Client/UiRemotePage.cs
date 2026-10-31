using Lumyte.Diagnostics.UI.Automation.Contracts;

namespace Lumyte.Diagnostics.UI.Automation.Client;

/// <summary>A lazy remote target view; observation pages cannot send input.</summary>
public sealed class UiRemotePage
{
    private readonly UiAutomationDiagnosticsClient _client;
    private readonly UiRemoteControl? _control;

    internal UiRemotePage(UiAutomationDiagnosticsClient client, Guid targetId, UiRemoteControl? control)
    {
        if (targetId == Guid.Empty)
        {
            throw new ArgumentException("A target registration identity is required.", nameof(targetId));
        }

        _client = client;
        _control = control;
        TargetId = targetId;
    }

    internal Guid TargetId { get; }

    /// <summary>Creates an exact identity locator, including hidden elements.</summary>
    /// <param name="testId">A nonempty identity up to 256 characters.</param>
    /// <returns>A lazy locator.</returns>
    public UiRemoteLocator GetByTestId(string testId) => new(this, [Step(testId)]);

    /// <summary>Creates an exact semantic role/name locator.</summary>
    /// <param name="role">button or group.</param>
    /// <param name="name">Optional exact name.</param>
    /// <param name="includeHidden">Whether hidden elements match.</param>
    /// <returns>A lazy locator.</returns>
    public UiRemoteLocator GetByRole(string role, string? name = null, bool includeHidden = false) => new(this, [Step(role, name, includeHidden)]);

    /// <summary>Creates assertions for this page's locator.</summary>
    /// <param name="locator">A locator owned by this page.</param>
    /// <returns>The remote assertion entry point.</returns>
    public UiRemoteExpect Expect(UiRemoteLocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        if (!ReferenceEquals(locator.Page, this))
        {
            throw new ArgumentException("The locator belongs to another page.", nameof(locator));
        }

        return new(locator);
    }

    /// <summary>Admits an observation-only plan and returns a retained low-level job.</summary>
    /// <param name="observation">A snapshot/count/expect/screenshot plan.</param>
    /// <param name="cancellationToken">Cancels admission.</param>
    /// <returns>The admitted job.</returns>
    public async Task<UiRemoteJob> StartObservationAsync(UiRemotePlan observation, CancellationToken cancellationToken = default)
    {
        string json = UiRemoteSchema.SerializePlan(observation);
        if (UiRemoteSchema.IsAction(UiRemoteSchema.ParsePlan(json)))
        {
            throw new ArgumentException("Observation cannot contain input.", nameof(observation));
        }

        Dictionary<string, DiagnosticValue> args = new()
        {
            ["target-id"] = DiagnosticValue.From(TargetId.ToString("D")),
            ["plan-json"] = DiagnosticValue.From(json),
            ["job-timeout-ms"] = DiagnosticValue.From((long)_client.Options.JobTimeoutMilliseconds),
        };
        IReadOnlyDictionary<string, DiagnosticValue> receipt = await _client.InvokeAsync("observe-start", args, null, cancellationToken);
        return new(_client, Guid.Parse(UiAutomationDiagnosticsClient.Text(receipt, "job-id")), false, null);
    }

    /// <summary>Captures and verifies actual PNG bytes, then returns the server job capacity.</summary>
    /// <param name="cancellationToken">Cancels this high-level operation.</param>
    /// <returns>Verified PNG bytes.</returns>
    public Task<byte[]> ScreenshotAsync(CancellationToken cancellationToken = default) => ExecuteAsync(new(1, "screenshot", []), cancellationToken);

    internal static UiRemoteLocatorStep Step(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Length > 256)
        {
            throw new ArgumentException("Identity is too long.", nameof(id));
        }

        return new("test-id", Value: id);
    }

    internal static UiRemoteLocatorStep Step(string role, string? name, bool hidden)
    {
        if (role is not ("button" or "group") || name?.Length > 256)
        {
            throw new ArgumentException("Unsupported role or name.", nameof(role));
        }

        return new("role", Role: role, Name: name, IncludeHidden: hidden);
    }

    internal async Task<byte[]> ExecuteAsync(UiRemotePlan plan, CancellationToken token)
    {
        if (UiRemoteSchema.IsAction(plan) && _control is null)
        {
            throw new InvalidOperationException("Use a control page for input.");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(_client.Options.OperationTimeout);
        UiRemoteJob job = UiRemoteSchema.IsAction(plan) ? await _control!.StartTargetAsync(TargetId, plan, deadline.Token) : await StartObservationAsync(plan, deadline.Token);
        UiRemoteJobStatus status = await job.WaitAsync(deadline.Token);
        if (status.State != "Succeeded")
        {
            throw new UiRemoteAutomationException(status.Code, job.Id, status.InputMayHaveBeenSent);
        }

        byte[] result = [];
        if (status.ResultArtifactId is { } id)
        {
            result = await job.DownloadArtifactAsync(id, deadline.Token);
        }
        else if (plan.Kind is "count" or "snapshot" or "screenshot")
        {
            throw new UiRemoteAutomationException("invalid-response", job.Id, status.InputMayHaveBeenSent);
        }

        await job.ReleaseAsync(deadline.Token);
        return result;
    }
}
