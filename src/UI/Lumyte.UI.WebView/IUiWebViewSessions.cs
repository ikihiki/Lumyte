namespace Lumyte.UI.WebView;

/// <summary>Resolves borrowed sessions in one owner-checked lifetime, independently of the browser engine.</summary>
public interface IUiWebViewSessions
{
    /// <summary>Gets the session for a nonempty stable key, refusing a changed initial HTTP(S) address.</summary>
    /// <param name="key">The stable, lifetime-local session identity.</param>
    /// <param name="initialUri">The immutable initial address; rebuilding never requests navigation.</param>
    /// <returns>The borrowed session. Its provider, not the component, owns its lifetime.</returns>
    UiWebViewSession GetSession(string key, Uri initialUri);
}
