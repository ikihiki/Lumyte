using Lumyte.Graphics.Native;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

public sealed unsafe partial class VulkanBackend
{
    // Every linear region is fully bound and every backing buffer has STORAGE_BUFFER usage.
    private const uint LinearAddressFlags = 0x00000002 | 0x00000004;

    internal static ImageAspectFlags TextureAspects(NativeGpuTextureAspect aspect) => aspect switch
    {
        NativeGpuTextureAspect.Color => ImageAspectFlags.ColorBit,
        NativeGpuTextureAspect.Depth => ImageAspectFlags.DepthBit,
        NativeGpuTextureAspect.Stencil => ImageAspectFlags.StencilBit,
        NativeGpuTextureAspect.DepthStencil => ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit,
        _ => throw new ArgumentOutOfRangeException(nameof(aspect)),
    };

    internal static NativeDeviceMemoryImageCopy TextureCopyRegion(
        GpuFormat format, NativeGpuRange range, NativeGpuTextureCopyFootprint footprint)
    {
        uint elementSize = footprint.Aspect switch
        {
            NativeGpuTextureAspect.Stencil => 1,
            NativeGpuTextureAspect.Depth => 4,
            NativeGpuTextureAspect.Color => format switch
            {
                GpuFormat.R8Unorm => 1,
                GpuFormat.Rg8Unorm => 2,
                GpuFormat.Rgba8Unorm or GpuFormat.Bgra8Unorm or GpuFormat.R32Float
                    or GpuFormat.Rgba8UnormSrgb or GpuFormat.Bgra8UnormSrgb => 4,
                _ => throw new ArgumentException("The format has no color transfer element.", nameof(format)),
            },
            _ => throw new ArgumentException("A texture copy requires one aspect.", nameof(footprint)),
        };
        // Byte strides must have an exact representation in Vulkan's texel/row stride fields.
        // Native pitch alignment, extent and range validity remain caller/native responsibilities.
        if (footprint.RowPitch == 0 || footprint.RowPitch % elementSize != 0
            || footprint.ImagePitch == 0 || footprint.ImagePitch % footprint.RowPitch != 0)
        {
            throw new ArgumentException("Byte pitches cannot be represented as Vulkan texel and row strides.", nameof(footprint));
        }
        return new()
        {
            SType = (StructureType)1000318002,
            AddressRange = new() { Address = range.GpuAddress, Size = range.Size },
            AddressFlags = LinearAddressFlags,
            AddressRowLength = checked((uint)(footprint.RowPitch / elementSize)),
            AddressImageHeight = checked((uint)(footprint.ImagePitch / footprint.RowPitch)),
            ImageSubresource = new(TextureAspects(footprint.Aspect), footprint.Mip, footprint.BaseLayer, footprint.LayerCount),
            ImageLayout = ImageLayout.General,
            ImageOffset = new(checked((int)footprint.Origin.X), checked((int)footprint.Origin.Y), checked((int)footprint.Origin.Z)),
            ImageExtent = new(footprint.Extent.Width, footprint.Extent.Height, footprint.Extent.Depth),
        };
    }

    private TextureRecord RequireCommandTexture(NativeGpuTextureHandle texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (texture is not TextureRecord record || !ReferenceEquals(record.Owner, this))
        {
            throw new ArgumentException("The texture belongs to another backend.", nameof(texture));
        }
        ObjectDisposedException.ThrowIf(record.Destroyed, texture);
        return record;
    }

    private void RequireCommandRange(NativeGpuRange range)
    {
        if (range.Region is not LinearRecord record || !ReferenceEquals(record.Owner, this))
        {
            throw new ArgumentException("The linear region belongs to another backend.", nameof(range));
        }
        ObjectDisposedException.ThrowIf(record.Destroyed, range.Region);
    }


}
