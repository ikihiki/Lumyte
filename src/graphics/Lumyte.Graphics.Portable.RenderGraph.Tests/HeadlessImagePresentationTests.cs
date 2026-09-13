using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.RenderGraph.Conformance;

namespace Lumyte.Graphics.Portable.RenderGraph.Tests;

public sealed class HeadlessImagePresentationTests
{
    [Fact]
    public async Task NextAcquireWaitsForThePreviousTargetsGpuUse()
    {
        var runtime = new TestRuntime();
        await using HeadlessImagePresentation presentation = await HeadlessImagePresentation.CreateAsync(runtime, new(4, 4, GpuFormat.Rgba8Unorm));
        GpuGraphPresentationTarget first = await presentation.AcquireNextTargetAsync();
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<GpuGraphPresentationTarget> next = presentation.AcquireNextTargetAsync().AsTask();

        presentation.Present(first, new(() => ended.Task.IsCompleted, ct => new(ended.Task.WaitAsync(ct))));
        Assert.False(next.IsCompleted);
        ended.SetResult();
        GpuGraphPresentationTarget second = await next;

        Assert.Equal((2, 1), (presentation.AcquiredCount, presentation.PresentedCount));
        Assert.Equal(2, runtime.TrackingResources.Pins);
        presentation.Discard(second);
    }

    [Fact]
    public async Task DisposalCancelsWaitingAcquiresAndWaitsForUnsubmittedTargets()
    {
        var runtime = new TestRuntime();
        HeadlessImagePresentation presentation = await HeadlessImagePresentation.CreateAsync(runtime, new(4, 4, GpuFormat.Rgba8Unorm));
        GpuGraphPresentationTarget acquired = await presentation.AcquireNextTargetAsync();
        Task<GpuGraphPresentationTarget> waiting = presentation.AcquireNextTargetAsync().AsTask();

        Task disposal = presentation.DisposeAsync().AsTask();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.False(disposal.IsCompleted);
        presentation.Discard(acquired);
        await disposal;

        Assert.Equal(0, runtime.TrackingResources.Pins);
    }

    [Fact]
    public async Task UncertainGpuUseRetainsTheTargetAndFailsDisposal()
    {
        var runtime = new TestRuntime();
        HeadlessImagePresentation presentation = await HeadlessImagePresentation.CreateAsync(runtime, new(4, 4, GpuFormat.Rgba8Unorm));
        GpuGraphPresentationTarget acquired = await presentation.AcquireNextTargetAsync();

        presentation.Retire(acquired, new(() => false, _ => ValueTask.FromException(new InvalidOperationException("GPU use unknown"))));
        AggregateException error = await Assert.ThrowsAsync<AggregateException>(() => presentation.DisposeAsync().AsTask());

        Assert.Contains("GPU use unknown", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(2, runtime.TrackingResources.Pins);
    }

    private sealed class TestRuntime : IGpuRenderRuntime
    {
        public Guid Id { get; } = Guid.NewGuid();
        public TestResources TrackingResources { get; } = new();
        public IGpuGraphResources Resources => TrackingResources;
        public void StopAccepting() { }
        public ValueTask WaitIdleAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public ValueTask<GpuRenderGraphExecution> SubmitAsync(GpuRenderGraphPlan plan, GpuRenderGraphBindings? bindings = null, CancellationToken cancellationToken = default)
        {
            var exports = plan.Exports.ToDictionary(resource => resource,
                resource => (GpuGraphResourceRef)new Texture(Id, ((GpuRenderGraphTexture)resource).Description));
            return ValueTask.FromResult(new GpuRenderGraphExecution(new(() => true, _ => ValueTask.CompletedTask), exports, new Noop()));
        }
    }
    private sealed class Texture(Guid runtimeId, GpuGraphTextureDescription description) : GpuGraphTextureRef(runtimeId, Guid.NewGuid(), description);
    private sealed class Noop : IDisposable { public void Dispose() { } }
    private sealed class TestResources : IGpuGraphResources
    {
        internal int Pins { get; set; }
        public GpuGraphResourceScope CreateScope() => throw new NotSupportedException();
        public GpuGraphResourcePin Pin(GpuGraphResourceRef reference) { Pins++; return new PinLease(this); }
        public IDisposable AcquireUse(GpuGraphResourceRef reference) => throw new NotSupportedException();
        public void Collect() { }
        public void Trim() { }
        private sealed class PinLease(TestResources owner) : GpuGraphResourcePin
        {
            private bool disposed;
            public override void Dispose() { if (!disposed) { disposed = true; owner.Pins--; } }
        }
    }
}
