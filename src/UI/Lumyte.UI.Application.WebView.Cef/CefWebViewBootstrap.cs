namespace Lumyte.UI.Application.WebView.Cef;

/// <summary>Compatibility shim. The UI process never loads CEF.</summary>
public static class CefWebViewBootstrap
{
    /// <summary>Returns false; bootstrap belongs to the optional helper.</summary>
    /// <param name="args">Application arguments.</param>
    /// <param name="exitCode">Always zero.</param>
    /// <returns>Always false.</returns>
    public static bool TryRunSubprocess(string[] args, out int exitCode)
    {
        exitCode = 0;
        return false;
    }
}
