namespace Lumyte.Resources;

/// <summary>Runs one session-owned reader operation inside the resource CPU scheduler.</summary>
/// <remarks>
/// The executor calls this operation exactly once after admission and observes its actual completion.
/// Normal awaits preserve the scheduler context; detached work and ConfigureAwait(false) are outside that contract.
/// An operation may remain suspended while waiting for I/O or buffer space without retaining a CPU worker.
/// </remarks>
public interface IResourceStreamWork
{
    /// <summary>Performs the reader operation, including any asynchronous continuations.</summary>
    /// <param name="cancellationToken">Cancellation before entry and cooperative cancellation thereafter.</param>
    /// <returns>Completion of the actual reader operation.</returns>
    ValueTask ExecuteAsync(CancellationToken cancellationToken);
}
