namespace Lumyte.Graphics;

/// <summary>
/// Observes completion of explicitly submitted GPU work.
/// </summary>
public sealed class Submission
{
    private readonly IGraphicsDriver _driver;
    private readonly object _handle;

    internal Submission(IGraphicsDriver driver, object handle)
    {
        (_driver, _handle) = (driver, handle);
    }

    /// <summary>
    /// Gets a value indicating whether completion has been observed, releasing leases on completion.
    /// </summary>
    public bool IsCompleted => _driver.IsCompleted(_handle);

    /// <summary>
    /// Polls until completion or cancellation and propagates GPU validation failures.
    /// </summary>
    /// <param name="cancellationToken">Cancels observation without cancelling the submitted GPU work.</param>
    public void Wait(CancellationToken cancellationToken = default) => _driver.Wait(_handle, cancellationToken);

    /// <summary>
    /// Asynchronously polls until completion or cancellation and propagates GPU validation failures.
    /// </summary>
    /// <param name="cancellationToken">Cancels observation without cancelling the submitted GPU work.</param>
    /// <returns>A task that completes when GPU completion is observed or cancellation is requested.</returns>
    public ValueTask WaitAsync(CancellationToken cancellationToken = default) => _driver.WaitAsync(_handle, cancellationToken);
}
