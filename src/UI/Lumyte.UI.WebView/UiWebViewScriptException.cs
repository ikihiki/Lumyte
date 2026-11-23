namespace Lumyte.UI.WebView;

/// <summary>A bounded main-document JavaScript evaluation failure.</summary>
public sealed class UiWebViewScriptException(string code, string? scriptMessage = null) : Exception(code)
{
    /// <summary>Gets the non-sensitive failure code.</summary>
    public string Code { get; } = code;

    /// <summary>Gets optional untrusted bounded JavaScript exception text.</summary>
    public string? ScriptMessage { get; } = scriptMessage;
}
