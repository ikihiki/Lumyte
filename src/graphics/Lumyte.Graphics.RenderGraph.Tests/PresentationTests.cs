namespace Lumyte.Graphics.RenderGraph.Tests;

public sealed class PresentationTests
{
    private static readonly GpuGraphTextureDescription Description = new(4, 4, GpuFormat.Rgba8Unorm);

    [Fact]
    public async Task ContextCleanupAttemptsEveryFrameWhenTheirLeasesFail()
    {
        await using var runtime = new TestRuntime();
        var presentation = new TestPresentation { Acquire = _ => new(CreateTarget(runtime.Id)) };
        using var context = new GpuRenderContext(runtime, presentation);
        var first = await context.BeginFrameAsync();
        var second = await context.BeginFrameAsync();
        var firstLease = new TestLease(() => throw new InvalidOperationException("first"));
        var secondLease = new TestLease(() => throw new InvalidOperationException("second"));
        first.Retain(firstLease);
        second.Retain(secondLease);

        var error = Assert.Throws<AggregateException>(context.Dispose);
        context.Dispose();

        Assert.Equal(["first", "second"], error.Flatten().InnerExceptions.Select(failure => failure.Message).Order());
        Assert.Equal((2, 1, 1), (presentation.DiscardCount, firstLease.Disposals, secondLease.Disposals));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnknownSubmissionKeepsCompletionWhenRetirementRegistrationFails(bool synchronous)
    {
        var ended = false;
        var completion = new GpuGraphCompletion(() => ended, _ => ValueTask.CompletedTask,
            _ => throw new InvalidOperationException("retirement"));
        var submissionError = new GpuRenderGraphSubmissionException(completion, new InvalidOperationException("handoff"));
        var lease = new TestLease();
        await using var runtime = new TestRuntime
        { Submit = () => synchronous ? throw submissionError : ValueTask.FromException<GpuRenderGraphExecution>(submissionError) };
        var presentation = new TestPresentation { Acquire = _ => new(CreateTarget(runtime.Id)) };
        using var context = new GpuRenderContext(runtime, presentation);
        using var frame = await context.BeginFrameAsync();
        frame.Retain(lease);
        frame.Graph.AddPass("clear", GraphTests.Contract.Instance, new(null, frame.TargetResource));

        var error = await Assert.ThrowsAsync<GpuRenderGraphSubmissionException>(() => frame.SubmitAsync().AsTask());
        frame.Dispose();
        Assert.Equal(0, lease.Disposals);
        ended = true;
        Assert.True(error.Completion.IsComplete);

        Assert.Equal(1, lease.Disposals);
    }

    [Fact]
    public async Task FrameCleanupAttemptsEveryLeaseWhenOneReleaseFails()
    {
        var lease = new TestLease();
        await using var runtime = new TestRuntime();
        var presentation = new TestPresentation { Acquire = _ => new(CreateTarget(runtime.Id)) };
        using var context = new GpuRenderContext(runtime, presentation);
        var frame = await context.BeginFrameAsync();
        frame.Retain(lease);
        frame.Retain(new TestLease(() => throw new InvalidOperationException("release")));

        Assert.Throws<AggregateException>(frame.Dispose);

        Assert.Equal(1, lease.Disposals);
        Assert.Equal(1, presentation.DiscardCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedRetirementRegistrationDoesNotReleaseAcceptedGpuOwnership(bool failExecutionCleanup)
    {
        var ended = false;
        var completion = new GpuGraphCompletion(() => ended, _ => ValueTask.CompletedTask,
            _ => throw new InvalidOperationException("retirement"));
        var lease = new TestLease();
        await using var runtime = new TestRuntime
        {
            Submit = () => new(new GpuRenderGraphExecution(completion,
                new Dictionary<GpuRenderGraphResource, GpuGraphResourceRef>(),
                new TestLease(() => { if (failExecutionCleanup) { throw new InvalidOperationException("execution cleanup"); } })))
        };
        var presentation = new TestPresentation { Acquire = _ => new(CreateTarget(runtime.Id)) };
        using var context = new GpuRenderContext(runtime, presentation);
        using var frame = await context.BeginFrameAsync();
        frame.Retain(lease);
        frame.Graph.AddPass("clear", GraphTests.Contract.Instance, new(null, frame.TargetResource));

        var error = await Assert.ThrowsAsync<GpuRenderGraphSubmissionException>(() => frame.SubmitAsync().AsTask());
        frame.Dispose();
        Assert.Same(completion, presentation.RetiredCompletion);
        Assert.Equal(0, lease.Disposals);
        ended = true;
        Assert.True(error.Completion.IsComplete);

        Assert.Equal(1, lease.Disposals);
    }

    [Fact]
    public async Task FrameLeaseSurvivesEarlyExecutionDisposalUntilGpuUseEnds()
    {
        IDisposable? gpuUse = null;
        var ended = false;
        var completion = new GpuGraphCompletion(() => ended, _ => ValueTask.CompletedTask, lease => gpuUse = lease);
        var lease = new TestLease();
        await using var runtime = new TestRuntime
        {
            Submit = () => new(new GpuRenderGraphExecution(completion,
                new Dictionary<GpuRenderGraphResource, GpuGraphResourceRef>(), new TestLease()))
        };
        var presentation = new TestPresentation { Acquire = _ => new(CreateTarget(runtime.Id)) };
        using var context = new GpuRenderContext(runtime, presentation);
        using var frame = await context.BeginFrameAsync();
        frame.Retain(lease);
        frame.Graph.AddPass("clear", GraphTests.Contract.Instance, new(null, frame.TargetResource));
        var execution = await frame.SubmitAsync();

        frame.Dispose();
        execution.Dispose();
        Assert.Equal(0, lease.Disposals);
        ended = true;
        gpuUse!.Dispose();

        Assert.Equal(1, lease.Disposals);
    }

    [Fact]
    public async Task FrameLeaseSurvivesUncertainSubmissionFailure()
    {
        IDisposable? gpuUse = null;
        var completion = new GpuGraphCompletion(() => false,
            _ => ValueTask.FromException(new InvalidOperationException("diagnostic")), lease => gpuUse = lease);
        var lease = new TestLease();
        await using var runtime = new TestRuntime
        {
            Submit = () => ValueTask.FromException<GpuRenderGraphExecution>(
                new GpuRenderGraphSubmissionException(completion, new InvalidOperationException("handoff")))
        };
        var presentation = new TestPresentation { Acquire = _ => new(CreateTarget(runtime.Id)) };
        using var context = new GpuRenderContext(runtime, presentation);
        using var frame = await context.BeginFrameAsync();
        frame.Retain(lease);
        frame.Graph.AddPass("clear", GraphTests.Contract.Instance, new(null, frame.TargetResource));

        await Assert.ThrowsAsync<GpuRenderGraphSubmissionException>(() => frame.SubmitAsync().AsTask());
        frame.Dispose();
        Assert.Equal(0, lease.Disposals);
        gpuUse!.Dispose();

        Assert.Equal(1, lease.Disposals);
    }

    [Fact]
    public async Task DiscardingUnsubmittedFrameReturnsItsLeaseOnce()
    {
        var lease = new TestLease();
        await using var runtime = new TestRuntime();
        var presentation = new TestPresentation { Acquire = _ => new(CreateTarget(runtime.Id)) };
        using var context = new GpuRenderContext(runtime, presentation);
        var frame = await context.BeginFrameAsync();
        frame.Retain(lease);

        frame.Dispose();
        frame.Dispose();

        Assert.Equal(1, lease.Disposals);
    }

    [Fact]
    public async Task ImportsAreRetainedBeforeWaitingForPresentationTarget()
    {
        await using var runtime = new TestRuntime();
        var pendingTarget = new TaskCompletionSource<GpuGraphPresentationTarget>(TaskCreationOptions.RunContinuationsAsynchronously);
        var presentation = new TestPresentation { Acquire = token => new(pendingTarget.Task.WaitAsync(token)) };
        using var context = new GpuRenderContext(runtime, presentation);
        var display = CreatePlan(new BindingsTests.TestTexture(runtime.Id, Description));

        var submission = context.SubmitAsync(display.Plan, display.Bindings, display.Target);
        Assert.Equal(1, runtime.Facade.UseCount);
        pendingTarget.SetResult(CreateTarget(runtime.Id));
        using var execution = await submission;

        Assert.Equal(0, runtime.Facade.UseCount);
        Assert.Equal(1, presentation.PresentCount);
    }

    [Fact]
    public async Task ResizedTargetIsDiscardedBeforeSubmission()
    {
        await using var runtime = new TestRuntime();
        var presentation = new TestPresentation { Acquire = _ => new(CreateTarget(runtime.Id, Description with { Width = 8 })) };
        using var context = new GpuRenderContext(runtime, presentation);
        var display = CreatePlan();

        var error = await Assert.ThrowsAsync<GpuPresentationTargetChangedException>(() => context.SubmitAsync(display.Plan, display.Bindings, display.Target).AsTask());

        Assert.Equal(8u, error.Description.Width);
        Assert.Equal(1, presentation.DiscardCount);
        Assert.Equal(0, runtime.SubmitCount);
    }

    [Fact]
    public async Task PresentationFailureDoesNotDiscardSubmittedTarget()
    {
        await using var runtime = new TestRuntime();
        var presentation = new TestPresentation { Acquire = _ => new(CreateTarget(runtime.Id)), FailPresent = true };
        using var context = new GpuRenderContext(runtime, presentation);
        var display = CreatePlan();

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SubmitAsync(display.Plan, display.Bindings, display.Target).AsTask());

        Assert.Equal(0, presentation.DiscardCount);
        Assert.Equal(1, presentation.PresentCount);
        Assert.Equal(1, runtime.ExecutionOwnership.Disposals);
    }

    [Fact]
    public async Task UnknownQueueAcceptanceRetiresTargetInsteadOfDiscarding()
    {
        var completion = new GpuGraphCompletion(() => false, _ => ValueTask.FromException(new InvalidOperationException("pending failure")));
        await using var runtime = new TestRuntime { Submit = () => ValueTask.FromException<GpuRenderGraphExecution>(new GpuRenderGraphSubmissionException(completion, new InvalidOperationException("handoff"))) };
        var presentation = new TestPresentation { Acquire = _ => new(CreateTarget(runtime.Id)) };
        using var context = new GpuRenderContext(runtime, presentation);
        var display = CreatePlan();

        await Assert.ThrowsAsync<GpuRenderGraphSubmissionException>(() => context.SubmitAsync(display.Plan, display.Bindings, display.Target).AsTask());

        Assert.Same(completion, presentation.RetiredCompletion);
        Assert.Equal(0, presentation.DiscardCount);
        Assert.Equal(0, presentation.PresentCount);
    }

    [Fact]
    public async Task RejectedSubmissionReturnsUnsubmittedTarget()
    {
        await using var runtime = new TestRuntime { Submit = () => ValueTask.FromException<GpuRenderGraphExecution>(new InvalidOperationException("build")) };
        var presentation = new TestPresentation { Acquire = _ => new(CreateTarget(runtime.Id)) };
        using var context = new GpuRenderContext(runtime, presentation);
        var display = CreatePlan();

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SubmitAsync(display.Plan, display.Bindings, display.Target).AsTask());

        Assert.Equal(1, presentation.DiscardCount);
        Assert.Equal(0, presentation.PresentCount);
    }

    [Fact]
    public async Task CancelledAcquireReturnsImportUsage()
    {
        await using var runtime = new TestRuntime();
        var pendingTarget = new TaskCompletionSource<GpuGraphPresentationTarget>(TaskCreationOptions.RunContinuationsAsynchronously);
        var presentation = new TestPresentation { Acquire = token => new(pendingTarget.Task.WaitAsync(token)) };
        using var context = new GpuRenderContext(runtime, presentation);
        var display = CreatePlan(new BindingsTests.TestTexture(runtime.Id, Description));
        using var cancellation = new CancellationTokenSource();

        var submission = context.SubmitAsync(display.Plan, display.Bindings, display.Target, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => submission.AsTask());

        Assert.Equal(0, runtime.Facade.UseCount);
        Assert.Equal(0, presentation.DiscardCount);
    }

    [Fact]
    public async Task ClosingContextWhileAcquiringReturnsTheArrivingTarget()
    {
        await using var runtime = new TestRuntime();
        var pendingTarget = new TaskCompletionSource<GpuGraphPresentationTarget>(TaskCreationOptions.RunContinuationsAsynchronously);
        var presentation = new TestPresentation { Acquire = _ => new(pendingTarget.Task) };
        using var context = new GpuRenderContext(runtime, presentation);
        var frame = context.BeginFrameAsync();

        context.Dispose();
        pendingTarget.SetResult(CreateTarget(runtime.Id));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => frame.AsTask());

        Assert.Equal(1, presentation.DiscardCount);
        Assert.False(runtime.IsDisposed);
    }

    [Fact]
    public async Task DisposingFrameDuringSubmissionDoesNotReturnItsTargetEarly()
    {
        var pending = new TaskCompletionSource<GpuRenderGraphExecution>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var runtime = new TestRuntime { Submit = () => new(pending.Task) };
        var presentation = new TestPresentation { Acquire = _ => new(CreateTarget(runtime.Id)) };
        using var context = new GpuRenderContext(runtime, presentation);
        using var frame = await context.BeginFrameAsync();
        frame.Graph.AddPass("clear", GraphTests.Contract.Instance, new(null, frame.TargetResource));
        var submission = frame.SubmitAsync();

        frame.Dispose();
        Assert.Equal(0, presentation.DiscardCount);
        pending.SetResult(runtime.CreateExecution());
        using var execution = await submission;

        Assert.Equal(1, presentation.PresentCount);
        Assert.Equal(0, presentation.DiscardCount);
    }

    [Fact]
    public async Task ContextDisposalReturnsUnsubmittedFramesWithoutOwningRuntime()
    {
        await using var runtime = new TestRuntime();
        var presentation = new TestPresentation { Acquire = _ => new(CreateTarget(runtime.Id)) };
        using var context = new GpuRenderContext(runtime, presentation);
        using var frame = await context.BeginFrameAsync();

        context.Dispose();
        frame.Dispose();

        Assert.Equal(1, presentation.DiscardCount);
        Assert.False(runtime.IsDisposed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FrameRetiresUnknownSubmissionForSynchronousAndAsynchronousFailures(bool synchronous)
    {
        var completion = new GpuGraphCompletion(() => false, _ => ValueTask.CompletedTask);
        var error = new GpuRenderGraphSubmissionException(completion, new InvalidOperationException("handoff"));
        await using var runtime = new TestRuntime { Submit = () => synchronous ? throw error : ValueTask.FromException<GpuRenderGraphExecution>(error) };
        var presentation = new TestPresentation { Acquire = _ => new(CreateTarget(runtime.Id)) };
        using var context = new GpuRenderContext(runtime, presentation);
        using var frame = await context.BeginFrameAsync();
        frame.Graph.AddPass("clear", GraphTests.Contract.Instance, new(null, frame.TargetResource));

        await Assert.ThrowsAsync<GpuRenderGraphSubmissionException>(() => frame.SubmitAsync().AsTask());
        frame.Dispose();

        Assert.Same(completion, presentation.RetiredCompletion);
        Assert.Equal(0, presentation.DiscardCount);
    }

    private static (GpuRenderGraphPlan Plan, GpuRenderGraphBindings Bindings, GpuGraphTextureInput Target) CreatePlan(GpuGraphTextureRef? imported = null)
    {
        var graph = new GpuRenderGraph();
        var target = graph.CreateTextureInput("target", Description);
        var source = imported is null ? null : graph.ImportTexture("source", imported);
        graph.AddPass("write", GraphTests.Contract.Instance, new(source, target.Texture));
        graph.MarkOutput(target.Texture);
        var plan = graph.Compile();
        return (plan, plan.CreateBindings().Build(), target);
    }
    private static GpuGraphPresentationTarget CreateTarget(Guid runtimeId, GpuGraphTextureDescription? description = null)
        => new(new BindingsTests.TestTexture(runtimeId, description ?? Description), new TestLease());

    private sealed class TestPresentation : IGpuGraphPresentation
    {
        public required Func<CancellationToken, ValueTask<GpuGraphPresentationTarget>> Acquire { get; init; }
        public bool FailPresent { get; init; }
        public int PresentCount { get; private set; }
        public int DiscardCount { get; private set; }
        public GpuGraphCompletion? RetiredCompletion { get; private set; }
        public ValueTask<GpuGraphPresentationTarget> AcquireNextTargetAsync(CancellationToken cancellationToken = default) => Acquire(cancellationToken);
        public void Present(GpuGraphPresentationTarget target, GpuGraphCompletion completion)
        { PresentCount++; if (FailPresent) { throw new InvalidOperationException("present"); } target.Ownership.Dispose(); }
        public void Retire(GpuGraphPresentationTarget target, GpuGraphCompletion completion) => RetiredCompletion = completion;
        public void Discard(GpuGraphPresentationTarget target) { DiscardCount++; target.Ownership.Dispose(); }
    }
    internal sealed class TestLease(Action? release = null) : IDisposable
    {
        public int Disposals { get; private set; }
        public void Dispose() { Disposals++; release?.Invoke(); }
    }
    private sealed class TestRuntime : IGpuRenderRuntime
    {
        public Guid Id { get; } = Guid.NewGuid();
        public TestResources Facade { get; } = new();
        public IGpuGraphResources Resources => Facade;
        public Func<ValueTask<GpuRenderGraphExecution>>? Submit { get; init; }
        public TestLease ExecutionOwnership { get; } = new();
        public int SubmitCount { get; private set; }
        public bool IsDisposed { get; private set; }
        public ValueTask<GpuRenderGraphExecution> SubmitAsync(GpuRenderGraphPlan plan, GpuRenderGraphBindings? bindings = null, CancellationToken cancellationToken = default)
        { plan.ValidateBindings(bindings); SubmitCount++; return Submit?.Invoke() ?? new(CreateExecution()); }
        public GpuRenderGraphExecution CreateExecution() => new(new(() => true, _ => ValueTask.CompletedTask), new Dictionary<GpuRenderGraphResource, GpuGraphResourceRef>(), ExecutionOwnership);
        public ValueTask WaitIdleAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public void StopAccepting() { }
        public ValueTask DisposeAsync() { IsDisposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class TestResources : IGpuGraphResources
    {
        public int UseCount { get; private set; }
        public IDisposable AcquireUse(GpuGraphResourceRef reference) { UseCount++; return new TestLease(() => UseCount--); }
        public GpuGraphResourceScope CreateScope() => throw new NotSupportedException();
        public GpuGraphResourcePin Pin(GpuGraphResourceRef reference) => throw new NotSupportedException();
        public void Collect() { }
        public void Trim() { }
    }
}
