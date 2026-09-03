namespace Lumyte.Graphics.RenderGraph;

public sealed class GpuRenderGraphExecution : IDisposable
{
    private IGpuBackend? backend;
    private readonly Dictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime> exported;
    private readonly GpuPersistentArena? arena;
    private readonly GpuMemoryAllocation[] retainedAllocations;
    private readonly bool ownsArena;
    private readonly GpuRetirementQueue? retirementQueue;
    private int importLeaseCount;
    private IReadOnlyList<Action>? deferredDisposal;

    internal GpuRenderGraphExecution(
        IGpuBackend backend,
        Dictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime> exported,
        GpuPersistentArena? arena,
        GpuMemoryAllocation[] retainedAllocations,
        bool ownsArena,
        GpuRetirementQueue? retirementQueue = null,
        GpuSubmissionToken completion = default)
    {
        this.backend = backend;
        this.exported = exported;
        this.arena = arena;
        this.retainedAllocations = retainedAllocations;
        this.ownsArena = ownsArena;
        this.retirementQueue = retirementQueue;
        Completion = completion;
    }

    /// <summary>The GPU submission associated with this execution. Synchronous executions are already complete.</summary>
    public GpuSubmissionToken Completion { get; }
    public bool IsComplete => Completion.IsComplete;

    public void WaitForCompletion() => Completion.Wait();

    public GpuRenderGraphExportedTexture GetTexture(GpuRenderGraphResource resource)
    {
        GpuRenderGraphResourceRuntime runtime = RequireExport(
            resource,
            GpuRenderGraphResourceKind.Texture);
        GpuTextureDescription description = runtime.Info.TextureDescription
            ?? throw new InvalidOperationException("The exported texture has no description.");
        return new(this, backend!, runtime.Texture, description);
    }

    public GpuRenderGraphExportedBuffer GetBuffer(GpuRenderGraphResource resource)
    {
        GpuRenderGraphResourceRuntime runtime = RequireExport(
            resource,
            GpuRenderGraphResourceKind.Buffer);
        GpuBufferDescription description = runtime.Info.BufferDescription
            ?? throw new InvalidOperationException("The exported buffer has no description.");
        return new(this, backend!, runtime.Buffer, description);
    }

    public void Dispose()
    {
        if (backend is not { } owner) { return; }
        GpuRenderGraphResourceRuntime[] resources = exported.Values.ToArray();
        exported.Clear();
        backend = null;

        var releases = new List<Action>();
        foreach (GpuRenderGraphResourceRuntime runtime in resources)
        {
            releases.Add(() => runtime.Dispose(owner));
        }
        if (arena is not null)
        {
            foreach (GpuMemoryAllocation allocation in retainedAllocations)
            {
                releases.Add(() => arena.Release(allocation));
            }
            if (ownsArena) { releases.Add(arena.Dispose); }
        }

        if (importLeaseCount == 0) { ScheduleDisposal(releases); }
        else { deferredDisposal = releases; }
    }

    internal void RequireAlive()
        => ObjectDisposedException.ThrowIf(backend is null, this);

    internal IDisposable AcquireImportLease()
    {
        RequireAlive();
        importLeaseCount++;
        return new GpuRenderGraphImportLease(this);
    }

    internal void ReleaseImportLease()
    {
        if (importLeaseCount <= 0)
        {
            throw new InvalidOperationException("Render-graph import lease was released more than once.");
        }
        importLeaseCount--;
        if (importLeaseCount == 0 && deferredDisposal is { } releases)
        {
            deferredDisposal = null;
            ScheduleDisposal(releases);
        }
    }

    private void ScheduleDisposal(IReadOnlyList<Action> releases)
    {
        if (retirementQueue is null)
        {
            foreach (Action release in releases) { release(); }
        }
        else { retirementQueue.Retire(Completion, releases); }
    }

    private GpuRenderGraphResourceRuntime RequireExport(
        GpuRenderGraphResource resource,
        GpuRenderGraphResourceKind kind)
    {
        RequireAlive();
        if (!exported.TryGetValue(resource, out GpuRenderGraphResourceRuntime? runtime))
        {
            throw new ArgumentException(
                "Resource was not exported by this execution.",
                nameof(resource));
        }
        if (runtime.Info.Kind != kind)
        {
            throw new ArgumentException(
                $"Resource is not a {kind.ToString().ToLowerInvariant()}.",
                nameof(resource));
        }
        return runtime;
    }
}
