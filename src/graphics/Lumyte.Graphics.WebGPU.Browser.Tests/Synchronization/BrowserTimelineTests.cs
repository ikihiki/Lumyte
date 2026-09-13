using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Browser.Tests.Synchronization;

public sealed class BrowserTimelineTests
{
    [Fact]
    public async Task InitialValueHasNoGpuWorkOrDiagnosticsToWaitFor()
    {
        const ulong initial = (1ul << 40) + 7;
        using var timeline = new BrowserTimeline(new object(), new(), initial);

        await timeline.WaitAsync(initial);

        Assert.True(timeline.IsComplete(initial));
        Assert.Equal((0, 0, 0), timeline.Statistics);
    }

    [Theory]
    [InlineData(10ul)]
    [InlineData(11ul)]
    public void SignalMustExceedEveryPreviouslyReservedValue(ulong value)
    {
        using var timeline = new BrowserTimeline(new object(), new(), 10);
        timeline.Reserve(11);

        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => timeline.ValidateSignal(value));

        Assert.Equal("value", error.ParamName);
    }

    [Fact]
    public void ReservationIsNotAnAcceptedCompletionPoint()
    {
        using var timeline = new BrowserTimeline(new object(), new(), 0);
        timeline.Reserve(7);

        Assert.Throws<ArgumentOutOfRangeException>(() => timeline.IsComplete(7));
        Assert.Throws<ArgumentOutOfRangeException>(() => timeline.WaitAsync(7));
    }

    [Fact]
    public async Task GapsBetweenAcceptedValuesAreNotInventedSubmissions()
    {
        using var timeline = new BrowserTimeline(new object(), new(), 0);
        Accept(timeline, 7, Task.CompletedTask, Success());
        await timeline.WaitAsync(7);

        Assert.Throws<ArgumentOutOfRangeException>(() => timeline.IsComplete(6));
        Assert.Throws<ArgumentOutOfRangeException>(() => timeline.WaitAsync(6));
    }

    [Fact]
    public async Task AlreadyCompletedCallbacksCanBeAcceptedWithoutLosingTheirResults()
    {
        using var timeline = new BrowserTimeline(new object(), new(), 0);

        Accept(timeline, 1, Task.CompletedTask, Success());
        await timeline.WaitAsync(1);

        Assert.True(timeline.IsComplete(1));
        Assert.Equal((0, 1, 0), timeline.Statistics);
    }

    [Fact]
    public async Task GpuCompletionDoesNotEstablishSuccessBeforeDiagnosticsArrive()
    {
        using var timeline = new BrowserTimeline(new object(), new(), 0);
        var gpu = Completion();
        var diagnostics = Diagnostics();
        Accept(timeline, 1, gpu.Task, diagnostics.Task);
        Task wait = timeline.WaitAsync(1).AsTask();

        gpu.SetResult();

        Assert.True(timeline.IsComplete(1));
        Assert.False(wait.IsCompleted);
        diagnostics.SetResult([]);
        await wait;
    }

    [Fact]
    public async Task DiagnosticsDoNotEstablishCompletionBeforeGpuUseEnds()
    {
        using var timeline = new BrowserTimeline(new object(), new(), 0);
        var gpu = Completion();
        var diagnostics = Diagnostics();
        Accept(timeline, 1, gpu.Task, diagnostics.Task);
        Task wait = timeline.WaitAsync(1).AsTask();

        diagnostics.SetResult([]);

        Assert.False(timeline.IsComplete(1));
        Assert.False(wait.IsCompleted);
        gpu.SetResult();
        await wait;
    }

    [Fact]
    public async Task ExecutionFailureIsReportedOnlyAfterGpuUseEnds()
    {
        using var timeline = new BrowserTimeline(new object(), new(), 0);
        var gpu = Completion();
        var diagnostics = Diagnostics();
        var diagnostic = new P.GpuDiagnostic(P.GpuDiagnosticKind.Validation, "Invalid compute binding.");
        Accept(timeline, 1, gpu.Task, diagnostics.Task);
        Task wait = timeline.WaitAsync(1).AsTask();

        diagnostics.SetResult([diagnostic]);

        Assert.False(wait.IsCompleted);
        Assert.False(timeline.IsComplete(1));
        gpu.SetResult();
        P.GpuExecutionException error = await Assert.ThrowsAsync<P.GpuExecutionException>(() => wait);
        Assert.Equal(new P.GpuFenceValue(timeline, 1), error.FenceValue);
        Assert.Equal(diagnostic, Assert.Single(error.Diagnostics));
        Assert.True(timeline.IsComplete(1));
    }

    [Fact]
    public async Task CancellationAffectsOnlyTheSelectedWait()
    {
        using var timeline = new BrowserTimeline(new object(), new(), 0);
        using CancellationTokenSource cancellation = new();
        var gpu = Completion();
        var diagnostics = Diagnostics();
        Accept(timeline, 1, gpu.Task, diagnostics.Task);
        Task cancelled = timeline.WaitAsync(1, cancellation.Token).AsTask();
        Task surviving = timeline.WaitAsync(1).AsTask();

        cancellation.Cancel();

        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.False(surviving.IsCompleted);
        Assert.False(timeline.IsComplete(1));
        gpu.SetResult();
        diagnostics.SetResult([]);
        await surviving;
    }

    [Fact]
    public async Task DeviceLossEndsPendingWaitsWithoutClaimingGpuCompletion()
    {
        var status = new BrowserDeviceStatus();
        using var timeline = new BrowserTimeline(new object(), status, 0);
        Accept(timeline, 1, Completion().Task, Diagnostics().Task);
        Task wait = timeline.WaitAsync(1).AsTask();

        status.Lose("Device disconnected.");

        GpuDeviceLostException error = await Assert.ThrowsAsync<GpuDeviceLostException>(() => wait);
        Assert.Contains("Device disconnected", error.Message);
        Assert.Throws<GpuDeviceLostException>(() => timeline.IsComplete(1));
    }

    [Fact]
    public void DeviceLossTakesPriorityEvenWhenTheRequestedValueWasNeverIssued()
    {
        var status = new BrowserDeviceStatus();
        using var timeline = new BrowserTimeline(new object(), status, 0);
        status.Lose("Device disconnected.");

        Assert.Throws<GpuDeviceLostException>(() => timeline.IsComplete(7));
        Assert.Throws<GpuDeviceLostException>(() => timeline.WaitAsync(7));
    }

    [Fact]
    public async Task AFailedEarlierBatchDoesNotPoisonAnIndependentLaterBatch()
    {
        using var timeline = new BrowserTimeline(new object(), new(), 0);
        var firstDiagnostics = Diagnostics();
        Accept(timeline, 1, Task.CompletedTask, firstDiagnostics.Task);
        Accept(timeline, 2, Task.CompletedTask, Success());
        Task first = timeline.WaitAsync(1).AsTask();

        await timeline.WaitAsync(2);

        Assert.True(timeline.IsComplete(1));
        Assert.False(first.IsCompleted);
        firstDiagnostics.SetResult([new(P.GpuDiagnosticKind.Validation, "Earlier batch failed.")]);
        P.GpuExecutionException error = await Assert.ThrowsAsync<P.GpuExecutionException>(() => first);
        Assert.Equal(1ul, error.FenceValue.Value);
        await timeline.WaitAsync(2);
    }

    [Fact]
    public async Task ConsecutiveSuccessesRetainIntervalsInsteadOfEveryBatch()
    {
        using var timeline = new BrowserTimeline(new object(), new(), 0);

        for (ulong value = 1; value <= 256; value++) { Accept(timeline, value, Task.CompletedTask, Success()); }
        await timeline.WaitAsync(256);

        Assert.Equal((0, 1, 0), timeline.Statistics);
        await timeline.WaitAsync(1);
        await timeline.WaitAsync(128);
    }

    [Fact]
    public async Task OutOfOrderDiagnosticsMergeAdjacentSuccessfulIntervals()
    {
        using var timeline = new BrowserTimeline(new object(), new(), 0);
        var first = Diagnostics();
        var second = Diagnostics();
        var third = Diagnostics();
        Accept(timeline, 1, Task.CompletedTask, first.Task);
        Accept(timeline, 2, Task.CompletedTask, second.Task);
        Accept(timeline, 3, Task.CompletedTask, third.Task);

        first.SetResult([]);
        third.SetResult([]);
        await timeline.WaitAsync(1);
        await timeline.WaitAsync(3);
        second.SetResult([]);
        await timeline.WaitAsync(2);

        Assert.Equal((0, 1, 0), timeline.Statistics);
    }

    [Fact]
    public async Task MaximumSignalValueDoesNotWrapSuccessIntervalArithmetic()
    {
        using var timeline = new BrowserTimeline(new object(), new(), ulong.MaxValue - 3);

        Accept(timeline, ulong.MaxValue - 2, Task.CompletedTask, Success());
        Accept(timeline, ulong.MaxValue, Task.CompletedTask, Success());
        await timeline.WaitAsync(ulong.MaxValue);

        Assert.Equal((0, 2, 0), timeline.Statistics);
        Assert.Throws<ArgumentOutOfRangeException>(() => timeline.IsComplete(ulong.MaxValue - 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => timeline.ValidateSignal(ulong.MaxValue));
    }

    [Fact]
    public async Task FailedDiagnosticsRemainAvailableAfterTheBatchRecordIsRetired()
    {
        using var timeline = new BrowserTimeline(new object(), new(), 0);
        var diagnostic = new P.GpuDiagnostic(P.GpuDiagnosticKind.Validation, "Binding 7 is missing.");
        List<P.GpuDiagnostic> diagnostics = [diagnostic];
        Accept(timeline, 1, Task.CompletedTask, Task.FromResult<IReadOnlyList<P.GpuDiagnostic>>(diagnostics));
        await Assert.ThrowsAsync<P.GpuExecutionException>(() => timeline.WaitAsync(1).AsTask());

        diagnostics.Clear();
        P.GpuExecutionException repeated = await Assert.ThrowsAsync<P.GpuExecutionException>(() => timeline.WaitAsync(1).AsTask());

        Assert.Equal(diagnostic, Assert.Single(repeated.Diagnostics));
        Assert.Equal((0, 0, 1), timeline.Statistics);
    }

    [Fact]
    public void ForeignQueueCannotUseTheTimeline()
    {
        var owner = new object();
        using var timeline = new BrowserTimeline(owner, new(), 0);

        ArgumentException error = Assert.Throws<ArgumentException>(() => timeline.VerifyOwner(new object()));

        Assert.Equal("owner", error.ParamName);
        timeline.VerifyOwner(owner);
    }

    [Fact]
    public void DisposalIsIdempotentAndEndsCpuObservation()
    {
        var timeline = new BrowserTimeline(new object(), new(), 0);

        timeline.Dispose();
        timeline.Dispose();

        Assert.Throws<ObjectDisposedException>(() => timeline.IsComplete(0));
        Assert.Throws<ObjectDisposedException>(() => timeline.WaitAsync(0));
        Assert.Throws<ObjectDisposedException>(() => timeline.ValidateSignal(1));
    }

    [Fact]
    public async Task FailedGpuCompletionBreaksTheWaitEvenWhenDiagnosticsNeverArrive()
    {
        using var timeline = new BrowserTimeline(new object(), new(), 0);
        var gpu = Completion();
        Accept(timeline, 1, gpu.Task, Diagnostics().Task);
        Task wait = timeline.WaitAsync(1).AsTask();

        gpu.SetException(new InvalidOperationException("Completion callback failed."));

        GpuDeviceLostException error = await Assert.ThrowsAsync<GpuDeviceLostException>(() => wait);
        Assert.Contains("Completion callback failed", error.Message);
        Assert.Throws<GpuDeviceLostException>(() => timeline.IsComplete(1));
    }

    [Fact]
    public async Task FailedDiagnosticConnectionBreaksTheWaitEvenWhenGpuCompletionNeverArrives()
    {
        using var timeline = new BrowserTimeline(new object(), new(), 0);
        var diagnostics = Diagnostics();
        Accept(timeline, 1, Completion().Task, diagnostics.Task);
        Task wait = timeline.WaitAsync(1).AsTask();

        diagnostics.SetException(new InvalidOperationException("Diagnostic callback failed."));

        GpuDeviceLostException error = await Assert.ThrowsAsync<GpuDeviceLostException>(() => wait);
        Assert.Contains("Diagnostic callback failed", error.Message);
    }

    private static TaskCompletionSource Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource<IReadOnlyList<P.GpuDiagnostic>> Diagnostics()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task<IReadOnlyList<P.GpuDiagnostic>> Success() => Task.FromResult<IReadOnlyList<P.GpuDiagnostic>>([]);

    private static void Accept(BrowserTimeline timeline, ulong value, Task gpuEnded, Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics)
    {
        timeline.Reserve(value);
        timeline.Accept(value, gpuEnded, diagnostics);
    }
}
