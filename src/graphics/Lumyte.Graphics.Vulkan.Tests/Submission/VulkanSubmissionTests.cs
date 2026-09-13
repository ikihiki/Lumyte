using Lumyte.Graphics.Native;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan.Tests;

public sealed class VulkanSubmissionTests
{
    [Fact]
    public void AcceptedSubmissionTransfersOwnershipBeforeProcessingItsResult()
    {
        var calls = new SubmissionCalls(Result.Success);

        VulkanSubmission.Execute(default, calls);

        Assert.Equal(["Submit", "RetainKnown", "Check"], calls.Operations);
        Assert.True(calls.Retirement.Submitted);
    }

    [Theory]
    [InlineData(Result.ErrorOutOfHostMemory)]
    [InlineData(Result.ErrorOutOfDeviceMemory)]
    public void DefinitelyRejectedSubmissionPreservesTheOriginalErrorWithoutTransferringOwnership(Result result)
    {
        var cause = new NativeGpuException("vkQueueSubmit2", (int)result);
        var calls = new SubmissionCalls(result, diagnosticFailure: cause);

        var failure = Assert.Throws<NativeGpuException>(() => VulkanSubmission.Execute(default, calls));
        calls.Retirement.RejectUnlessRetained();

        Assert.Same(cause, failure);
        Assert.Equal(["Submit", "Check"], calls.Operations);
        Assert.True(calls.Retirement.Rejected);
        Assert.False(calls.Retirement.Submitted);
        Assert.Equal((0, 1), calls.Retirement.ReleaseCounts);
    }

    [Theory]
    [InlineData(Result.ErrorDeviceLost)]
    [InlineData(Result.ErrorUnknown)]
    public void AmbiguousResultTransfersOwnershipBeforeReportingAnUnprovenCompletion(Result result)
    {
        using var semaphore = new UnusedSemaphore();
        var completion = new NativeGpuTimelinePoint(semaphore, ulong.MaxValue - 1);
        var cause = new NativeGpuException("vkQueueSubmit2", (int)result);
        var calls = new SubmissionCalls(result, diagnosticFailure: cause);

        var failure = Assert.Throws<NativeGpuSubmissionException>(() => VulkanSubmission.Execute(completion, calls));
        calls.Retirement.RejectUnlessRetained();

        Assert.Equal(completion, failure.Completion);
        Assert.Same(cause, failure.InnerException);
        Assert.Equal(["Submit", "RetainUnknown", "Check"], calls.Operations);
        Assert.True(calls.Retirement.Submitted);
        Assert.False(calls.Retirement.TryReleaseCompleted(ulong.MaxValue));
        Assert.Equal((0, 0), calls.Retirement.ReleaseCounts);
    }

    [Fact]
    public void InteropFailureTransfersOwnershipWithoutInventingANativeResult()
    {
        using var semaphore = new UnusedSemaphore();
        var completion = new NativeGpuTimelinePoint(semaphore, 37);
        var cause = new InvalidOperationException("Native invocation failed.");
        var calls = new SubmissionCalls(Result.Success, submitFailure: cause);

        var failure = Assert.Throws<NativeGpuSubmissionException>(() => VulkanSubmission.Execute(completion, calls));
        calls.Retirement.RejectUnlessRetained();

        Assert.Equal(completion, failure.Completion);
        Assert.Same(cause, failure.InnerException);
        Assert.Equal(["Submit", "RetainUnknown"], calls.Operations);
        Assert.True(calls.Retirement.Submitted);
        Assert.Equal((0, 0), calls.Retirement.ReleaseCounts);
    }

    [Fact]
    public void DiagnosticFailureAfterAcceptanceLeavesOwnershipTransferred()
    {
        var cause = new OutOfMemoryException("Diagnostic allocation failed.");
        var calls = new SubmissionCalls(Result.Success, diagnosticFailure: cause);

        var failure = Assert.Throws<NativeGpuSubmissionException>(() => VulkanSubmission.Execute(default, calls));

        Assert.Same(cause, failure.InnerException);
        Assert.Equal(["Submit", "RetainKnown", "Check"], calls.Operations);
    }

    [Theory]
    [InlineData(36ul, false)]
    [InlineData(37ul, true)]
    [InlineData(38ul, true)]
    public void AcceptedStorageReleasesOnlyAfterItsInternalCompletion(ulong completed, bool shouldRelease)
    {
        var calls = new SubmissionCalls(Result.Success);
        VulkanSubmission.Execute(default, calls);

        bool released = calls.Retirement.TryReleaseCompleted(completed);

        Assert.Equal(shouldRelease, released);
        Assert.Equal(shouldRelease ? (1, 1) : (0, 0), calls.Retirement.ReleaseCounts);
    }

    [Fact]
    public void UnknownStorageCanBeReleasedOnceAfterGpuUseHasEnded()
    {
        var calls = new SubmissionCalls(Result.ErrorUnknown,
            diagnosticFailure: new NativeGpuException("vkQueueSubmit2", (int)Result.ErrorUnknown));
        Assert.Throws<NativeGpuSubmissionException>(() => VulkanSubmission.Execute(default, calls));
        calls.Retirement.RejectUnlessRetained();

        calls.Retirement.ReleaseAfterGpuUse();
        calls.Retirement.ReleaseAfterGpuUse();

        Assert.Equal((1, 1), calls.Retirement.ReleaseCounts);
    }

    private sealed class SubmissionCalls(Result result, Exception? submitFailure = null,
        Exception? diagnosticFailure = null) : IVulkanSubmissionCalls
    {
        public List<string> Operations { get; } = [];
        public Retirement Retirement { get; } = new(37);

        public Result Submit()
        {
            Operations.Add("Submit");
            if (submitFailure is not null) { throw submitFailure; }
            return result;
        }

        public void Retain(bool completionSignalKnown)
        {
            Retirement.Retain(completionSignalKnown);
            Operations.Add(completionSignalKnown ? "RetainKnown" : "RetainUnknown");
        }

        public void CheckResult(Result nativeResult)
        {
            Operations.Add("Check");
            Assert.Equal(result, nativeResult);
            if (diagnosticFailure is not null) { throw diagnosticFailure; }
        }
    }

    private sealed class Retirement(ulong value) : VulkanSubmissionRetirement(value)
    {
        private int recordingReleases;
        private int initializationReleases;
        public bool Submitted { get; private set; }
        public bool Rejected { get; private set; }
        public (int Recording, int Initialization) ReleaseCounts => (recordingReleases, initializationReleases);

        protected override void MarkSubmitted() => Submitted = true;
        protected override void Reject()
        {
            Rejected = true;
            initializationReleases++;
        }
        protected override void ReleaseNative()
        {
            recordingReleases++;
            initializationReleases++;
        }
    }

    private sealed class UnusedSemaphore : NativeGpuSemaphore
    {
        public override bool IsComplete(ulong value) => throw new InvalidOperationException("No completion query is expected.");
        public override void WaitCpu(ulong value) => throw new InvalidOperationException("No wait is expected.");
        public override void SignalCpu(ulong value) => throw new InvalidOperationException("No signal is expected.");
        public override void Dispose() { }
    }
}
