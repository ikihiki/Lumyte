using P = Lumyte.Graphics.Portable;
using N = WebGpuSharp;
using F = WebGpuSharp.FFI;

namespace Lumyte.Graphics.WebGPU;

public sealed partial class WebGpuBackend
{
    private unsafe bool TryEncodeTextureCopy(RecordedCommand command, F.CommandEncoderHandle encoder,
        HashSet<Task<IReadOnlyList<P.GpuDiagnostic>>> dependencies)
    {
        switch (command)
        {
            case CopyBufferToTextureCommand copy:
            {
                BufferResource source = RequireBuffer(copy.Source.Buffer);
                TextureResource texture = RequireTexture(copy.Texture);
                dependencies.Add(source.Diagnostics);
                dependencies.Add(texture.Diagnostics);
                var buffer = new F.TexelCopyBufferInfoFFI
                { Buffer = source.Handle, Layout = MapTextureCopyLayout(copy.Source.Offset, copy.Footprint) };
                var target = TextureCopyInfo(texture, copy.Footprint);
                var extent = TextureCopyExtent(copy.Footprint);
                F.WebGPU_FFI.CommandEncoderCopyBufferToTexture(encoder, &buffer, &target, &extent);
                return true;
            }
            case CopyTextureToBufferCommand copy:
            {
                TextureResource texture = RequireTexture(copy.Texture);
                BufferResource destination = RequireBuffer(copy.Destination.Buffer);
                dependencies.Add(texture.Diagnostics);
                dependencies.Add(destination.Diagnostics);
                var source = TextureCopyInfo(texture, copy.Footprint);
                var buffer = new F.TexelCopyBufferInfoFFI
                { Buffer = destination.Handle, Layout = MapTextureCopyLayout(copy.Destination.Offset, copy.Footprint) };
                var extent = TextureCopyExtent(copy.Footprint);
                F.WebGPU_FFI.CommandEncoderCopyTextureToBuffer(encoder, &source, &buffer, &extent);
                return true;
            }
            case CopyTextureCommand copy:
            {
                TextureResource sourceTexture = RequireTexture(copy.Source);
                TextureResource destinationTexture = RequireTexture(copy.Destination);
                dependencies.Add(sourceTexture.Diagnostics);
                dependencies.Add(destinationTexture.Diagnostics);
                var source = TextureCopyInfo(sourceTexture, copy.SourceFootprint);
                var destination = TextureCopyInfo(destinationTexture, copy.DestinationFootprint);
                var extent = TextureCopyExtent(copy.SourceFootprint);
                F.WebGPU_FFI.CommandEncoderCopyTextureToTexture(encoder, &source, &destination, &extent);
                return true;
            }
            default: return false;
        }
    }

    private static N.TexelCopyBufferLayout MapTextureCopyLayout(ulong offset, P.GpuTextureCopyFootprint footprint)
    {
        uint rowPitch = footprint.RowPitch == 0 ? F.WebGPU_FFI.COPY_STRIDE_UNDEFINED : checked((uint)footprint.RowPitch);
        if (footprint.RowPitch == F.WebGPU_FFI.COPY_STRIDE_UNDEFINED)
        { throw new ArgumentOutOfRangeException(nameof(footprint), "An explicit row pitch cannot use WebGPU's undefined sentinel."); }
        uint rowsPerImage = F.WebGPU_FFI.COPY_STRIDE_UNDEFINED;
        if (footprint.ImagePitch != 0)
        {
            if (footprint.RowPitch == 0 || footprint.ImagePitch % footprint.RowPitch != 0)
            { throw new ArgumentException("ImagePitch must be a whole multiple of an explicit RowPitch to map without loss.", nameof(footprint)); }
            rowsPerImage = checked((uint)(footprint.ImagePitch / footprint.RowPitch));
            if (rowsPerImage == F.WebGPU_FFI.COPY_STRIDE_UNDEFINED)
            { throw new ArgumentOutOfRangeException(nameof(footprint), "An explicit image pitch cannot map to WebGPU's undefined sentinel."); }
        }
        return new() { Offset = offset, BytesPerRow = rowPitch, RowsPerImage = rowsPerImage };
    }

    private static unsafe F.TexelCopyTextureInfoFFI TextureCopyInfo(TextureResource texture, P.GpuTextureCopyFootprint footprint)
        => new()
        {
            Texture = texture.Handle,
            MipLevel = footprint.Mip,
            Aspect = MapTextureAspect(footprint.Aspect),
            Origin = new() { X = footprint.Origin.X, Y = footprint.Origin.Y, Z = footprint.Origin.Z },
        };

    private static N.Extent3D TextureCopyExtent(P.GpuTextureCopyFootprint footprint)
        => new() { Width = footprint.Extent.Width, Height = footprint.Extent.Height, DepthOrArrayLayers = footprint.Extent.Depth };
}
