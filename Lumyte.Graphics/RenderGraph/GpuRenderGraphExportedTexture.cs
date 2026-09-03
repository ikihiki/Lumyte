namespace Lumyte.Graphics.RenderGraph;

public sealed class GpuRenderGraphExportedTexture
{
    private readonly GpuRenderGraphExecution execution;
    private readonly IGpuBackend backend;

    internal GpuRenderGraphExportedTexture(
        GpuRenderGraphExecution execution,
        IGpuBackend backend,
        GpuTextureHandle texture,
        GpuTextureDescription description)
    {
        this.execution = execution;
        this.backend = backend;
        Texture = texture;
        Description = description;
    }

    public GpuTextureHandle Texture { get; }
    public GpuTextureDescription Description { get; }

    internal void RequireAlive() => execution.RequireAlive();

    internal void RequireBackend(IGpuBackend candidate)
    {
        execution.RequireAlive();
        if (!ReferenceEquals(backend, candidate))
        {
            throw new InvalidOperationException(
                "An exported texture must be imported by its owning backend.");
        }
    }

    internal IDisposable AcquireImportLease(IGpuBackend candidate)
    {
        RequireBackend(candidate);
        return execution.AcquireImportLease();
    }
}
