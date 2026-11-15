using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Benchmarks.Testing;

internal sealed class CpuSubmission(CpuGraphicsDevice owner) : CpuResource(owner), IGraphicsSubmission
{
    public SubmissionStatus Status
    {
        get
        {
            Check();
            return SubmissionStatus.Completed;
        }
    }

    public ValueTask WaitAsync(CancellationToken cancellationToken = default)
    {
        Check();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
