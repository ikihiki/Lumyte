using Lumyte.Graphics.Passes;
using Lumyte.Graphics.Portable.RenderGraph;

namespace Lumyte.Graphics.Portable.Passes;

public static class PortableImageProcessingPasses
{
    /// <summary>Registers CPU factories; shader and GPU objects are initialized only when a runtime builds work.</summary>
    public static PortableRenderPassRegistry AddImageProcessing(this PortableRenderPassRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register(ClearPassContract.Instance, static _ => new PortableClearPass());
        registry.Register(TextureCopyPassContract.Instance, static _ => new PortableTextureCopyPass());
        registry.Register(OutputPassContract.Instance, static services => new PortableOutputPass(services));
        return registry;
    }
}

internal sealed class PortableClearPass : IPortableRenderPass<ClearPassRequest, TexturePassResult>
{
    public ValueTask BuildAsync(PortablePassBuildContext context, ClearPassRequest request,
        TexturePassResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PortablePassTexture texture = context.ImportTexture(request.Target);
        TextureClearValue value = context.GetInput(request.Value);
        GpuTextureDescription description = texture.Description;
        var attachments = new List<(PortablePassView View, uint? DepthSlice)>();
        for (uint mip = 0; mip < description.MipCount; mip++)
        {
            for (uint layer = 0; layer < description.LayerCount; layer++)
            {
                PortablePassView view = context.CreateView($"Clear attachment {mip}/{layer}", texture,
                    new(Dimension: description.Dimension == GpuTextureDimension.Texture3D ? GpuTextureViewDimension.Texture3D : GpuTextureViewDimension.Texture2D,
                        BaseMip: mip, MipCount: 1, BaseLayer: layer, LayerCount: 1));
                uint depth = description.Dimension == GpuTextureDimension.Texture3D ? Math.Max(1, description.Depth >> (int)mip) : 1;
                for (uint slice = 0; slice < depth; slice++)
                {
                    attachments.Add((view, description.Dimension == GpuTextureDimension.Texture3D ? slice : null));
                }
            }
        }
        // One declared Write initializes the entire texture, including every mip, layer and slice.
        context.AddPass("Clear", (Attachments: attachments.ToArray(), Value: value, description.Format), static (record, state) =>
        {
            foreach (var item in state.Attachments)
            {
                GpuTextureView attachment = record.GetTextureView(item.View);
                if (state.Value.IsDepthStencil)
                {
                    bool stencil = state.Format == GpuFormat.Depth24PlusStencil8;
                    record.Commands.BeginRendering([], new(attachment,
                        DepthLoadOperation: GpuAttachmentLoadOperation.Clear, DepthStoreOperation: GpuAttachmentStoreOperation.Store,
                        StencilLoadOperation: stencil ? GpuAttachmentLoadOperation.Clear : null,
                        StencilStoreOperation: stencil ? GpuAttachmentStoreOperation.Store : null,
                        ClearValue: new(state.Value.Depth, state.Value.Stencil)));
                }
                else
                {
                    var color = state.Value.ColorValue;
                    record.Commands.BeginRendering([new(attachment, GpuAttachmentLoadOperation.Clear,
                        ClearColor: new(color.X * color.W, color.Y * color.W, color.Z * color.W, color.W), DepthSlice: item.DepthSlice)]);
                }
                record.Commands.EndRendering();
            }
        }).Write(texture, value.IsDepthStencil ? PortablePassUsage.DepthStencilAttachment : PortablePassUsage.ColorAttachment);
        return ValueTask.CompletedTask;
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class PortableTextureCopyPass : IPortableRenderPass<TextureCopyPassRequest, TexturePassResult>
{
    public ValueTask BuildAsync(PortablePassBuildContext context, TextureCopyPassRequest request,
        TexturePassResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PortablePassTexture source = context.ImportTexture(request.Source), target = context.ImportTexture(request.Target);
        context.AddPass("Texture copy", (source, target), static (record, state) =>
        {
            GpuTextureDescription description = state.source.Description;
            for (uint mip = 0; mip < description.MipCount; mip++)
            {
                uint depth = description.Dimension == GpuTextureDimension.Texture3D ? Math.Max(1, description.Depth >> (int)mip) : description.LayerCount;
                var footprint = new GpuTextureCopyFootprint(mip, GpuTextureAspect.All, default,
                    new(Math.Max(1, description.Width >> (int)mip), Math.Max(1, description.Height >> (int)mip), depth));
                record.Commands.CopyTexture(record.GetTexture(state.source), footprint, record.GetTexture(state.target), footprint);
            }
        }).Read(source, PortablePassUsage.CopySource).Write(target, PortablePassUsage.CopyDestination);
        return ValueTask.CompletedTask;
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
