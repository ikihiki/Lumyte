namespace Lumyte.Graphics.RenderGraph.Tests;

public sealed class SurfacePresentationTests
{
    [Fact]
    public async Task FailedReturnOfCancelledAcquisitionRetainsOwnership()
    {
        using var cancellation = new CancellationTokenSource();
        var surface = new Surface
        {
            BeforeAcquire = _ => { cancellation.Cancel(); return ValueTask.CompletedTask; },
            Return = (_, _) => ValueTask.FromException(new InvalidOperationException("discard failed"))
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => surface.AcquireNextTargetAsync(cancellation.Token).AsTask());
        Assert.Equal("discard failed", error.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => surface.DisposeAsync().AsTask());

        Assert.False(surface.Disposed);
    }
    [Fact]
    public async Task NextFrameWaitsForGpuAndPresentationUse()
    {
        var gpu = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var displayed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var surface = new Surface { Return = (_, _) => new(displayed.Task) };
        var target = await surface.AcquireNextTargetAsync();
        surface.Present(target, new(() => gpu.Task.IsCompleted, _ => new(gpu.Task)));
        var next = surface.AcquireNextTargetAsync().AsTask();
        Assert.False(next.IsCompleted);

        gpu.SetResult();
        await surface.ReturnStarted.Task;
        Assert.False(next.IsCompleted);
        displayed.SetResult();
        var second = await next;
        surface.Discard(second);
        await surface.WaitForPresentationAsync();

        Assert.Equal(2, surface.ReturnCount);
    }
    [Fact]
    public async Task FailedPixelsAreDiscardedAfterUseEnds()
    {
        await using var surface = new Surface();
        var target = await surface.AcquireNextTargetAsync();
        surface.Present(target, new(() => true, _ => ValueTask.FromException(new InvalidOperationException("pixels"))));
        await surface.WaitForPresentationAsync();
        Assert.False(surface.LastPresent);
    }
    [Fact]
    public async Task UnknownGpuUseRetainsTheSurface()
    {
        var surface = new Surface();
        var target = await surface.AcquireNextTargetAsync();
        surface.Retire(target, new(() => false, _ => ValueTask.FromException(new InvalidOperationException("device lost"))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => surface.WaitForPresentationAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => surface.DisposeAsync().AsTask());

        Assert.Equal(0, surface.ReturnCount);
        Assert.False(surface.Disposed);
    }
    [Fact]
    public async Task ClosingCancelsAWaitingAcquire()
    {
        var surface = new Surface();
        var target = await surface.AcquireNextTargetAsync();
        var gpu = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        surface.Present(target, new(() => gpu.Task.IsCompleted, _ => new(gpu.Task)));
        var next = surface.AcquireNextTargetAsync().AsTask();

        var closing = surface.DisposeAsync().AsTask();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next);
        Assert.False(closing.IsCompleted);
        gpu.SetResult();
        await closing;

        Assert.True(surface.Disposed);
    }
    [Fact]
    public async Task UnsubmittedTargetMustBeReturnedBeforeShutdown()
    {
        var surface = new Surface();
        var target = await surface.AcquireNextTargetAsync();
        Assert.Contains("unsubmitted", Assert.Throws<InvalidOperationException>(() => surface.DisposeAsync()).Message);
        surface.Discard(target);
        await surface.DisposeAsync();
        Assert.True(surface.Disposed);
    }
    private sealed class Surface : GpuSurfacePresentation
    {
        public Func<CancellationToken, ValueTask>? BeforeAcquire { get; init; }
        public Func<GpuGraphPresentationTarget, bool, ValueTask> Return { get; init; } = (_, _) => ValueTask.CompletedTask;
        public TaskCompletionSource ReturnStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ReturnCount { get; private set; }
        public bool LastPresent { get; private set; }
        public bool Disposed { get; private set; }
        protected override async ValueTask<GpuGraphPresentationTarget> AcquireCoreAsync(CancellationToken cancellationToken)
        {
            if (BeforeAcquire is not null)
            { await BeforeAcquire(cancellationToken); }
            return new(new BindingsTests.TestTexture(Guid.NewGuid(), new(4, 4, GpuFormat.Rgba8Unorm)), new PresentationTests.TestLease());
        }
        protected override async ValueTask ReturnCoreAsync(GpuGraphPresentationTarget target, bool present)
        { LastPresent = present; ReturnCount++; ReturnStarted.TrySetResult(); await Return(target, present); target.Ownership.Dispose(); }
        protected override ValueTask DisposeCoreAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
