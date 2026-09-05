namespace Lumyte.Graphics.RenderGraph;

public sealed class GpuRenderGraphExportedBuffer
{
    private readonly GpuRenderGraphExecution execution;
    private readonly IGpuBackend backend;

    internal GpuRenderGraphExportedBuffer(
        GpuRenderGraphExecution execution,
        IGpuBackend backend,
        GpuBufferHandle buffer,
        GpuBufferDescription description)
    {
        this.execution = execution;
        this.backend = backend;
        Buffer = buffer;
        Description = description;
    }

    public GpuBufferHandle Buffer { get; }
    public GpuBufferDescription Description { get; }

    internal void RequireAlive() => execution.RequireAlive();

    internal void RequireBackend(IGpuBackend candidate)
    {
        execution.RequireAlive();
        if (!ReferenceEquals(backend, candidate))
        {
            throw new InvalidOperationException(
                "An exported buffer must be imported by its owning backend.");
        }
    }

    internal IDisposable AcquireImportLease(IGpuBackend candidate)
    {
        RequireBackend(candidate);
        return execution.AcquireImportLease();
    }
}
