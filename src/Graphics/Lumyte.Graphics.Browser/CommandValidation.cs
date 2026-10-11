using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal static class CommandValidation
{
    internal static void Scope(BarrierScope scope)
    {
        const PipelineStage Stages = PipelineStage.AllCommands | PipelineStage.Host;
        const ResourceAccess Access = ResourceAccess.HostRead | ResourceAccess.HostWrite | ResourceAccess.CopyRead | ResourceAccess.CopyWrite | ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite | ResourceAccess.ColorRead | ResourceAccess.ColorWrite | ResourceAccess.IndexRead | ResourceAccess.IndirectRead | ResourceAccess.DepthStencilRead | ResourceAccess.DepthStencilWrite;
        if ((scope.Stages & ~Stages) != 0 || (scope.Access & ~Access) != 0)
        {
            throw new ArgumentException("Unknown dependency flags.");
        }

        CheckAccess(scope, ResourceAccess.HostRead | ResourceAccess.HostWrite, PipelineStage.Host);
        CheckAccess(scope, ResourceAccess.CopyRead | ResourceAccess.CopyWrite, PipelineStage.Copy);
        CheckAccess(scope, ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite, PipelineStage.VertexShader | PipelineStage.FragmentShader | PipelineStage.ComputeShader);
        CheckAccess(scope, ResourceAccess.IndexRead, PipelineStage.IndexInput);
        CheckAccess(scope, ResourceAccess.IndirectRead, PipelineStage.DrawIndirect);
        CheckAccess(scope, ResourceAccess.DepthStencilRead | ResourceAccess.DepthStencilWrite, PipelineStage.DepthStencil);
        CheckAccess(scope, ResourceAccess.ColorRead | ResourceAccess.ColorWrite, PipelineStage.ColorOutput);
    }

    internal static void Region(TextureCopyRegion region, TextureUsage usage)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(region.Texture);
        (uint width, uint height) = region.Texture.GetMipSize(region.MipLevel);
        if (region.Width == 0 || region.Height == 0 || region.OriginX > width || region.Width > width - region.OriginX || region.OriginY > height || region.Height > height - region.OriginY ||
            region.ArrayLayerCount == 0 || region.BaseArrayLayer >= region.Texture.ArrayLayers || region.ArrayLayerCount > region.Texture.ArrayLayers - region.BaseArrayLayer || (region.Texture.Usage & usage) == 0)
        {
            throw new ArgumentException("Invalid texture copy region or usage.");
        }
    }

    internal static void Layout(BufferTextureCopyLayout layout, TextureCopyRegion region, TextureCopyLayout constraints)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ulong rowSize = checked((ulong)region.Width * constraints.BytesPerTexel);
        ulong required = checked((((ulong)region.ArrayLayerCount - 1) * layout.RowsPerImage * layout.BytesPerRow) + (((ulong)region.Height - 1) * layout.BytesPerRow) + rowSize);
        if (layout.BytesPerRow < rowSize || layout.BytesPerRow % constraints.BytesPerRowAlignment != 0 || layout.RowsPerImage < region.Height ||
            layout.Buffer.OffsetInBytes % constraints.BufferOffsetAlignmentInBytes != 0 || layout.Buffer.SizeInBytes < required)
        {
            throw new ArgumentException("Invalid explicit row layout, alignment or buffer range.");
        }
    }

    internal static void Range(IGraphicsTexture texture, TextureSubresourceRange range)
    {
        if (range.MipLevelCount == 0 || range.BaseMipLevel >= texture.MipLevels || range.MipLevelCount > texture.MipLevels - range.BaseMipLevel ||
            range.ArrayLayerCount == 0 || range.BaseArrayLayer >= texture.ArrayLayers || range.ArrayLayerCount > texture.ArrayLayers - range.BaseArrayLayer)
        {
            throw new ArgumentException("Invalid texture subresource range.");
        }
    }

    internal static void TextureState(IGraphicsTexture texture, Abstractions.TextureState state, bool after)
    {
        TextureUsage usage = state switch
        {
            Abstractions.TextureState.Undefined when !after => 0,
            Abstractions.TextureState.Present when texture is BrowserTexture { SurfaceFrame: not null } => 0,
            Abstractions.TextureState.CopySource => TextureUsage.CopySource,
            Abstractions.TextureState.CopyDestination => TextureUsage.CopyDestination,
            Abstractions.TextureState.Sampled => TextureUsage.Sampled,
            Abstractions.TextureState.ColorAttachment when texture.Format is not (TextureFormat.Depth32Float or TextureFormat.Depth24Stencil8) => TextureUsage.RenderAttachment,
            Abstractions.TextureState.DepthStencilAttachment when texture.Format is TextureFormat.Depth32Float or TextureFormat.Depth24Stencil8 => TextureUsage.RenderAttachment,
            _ => throw new ArgumentException("Invalid texture state."),
        };
        if ((texture.Usage & usage) != usage)
        {
            throw new ArgumentException("Texture state conflicts with allocation usage.");
        }
    }

    internal static void BufferAccess<T>(IGraphicsBuffer<T> buffer, BarrierScope scope)
        where T : unmanaged
    {
        ResourceAccess access = scope.Access;
        if (((access & ResourceAccess.CopyRead) != 0 && (buffer.Usage & BufferUsage.CopySource) == 0) ||
            ((access & ResourceAccess.CopyWrite) != 0 && (buffer.Usage & BufferUsage.CopyDestination) == 0) ||
            ((access & ResourceAccess.ShaderRead) != 0 && (buffer.Usage & BufferUsage.ShaderRead) == 0) ||
            ((access & ResourceAccess.ShaderWrite) != 0 && (buffer.Usage & BufferUsage.ShaderWrite) == 0) ||
            ((access & ResourceAccess.HostWrite) != 0 && buffer.Memory != MemoryPreference.Upload) ||
            ((access & ResourceAccess.HostRead) != 0 && buffer.Memory != MemoryPreference.Readback) ||
            ((access & ResourceAccess.IndexRead) != 0 && (buffer.Usage & BufferUsage.Index) == 0) ||
            ((access & ResourceAccess.IndirectRead) != 0 && (buffer.Usage & BufferUsage.Indirect) == 0) ||
            (access & (ResourceAccess.ColorRead | ResourceAccess.ColorWrite | ResourceAccess.DepthStencilRead | ResourceAccess.DepthStencilWrite)) != 0)
        {
            throw new ArgumentException("Buffer dependency conflicts with allocation usage or memory preference.");
        }
    }

    internal static void TextureAccess(Abstractions.TextureState state, BarrierScope scope)
    {
        ResourceAccess permitted = state switch
        {
            Abstractions.TextureState.Undefined => ResourceAccess.CopyRead | ResourceAccess.CopyWrite | ResourceAccess.ShaderRead | ResourceAccess.ColorRead | ResourceAccess.ColorWrite | ResourceAccess.DepthStencilRead | ResourceAccess.DepthStencilWrite,
            Abstractions.TextureState.CopySource => ResourceAccess.CopyRead,
            Abstractions.TextureState.CopyDestination => ResourceAccess.CopyWrite,
            Abstractions.TextureState.Sampled => ResourceAccess.ShaderRead,
            Abstractions.TextureState.ColorAttachment => ResourceAccess.ColorRead | ResourceAccess.ColorWrite,
            Abstractions.TextureState.DepthStencilAttachment => ResourceAccess.DepthStencilRead | ResourceAccess.DepthStencilWrite,
            _ => 0,
        };
        if ((scope.Access & ~permitted) != 0)
        {
            throw new ArgumentException("Texture dependency conflicts with its declared state.");
        }
    }

    private static void CheckAccess(BarrierScope scope, ResourceAccess accesses, PipelineStage stages)
    {
        if ((scope.Access & accesses) != 0 && (scope.Stages & stages) == 0)
        {
            throw new ArgumentException("Access does not match the dependency stages.");
        }
    }
}
