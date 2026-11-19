namespace Lumyte.UI.WebView;

/// <summary>The browser lifetime, independent of document loading.</summary>
public enum UiWebViewState
{
    /// <summary>Waiting for browser creation.</summary>
    Creating,

    /// <summary>Accepting commands.</summary>
    Ready,

    /// <summary>Rejecting work while native close completes.</summary>
    Closing,

    /// <summary>Native close and resource retirement completed.</summary>
    Closed,

    /// <summary>Creation or renderer failed.</summary>
    Faulted,
}
