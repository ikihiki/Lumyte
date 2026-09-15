using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Portable.RenderGraph;

/// <summary>Imports the acquired Portable surface image into this runtime. The surface owns its GPU object.</summary>
public sealed class PortableGraphPresentation(PortableGraphResources resources, IPortableGpuSurface surface, Func<(uint Width, uint Height)> size)
    : GpuSurfacePresentation
{
    private GpuSurfaceImage? image;
    protected override async ValueTask<GpuGraphPresentationTarget> AcquireCoreAsync(CancellationToken cancellationToken)
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
    protected override async ValueTask ReturnCoreAsync(GpuGraphPresentationTarget target, bool present)
    {
        target.Ownership.Dispose();
        resources.Collect();
        if (present)
        { await surface.PresentAsync(image!).ConfigureAwait(false); }
        else
        { await surface.DiscardAsync(image!).ConfigureAwait(false); }
        image = null;
    }
    protected override ValueTask DisposeCoreAsync() => surface.DisposeAsync();
    private sealed class Borrowed : IDisposable { public void Dispose() { } }
}
