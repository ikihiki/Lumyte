using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Native.RenderGraph;

/// <summary>Imports the acquired native surface image into this runtime without exposing its ABI to the consumer.</summary>
public sealed class NativeGraphPresentation(NativeGraphResources resources, INativeGpuSurface surface, Func<(uint Width, uint Height)> size)
    : GpuSurfacePresentation
{
    private NativeGpuSurfaceImage? image;
    protected override async ValueTask<GpuGraphPresentationTarget> AcquireCoreAsync(CancellationToken cancellationToken)
    {
        await resources.Work.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var extent = size();
            image = await surface.AcquireAsync(extent.Width, extent.Height, cancellationToken).ConfigureAwait(false);
            try
            {
                var import = resources.ImportTexture(image.Texture, image.Description, new Borrowed());
                return new(import.Reference, import);
            }
            catch { await surface.DiscardAsync(image).ConfigureAwait(false); image = null; throw; }
        }
        finally { resources.Work.Release(); }
    }
    protected override async ValueTask ReturnCoreAsync(GpuGraphPresentationTarget target, bool present)
    {
        await resources.Work.WaitAsync().ConfigureAwait(false);
        try
        {
            target.Ownership.Dispose();
            resources.Collect();
            if (present)
            { await surface.PresentAsync(image!).ConfigureAwait(false); }
            else
            { await surface.DiscardAsync(image!).ConfigureAwait(false); }
            image = null;
        }
        finally { resources.Work.Release(); }
    }
    protected override ValueTask DisposeCoreAsync() => surface.DisposeAsync();
    private sealed class Borrowed : IDisposable { public void Dispose() { } }
}
