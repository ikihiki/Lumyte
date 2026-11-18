namespace Lumyte.UI.WebView;

/// <summary>Untrusted JSON posted by an explicitly allowed main document.</summary>
public sealed class UiWebMessageReceivedEventArgs(Uri source, string json) : EventArgs
{
    /// <summary>Gets the actual engine-reported main-document source.</summary>
    public Uri Source { get; } = source;

    /// <summary>Gets untrusted JSON, never a host-object invocation.</summary>
    public string Json { get; } = json;

    /// <summary>Gets a value indicating whether the sender is the main frame. Always true.</summary>
    public bool IsMainFrame => true;
}
