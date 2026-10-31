namespace Lumyte.Diagnostics.UI.Automation;

/// <summary>Owns diagnostic registration, never the borrowed UI or host.</summary>
public sealed class UiAutomationDiagnosticRegistration : IDisposable, IAsyncDisposable
{
    private readonly Action _release;

    private readonly TaskCompletionSource _released = new();

    internal UiAutomationDiagnosticRegistration(Guid id, Action release)
    {
        TargetId = id;
        _release = release;
    }

    /// <summary>Gets the unique registration identity.</summary>
    public Guid TargetId { get; }

    /// <summary>Gets a value indicating whether cancellation and host cleanup have completed.</summary>
    public bool IsReleased
    {
        get => _released.Task.IsCompleted;
        internal set
        {
            if (value)
            {
                _released.TrySetResult();
            }
        }
    }

    /// <summary>Stops admission and requests cancellation; Advance completes release.</summary>
    public void Dispose() => _release();

    /// <summary>Requests release and waits for runtime Advance to finish host cleanup.</summary>
    /// <returns>Completion after all jobs have drained, suitable for popup BindLifetime.</returns>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return new(_released.Task);
    }
}
