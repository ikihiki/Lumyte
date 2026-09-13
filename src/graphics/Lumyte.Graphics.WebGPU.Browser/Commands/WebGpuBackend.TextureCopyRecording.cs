using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Browser;

public sealed partial class WebGpuBackend
{
    private sealed record CopyBufferToTextureCommand(P.GpuBufferRange Source, P.GpuTextureHandle Texture,
        P.GpuTextureCopyFootprint Footprint) : RecordedCommand;
    private sealed record CopyTextureToBufferCommand(P.GpuTextureHandle Texture, P.GpuTextureCopyFootprint Footprint,
        P.GpuBufferRange Destination) : RecordedCommand;
    private sealed record CopyTextureCommand(P.GpuTextureHandle Source, P.GpuTextureCopyFootprint SourceFootprint,
        P.GpuTextureHandle Destination, P.GpuTextureCopyFootprint DestinationFootprint) : RecordedCommand;

    private sealed partial class CommandRecording
    {
        public override void CopyBufferToTexture(P.GpuBufferRange source, P.GpuTextureHandle texture,
            P.GpuTextureCopyFootprint footprint)
        {
            lock (Queue.Owner.gate)
            {
                RequireOutsidePass();
                P.GpuBufferRange range = Queue.Owner.ResolveTextureCopyRange(source, texture, footprint, nameof(source));
                Commands.Add(new CopyBufferToTextureCommand(range, texture, footprint));
            }
        }

        public override void CopyTextureToBuffer(P.GpuTextureHandle texture, P.GpuTextureCopyFootprint footprint,
            P.GpuBufferRange destination)
        {
            lock (Queue.Owner.gate)
            {
                RequireOutsidePass();
                P.GpuBufferRange range = Queue.Owner.ResolveTextureCopyRange(destination, texture, footprint, nameof(destination));
                Commands.Add(new CopyTextureToBufferCommand(texture, footprint, range));
            }
        }

        public override void CopyTexture(P.GpuTextureHandle source, P.GpuTextureCopyFootprint sourceFootprint,
            P.GpuTextureHandle destination, P.GpuTextureCopyFootprint destinationFootprint)
        {
            lock (Queue.Owner.gate)
            {
                RequireOutsidePass();
                Queue.Owner.RequireTexture(source);
                Queue.Owner.RequireTexture(destination);
                MapTextureAspect(sourceFootprint.Aspect);
                MapTextureAspect(destinationFootprint.Aspect);
                if (sourceFootprint.Extent != destinationFootprint.Extent)
                { throw new ArgumentException("Texture copy regions must have equal extents.", nameof(destinationFootprint)); }
                Commands.Add(new CopyTextureCommand(source, sourceFootprint, destination, destinationFootprint));
            }
        }
    }

    private P.GpuBufferRange ResolveTextureCopyRange(P.GpuBufferRange range, P.GpuTextureHandle handle,
        P.GpuTextureCopyFootprint footprint, string parameterName)
    {
        TextureResource texture = RequireTexture(handle);
        P.GpuBufferRange resolved = ResolveCommandRange(range);
        MapTextureAspect(footprint.Aspect);
        MapTextureCopyLayout(resolved.Offset, footprint);
        // No byte layout exists for the depth24plus aspect. Its buffer-copy rejection belongs to native validation.
        if (texture.Description.Format != GpuFormat.Depth24PlusStencil8 || footprint.Aspect == P.GpuTextureAspect.StencilOnly)
        {
            ulong required = footprint.RequiredBytes(texture.Description.Format);
            if (resolved.Length < required)
            { throw new ArgumentOutOfRangeException(parameterName, "The logical buffer range must cover the texture footprint's required bytes."); }
        }
        return resolved;
    }
}
