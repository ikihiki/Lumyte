using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class QueueSubmission : IGraphicsSubmission
{
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public SubmissionStatus Status { get; private set; } = SubmissionStatus.Pending;

    public int DisposeCount { get; private set; }

    public ValueTask WaitAsync(CancellationToken cancellationToken = default) => new(_completion.Task.WaitAsync(cancellationToken));

    public void Complete()
    {
        Status = SubmissionStatus.Completed;
        _completion.SetResult();
    }

    public void Dispose()
    {
        DisposeCount++;
        Status = SubmissionStatus.Disposed;
    }
}
