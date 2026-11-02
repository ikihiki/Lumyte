namespace Lumyte.UI.Application;

/// <summary>The application lifecycle, independently of individual window visibility.</summary>
public enum UiApplicationState
{
    /// <summary>Configuration is frozen; no native resources exist.</summary>
    Created,

    /// <summary>The owner and initial presentation are being prepared.</summary>
    Starting,

    /// <summary>Input and frames are advancing.</summary>
    Running,

    /// <summary>The graphics generation is being replaced.</summary>
    Recovering,

    /// <summary>Input and content are draining.</summary>
    Stopping,

    /// <summary>A retryable cleanup stage still owns resources.</summary>
    StopFailed,

    /// <summary>Normal shutdown completed.</summary>
    Stopped,

    /// <summary>Startup, execution or final release failed.</summary>
    Failed,
}
