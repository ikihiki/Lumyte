using Lumyte.UI.WebView.Cef;

namespace Lumyte.UI.Application.WebView.Cef;

/// <summary>Handles CEF child processes before constructing the UI application.</summary>
public static class CefWebViewBootstrap
{
    /// <summary>Runs a native CEF subprocess with sandbox enabled; never falls back to no-sandbox.</summary>
    /// <param name="args">The process arguments.</param>
    /// <param name="exitCode">The child exit code.</param>
    /// <returns>Whether the caller must exit immediately.</returns>
    public static bool TryRunSubprocess(string[] args, out int exitCode) => CefEngineRuntime.TryRunSubprocess(args, out exitCode);
}
