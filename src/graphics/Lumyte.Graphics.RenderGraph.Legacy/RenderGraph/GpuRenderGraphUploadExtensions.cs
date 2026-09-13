namespace Lumyte.Graphics.RenderGraph.Legacy;

public static class GpuRenderGraphUploadExtensions
{
    /// <summary>Adds an upload pass whose dependency participates in later compute and draw passes.</summary>
    public static GpuRenderGraphPassBuilder AddTextureUpload(
        this GpuRenderGraph graph,
        string name,
        GpuMemoryAddress source,
        GpuRenderGraphTexture destination,
        GpuTextureCopyFootprint footprint)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (source.IsNull) { throw new ArgumentException("Upload source cannot be null.", nameof(source)); }
        footprint.Validate();
        if (source.Length != 0 && source.Length < footprint.RequiredBytes)
        {
            throw new ArgumentException("Upload source is smaller than the copy footprint.", nameof(source));
        }
        if (footprint.Width > destination.Description.Width || footprint.Height > destination.Description.Height)
        {
            throw new ArgumentException("Copy footprint exceeds the destination texture.", nameof(footprint));
        }
        if ((destination.Description.Usage & GpuTextureUsage.CopyDestination) == 0)
        {
            throw new ArgumentException("Destination texture must allow copy destination usage.", nameof(destination));
        }

        var state = new TextureUploadState(source, destination, footprint);
        return graph.AddPass(
                name,
                state,
                static (context, upload) => context.Commands.CopyMemoryToTexture(
                    upload.Source,
                    context.GetTexture(upload.Destination),
                    upload.Footprint))
            .Write(destination, GpuStage.Copy);
    }

    private readonly record struct TextureUploadState(
        GpuMemoryAddress Source,
        GpuRenderGraphTexture Destination,
        GpuTextureCopyFootprint Footprint);
}
