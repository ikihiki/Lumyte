namespace Lumyte.Graphics.RenderGraph;

/// <summary>Owns reusable render-graph scheduling state for a sequence of frames.</summary>
public sealed class GpuRenderContext : IDisposable
{
    private readonly IGpuBackend backend;
    private readonly bool ownsBackend;
    private readonly GpuRetirementQueue retirementQueue;
    private bool disposed;

    public GpuRenderContext(
        IGpuBackend backend,
        int maximumFramesInFlight = 3,
        int maximumCachedPlans = 64,
        bool ownsBackend = false)
    {
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        this.ownsBackend = ownsBackend;
        retirementQueue = new(backend, maximumFramesInFlight);
        PlanCache = new(maximumCachedPlans);
    }

    public IGpuBackend Backend => backend;
    public GpuRenderGraphPlanCache PlanCache { get; }

    public GpuFrame BeginFrame(IGpuPresentationAdapter presentation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(presentation);
        retirementQueue.Collect();
        return new(this, presentation, presentation.AcquireNextTarget().Validate());
    }

    internal GpuRenderGraphExecution Submit(GpuRenderGraph graph)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return graph.Compile(PlanCache).ExecuteAsync(backend, retirementQueue);
    }

    public void Dispose()
    {
        if (disposed) { return; }
        retirementQueue.Dispose();
        if (ownsBackend) { backend.Dispose(); }
        disposed = true;
    }
}
