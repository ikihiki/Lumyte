namespace Lumyte.Diagnostics.UI.Automation;

/// <summary>Owns diagnostic registration, never the borrowed UI or host.</summary>
public sealed class UiAutomationDiagnosticRegistration : IDisposable
{
    private readonly Action _release;

    internal UiAutomationDiagnosticRegistration(Guid id, Action release)
    {
        TargetId = id;
        _release = release;
    }

    /// <summary>Gets the unique registration identity.</summary>
    public Guid TargetId { get; }

    /// <summary>Gets a value indicating whether cancellation and host cleanup have completed.</summary>
    public bool IsReleased { get; internal set; }

    /// <summary>Stops admission and requests cancellation; Advance completes release.</summary>
    public void Dispose() => _release();
}
