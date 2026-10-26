namespace Lumyte.Execution;

/// <summary>Treatment of accepted work during shutdown.</summary>
public enum ShutdownMode
{
    /// <summary>Drain.</summary>
    Drain,

    /// <summary>CancelPending.</summary>
    CancelPending,
}
