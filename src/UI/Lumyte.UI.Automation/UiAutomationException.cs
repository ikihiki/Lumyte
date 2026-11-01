namespace Lumyte.UI.Automation;

/// <summary>Reports a classified failure with a detached last snapshot and at most 64 trace entries.</summary>
public sealed class UiAutomationException : Exception
{
    internal UiAutomationException(UiAutomationFailureKind kind, string message, UiAutomationSnapshot? snapshot, string[] trace, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        LastSnapshot = snapshot;
        Trace = Array.AsReadOnly(trace);
    }

    /// <summary>Gets the failure category.</summary>
    public UiAutomationFailureKind Kind { get; }

    /// <summary>Gets the last captured state, if capture succeeded.</summary>
    public UiAutomationSnapshot? LastSnapshot { get; }

    /// <summary>Gets bounded diagnostics including criteria, wait reasons, frames and sent input.</summary>
    public IReadOnlyList<string> Trace { get; }
}
