namespace Lumyte.Graphics.Abstractions;

/// <summary>Owns completion tracking without implicitly waiting during disposal.</summary>
/// <remarks>The caller confirms completion before disposal; disposal does not query outstanding GPU use.</remarks>
public interface IGraphicsSubmission : IDisposable
{
    /// <summary>Gets the nonblocking completion status.</summary>
    SubmissionStatus Status { get; }

    /// <summary>Waits for this submission; cancellation only cancels the wait.</summary>
    /// <param name="cancellationToken">The cancellationToken value.</param>
    /// <returns>Completion of the explicit wait.</returns>
    ValueTask WaitAsync(CancellationToken cancellationToken = default);
}
