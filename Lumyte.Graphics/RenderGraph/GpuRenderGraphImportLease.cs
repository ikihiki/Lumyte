namespace Lumyte.Graphics.RenderGraph;

internal sealed class GpuRenderGraphImportLease(GpuRenderGraphExecution owner) : IDisposable
{
    private GpuRenderGraphExecution? owner = owner;

    public void Dispose()
    {
        GpuRenderGraphExecution? current = Interlocked.Exchange(ref owner, null);
        current?.ReleaseImportLease();
    }
}
