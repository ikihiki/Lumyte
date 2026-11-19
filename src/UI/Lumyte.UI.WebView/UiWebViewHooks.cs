using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.WebView;

/// <summary>Connects function components to an optional window-scoped browser integration.</summary>
public static class UiWebViewHooks
{
    private static readonly UiContextKey<IUiWebViewSessions> _sessions = new();

    /// <summary>Gets a borrowed session during composable function evaluation.</summary>
    /// <param name="key">A nonempty stable key within the owning window.</param>
    /// <param name="initialUri">The immutable initial HTTP(S) address.</param>
    /// <returns>The same window-owned session across rebuilds and component remounts.</returns>
    /// <remarks>Requires a configured provider (for example UseCefWebViews). Components do not dispose
    /// this borrowed session on unmount. Capture the returned session in event callbacks; do not call
    /// hooks from callbacks. Multiple keys do not override the backend's session budget.</remarks>
    public static UiWebViewSession UseWebView(string key, Uri initialUri)
    {
        IUiWebViewSessions sessions;
        try
        {
            sessions = Lumyte.UI.Compose.UseContext(_sessions);
        }
        catch (InvalidOperationException error) when (error.Message == "No value was provided for the requested UI context.")
        {
            throw new InvalidOperationException("UseWebView requires a web view session provider. Configure UseCefWebViews or ProvideWebViewSessions.", error);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        UiWebViewSession.ValidateUri(initialUri);
        return sessions.GetSession(key, initialUri);
    }

    /// <summary>Provides borrowed engine-independent sessions to a fresh standalone declaration subtree.</summary>
    /// <param name="sessions">The provider responsible for session ownership, key identity and owner checks.</param>
    /// <param name="content">The fresh declaration subtree.</param>
    /// <returns>The same declaration with session context.</returns>
    public static Widget ProvideWebViewSessions(IUiWebViewSessions sessions, Widget content)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        return Lumyte.UI.Compose.Provide(_sessions, sessions, content);
    }
}
