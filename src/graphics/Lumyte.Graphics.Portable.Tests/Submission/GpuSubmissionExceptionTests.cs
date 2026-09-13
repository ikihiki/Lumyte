namespace Lumyte.Graphics.Portable.Tests;

public sealed class GpuSubmissionExceptionTests
{
    [Fact]
    public void FailurePreservesTheExactPointAndOriginalCause()
    {
        using var semaphore = new TestSemaphore();
        var completion = new GpuFenceValue(semaphore, ulong.MaxValue);
        var cause = new InvalidOperationException("Runtime submission failed.");

        var failure = new GpuSubmissionException(completion, cause);

        Assert.Equal(completion, failure.Completion);
        Assert.Same(cause, failure.InnerException);
    }

    [Fact]
    public void CompletionRequiresATimelineIdentity()
    {
        var failure = Assert.Throws<ArgumentException>(() => new GpuSubmissionException(default, new InvalidOperationException()));

        Assert.Equal("completion", failure.ParamName);
    }

    [Fact]
    public void FailureRequiresItsOriginalCause()
    {
        using var semaphore = new TestSemaphore();

        var failure = Assert.Throws<ArgumentNullException>(() => new GpuSubmissionException(new(semaphore, 1), null!));

        Assert.Equal("innerException", failure.ParamName);
    }

    private sealed class TestSemaphore : GpuSemaphore { public override void Dispose() { } }
}
