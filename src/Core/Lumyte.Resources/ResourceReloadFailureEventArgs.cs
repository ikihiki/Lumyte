namespace Lumyte.Resources;

/// <summary>Reports an automatic reload failure while preserving the previous graph.</summary>
/// <param name="exception">The exception raised while rebuilding.</param>
/// <param name="attempt">The one-based attempt number within the current batch.</param>
/// <param name="willRetry">Whether the service will retry this batch.</param>
public sealed class ResourceReloadFailureEventArgs(Exception exception, int attempt, bool willRetry) : EventArgs
{
    /// <summary>Gets the exception raised while rebuilding.</summary>
    public Exception Error { get; } = exception;

    /// <summary>Gets the one-based attempt number.</summary>
    public int Attempt { get; } = attempt;

    /// <summary>Gets a value indicating whether the batch will be retried.</summary>
    public bool WillRetry { get; } = willRetry;
}
