namespace Lumyte.Graphics.Native.Tests;

public sealed class NativeGpuSubmissionExceptionTests
{
    [Fact]
    public void FailurePreservesTheRequestedCompletionAndOriginalError()
    {
        using var semaphore = new UnusedSemaphore();
        var completion = new NativeGpuTimelinePoint(semaphore, ulong.MaxValue - 1);
        var cause = new InvalidOperationException("Submission failed.");

        var failure = new NativeGpuSubmissionException(completion, cause);

        Assert.Equal(completion, failure.Completion);
        Assert.Same(cause, failure.InnerException);
    }

    [Fact]
    public void FailureRequiresAnOriginalError()
    {
        var failure = Assert.Throws<ArgumentNullException>(() => new NativeGpuSubmissionException(default, null!));

        Assert.Equal("innerException", failure.ParamName);
    }

    private sealed class UnusedSemaphore : NativeGpuSemaphore
    {
        public override bool IsComplete(ulong value) => throw new InvalidOperationException("No completion query is expected.");
        public override void WaitCpu(ulong value) => throw new InvalidOperationException("No wait is expected.");
        public override void SignalCpu(ulong value) => throw new InvalidOperationException("No signal is expected.");
        public override void Dispose() { }
    }
}
