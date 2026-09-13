using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Tests;

public sealed class WebGpuSubmissionTests
{
    [Fact]
    public async Task AcceptanceFailureClosesScopesWithoutHandingOffWork()
    {
        var expected = new InvalidOperationException("The timeline was disposed before handoff.");
        var submission = Create();
        bool handoff = false;
        bool gpuObserved = false;
        bool scopesClosed = false;

        var failure = Assert.Throws<InvalidOperationException>(() => submission.Execute(
            () => throw expected, () => handoff = true,
            () => { gpuObserved = true; return Task.CompletedTask; },
            () => { scopesClosed = true; return Success(); }));

        Assert.Same(expected, failure);
        Assert.False(handoff);
        Assert.False(gpuObserved);
        Assert.True(scopesClosed);
        Assert.False(submission.HandoffStarted);
        Assert.Empty(await submission.Diagnostics);
    }

    [Fact]
    public async Task FailedAcceptanceAndScopeCleanupPreserveBothErrorsWithoutHandoff()
    {
        var acceptance = new InvalidOperationException("Timeline acceptance failed.");
        var cleanup = new InvalidOperationException("Scope cleanup failed.");
        var submission = Create();
        bool handedOff = false;

        var failure = Assert.Throws<AggregateException>(() => submission.Execute(
            () => throw acceptance, () => handedOff = true, () => Task.CompletedTask, () => throw cleanup));

        Assert.Equal([acceptance, cleanup], failure.InnerExceptions);
        Assert.False(handedOff);
        Assert.False(submission.HandoffStarted);
        Assert.Same(cleanup, await Assert.ThrowsAsync<InvalidOperationException>(() => submission.Diagnostics));
    }

    [Fact]
    public async Task HandoffFailureStillRegistersBothObservations()
    {
        using var semaphore = new TestSemaphore();
        var completion = new P.GpuFenceValue(semaphore, ulong.MaxValue - 1);
        var submission = new WebGpuSubmission(completion);
        var expected = new InvalidOperationException("Runtime handoff failed after entry.");
        var gpu = PendingGpu();
        var calls = new List<string>();

        var failure = Assert.Throws<P.GpuSubmissionException>(() => submission.Execute(
            () => calls.Add("accept"),
            () => { calls.Add("handoff"); throw expected; },
            () => { calls.Add("gpu"); return gpu.Task; },
            () => { calls.Add("diagnostics"); return Success(); }));

        Assert.Equal(completion, failure.Completion);
        Assert.Same(expected, failure.InnerException);
        Assert.Equal(["accept", "handoff", "gpu", "diagnostics"], calls);
        Assert.True(submission.HandoffStarted);
        Assert.False(submission.GpuEnded.IsCompleted);
        Assert.Empty(await submission.Diagnostics);
        gpu.SetResult();
        await submission.GpuEnded;
    }

    [Fact]
    public async Task DiagnosticRegistrationFailureDoesNotAbandonGpuObservation()
    {
        var submission = Create();
        var gpu = PendingGpu();
        var expected = new InvalidOperationException("PopErrorScope registration failed.");
        bool handedOff = false;

        var failure = Assert.Throws<P.GpuSubmissionException>(() => submission.Execute(
            () => { }, () => handedOff = true, () => gpu.Task, () => throw expected));

        Assert.True(handedOff);
        Assert.Same(expected, failure.InnerException);
        Assert.False(submission.GpuEnded.IsCompleted);
        var diagnosticFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => submission.Diagnostics);
        Assert.Same(expected, diagnosticFailure);
        gpu.SetResult();
        await submission.GpuEnded;
    }

    [Fact]
    public async Task GpuRegistrationFailureStillCollectsDiagnostics()
    {
        var submission = Create();
        var expected = new InvalidOperationException("Work-done callback registration failed.");
        var diagnostic = new P.GpuDiagnostic(P.GpuDiagnosticKind.Validation, "Invalid shader.");
        bool collected = false;

        var failure = Assert.Throws<P.GpuSubmissionException>(() => submission.Execute(
            () => { }, () => { }, () => throw expected,
            () => { collected = true; return Task.FromResult<IReadOnlyList<P.GpuDiagnostic>>([diagnostic]); }));

        Assert.True(collected);
        Assert.Same(expected, failure.InnerException);
        Assert.Equal(diagnostic, Assert.Single(await submission.Diagnostics));
        var gpuFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => submission.GpuEnded);
        Assert.Same(expected, gpuFailure);
        Assert.False(submission.GpuEnded.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task EverySynchronousFailureRetainsItsOriginalCause()
    {
        var handoff = new InvalidOperationException("handoff");
        var gpu = new InvalidOperationException("completion registration");
        var diagnostics = new InvalidOperationException("scope registration");
        var submission = Create();

        var failure = Assert.Throws<P.GpuSubmissionException>(() => submission.Execute(
            () => { }, () => throw handoff, () => throw gpu, () => throw diagnostics));

        var causes = Assert.IsType<AggregateException>(failure.InnerException).Flatten().InnerExceptions;
        Assert.Equal(3, causes.Count);
        Assert.Contains(handoff, causes);
        Assert.Contains(gpu, causes);
        Assert.Contains(diagnostics, causes);
        Assert.Same(gpu, await Assert.ThrowsAsync<InvalidOperationException>(() => submission.GpuEnded));
        Assert.Same(diagnostics, await Assert.ThrowsAsync<InvalidOperationException>(() => submission.Diagnostics));
    }

    [Fact]
    public async Task FailedAsynchronousCompletionDoesNotEstablishGpuUseEnding()
    {
        var submission = Create();
        var gpu = PendingGpu();
        var expected = new InvalidOperationException("GPU completion remains unknown.");
        submission.Execute(() => { }, () => { }, () => gpu.Task, Success);

        gpu.SetException(expected);

        Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(() => submission.GpuEnded));
        Assert.False(submission.GpuEnded.IsCompletedSuccessfully);
        Assert.Empty(await submission.Diagnostics);
    }

    [Fact]
    public async Task FailedAsynchronousDiagnosticsDoNotCompletePendingGpuWork()
    {
        var submission = Create();
        var gpu = PendingGpu();
        var diagnostics = new TaskCompletionSource<IReadOnlyList<P.GpuDiagnostic>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new InvalidOperationException("Diagnostic connection was lost.");
        submission.Execute(() => { }, () => { }, () => gpu.Task, () => diagnostics.Task);

        diagnostics.SetException(expected);

        Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(() => submission.Diagnostics));
        Assert.False(submission.GpuEnded.IsCompleted);
        gpu.SetResult();
        await submission.GpuEnded;
    }

    [Fact]
    public async Task ObservationsAreBoundBeforeInlineRuntimeCompletion()
    {
        var submission = Create();
        Task? acceptedGpu = null;
        Task<IReadOnlyList<P.GpuDiagnostic>>? acceptedDiagnostics = null;
        var diagnostic = new P.GpuDiagnostic(P.GpuDiagnosticKind.Validation, "Submission rejected by validation.");

        submission.Execute(
            () => { acceptedGpu = submission.GpuEnded; acceptedDiagnostics = submission.Diagnostics; },
            () => { Assert.NotNull(acceptedGpu); Assert.NotNull(acceptedDiagnostics); },
            () => Task.CompletedTask,
            () => Task.FromResult<IReadOnlyList<P.GpuDiagnostic>>([diagnostic]));

        await acceptedGpu!;
        Assert.Equal(diagnostic, Assert.Single(await acceptedDiagnostics!));
    }

    [Theory]
    [InlineData("acceptance", true)]
    [InlineData("handoff", false)]
    [InlineData("gpu", false)]
    [InlineData("diagnostics", false)]
    public async Task FailureReleasesRecordingStorageOnlyBeforeRuntimeHandoff(string stage, bool expectedRelease)
    {
        var submission = Create();
        var gpu = PendingGpu();
        var expected = new InvalidOperationException($"Failure at {stage}.");
        bool released = false;
        void FailAt(string current) { if (stage == current) { throw expected; } }

        void Execute() => submission.Execute(
            () => FailAt("acceptance"), () => FailAt("handoff"),
            () => { FailAt("gpu"); return gpu.Task; },
            () => { FailAt("diagnostics"); return Success(); });
        if (stage == "acceptance") { Assert.Same(expected, Assert.Throws<InvalidOperationException>(Execute)); }
        else { Assert.Same(expected, Assert.Throws<P.GpuSubmissionException>(Execute).InnerException); }
        submission.ReleaseUnsubmitted(() => released = true);

        Assert.Equal(expectedRelease, released);
        gpu.SetResult();
        if (stage == "gpu")
        { Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(() => submission.GpuEnded)); }
        else if (stage != "acceptance") { await submission.GpuEnded; }
        if (stage == "diagnostics")
        { Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(() => submission.Diagnostics)); }
        else { Assert.Empty(await submission.Diagnostics); }
    }

    private static WebGpuSubmission Create() => new(new(new TestSemaphore(), 1));
    private static TaskCompletionSource PendingGpu() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task<IReadOnlyList<P.GpuDiagnostic>> Success() => Task.FromResult<IReadOnlyList<P.GpuDiagnostic>>([]);
    private sealed class TestSemaphore : P.GpuSemaphore { public override void Dispose() { } }
}
