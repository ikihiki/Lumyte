namespace Lumyte.Diagnostics.UI.Automation;

/// <summary>Owns one hidden target reservation until publication or discard completes.</summary>
public sealed class UiAutomationDiagnosticReplacement : IAsyncDisposable
{
    private readonly Func<Guid?, CancellationToken, ValueTask> _quiesce;
    private readonly Func<UiAutomationDiagnosticRegistration> _commit;
    private readonly Func<CancellationToken, ValueTask> _discard;
    private readonly Action _release;
    private readonly Func<bool> _validate;
    private bool _completed;
    private bool _disposed;

    internal UiAutomationDiagnosticReplacement(Guid id, Func<Guid?, CancellationToken, ValueTask> quiesce, Func<UiAutomationDiagnosticRegistration> commit, Func<CancellationToken, ValueTask> discard, Action release, Func<bool> validate)
    {
        CandidateTargetId = id;
        _quiesce = quiesce;
        _commit = commit;
        _discard = discard;
        _release = release;
        _validate = validate;
    }

    /// <summary>Gets the nonreused hidden candidate identity.</summary>
    public Guid CandidateTargetId { get; }

    /// <summary>Pauses admission, drains the trusted cause and cancels competing group jobs.</summary>
    /// <param name="originatingOperation">An operation captured from the same runtime and control group.</param>
    /// <param name="cancellationToken">Cancels this wait, retaining the reservation.</param>
    /// <returns>Completion after old jobs have returned their leases.</returns>
    public ValueTask QuiesceAsync(Guid? originatingOperation, CancellationToken cancellationToken) => _quiesce(originatingOperation, cancellationToken);

    /// <summary>Checks the reservation is still ready on its owner without changing admission.</summary>
    /// <returns>Whether the prepared target may be committed.</returns>
    public bool TryValidateCommit() => !_completed && !_disposed && _validate();

    /// <summary>Publishes the reserved target in the old capacity slot without invoking callbacks.</summary>
    /// <returns>The new registration transferred to the caller.</returns>
    public UiAutomationDiagnosticRegistration Commit()
    {
        UiAutomationDiagnosticRegistration result = _commit();
        _completed = true;
        return result;
    }

    /// <summary>Returns an uncommitted reservation and resumes the same old target when still live.</summary>
    /// <param name="cancellationToken">Cancels only the wait.</param>
    /// <returns>Discard completion after accepted job cleanup.</returns>
    public async ValueTask DiscardAsync(CancellationToken cancellationToken)
    {
        await _discard(cancellationToken);
        _completed = true;
    }

    /// <summary>Returns a completed token; incomplete cleanup remains owned by the caller.</summary>
    /// <returns>The release completion.</returns>
    public ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            throw new InvalidOperationException("Commit or discard the reservation before disposal.");
        }

        if (!_disposed)
        {
            _release();
            _disposed = true;
        }

        return ValueTask.CompletedTask;
    }
}
