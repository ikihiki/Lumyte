using Lumyte.Graphics.Native;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;

namespace Lumyte.Graphics.DirectX12;

public sealed unsafe partial class DirectX12Backend
{
    private void EncodeTextureCopy(ComPtr<ID3D12GraphicsCommandList7> commands, NativeGpuRange memory,
        NativeGpuTextureHandle handle, NativeGpuTextureCopyFootprint copy, bool upload)
    {
        LinearRecord linear = RequireLinear(memory.Region);
        TextureRecord texture = RequireTexture(handle);
        ResourceDesc description = texture.Resource.GetDesc();
        bool volume = description.Dimension == ResourceDimension.Texture3D;
        // A 3D subresource has depth slices, but no array layers. Reject coordinates that the
        // native argument conversion would otherwise silently discard.
        if (volume ? copy.BaseLayer != 0 || copy.LayerCount != 1 : copy.Extent.Depth != 1)
        {
            throw new ArgumentException("The copy's array layers and depth must represent the texture dimension.", nameof(copy));
        }
        (uint plane, uint planeCount) = TexturePlanes(texture.Description.Format, copy.Aspect);
        if (planeCount != 1)
        {
            throw new ArgumentException("A texture copy must select one color, depth, or stencil aspect.", nameof(copy));
        }
        uint arrayCount = volume ? 1u : description.DepthOrArraySize;
        // Flattening mip/layer/plane into one index can turn an invalid mip or array layer
        // into a valid index of another layer/plane. Preserve these bounds before flattening.
        if (copy.Mip >= description.MipLevels || copy.BaseLayer >= arrayCount
            || copy.LayerCount > arrayCount - copy.BaseLayer)
        {
            throw new ArgumentOutOfRangeException(nameof(copy), "The mip and array layers must stay within their own subresource axes.");
        }
        uint subresource = checked(copy.Mip + copy.BaseLayer * description.MipLevels + plane * description.MipLevels * arrayCount);
        PlacedSubresourceFootprint native = default;
        ulong rowBytes = 0;
        ulong totalBytes = 0;
        // GetCopyableFootprints supplies the actual copy-plane format, including split depth/stencil.
        // Caller pitches and offsets below remain unchanged; no staging or repacking is introduced.
        device.GetCopyableFootprints(&description, subresource, 1, 0, &native, (uint*)null, &rowBytes, &totalBytes);
        if (totalBytes == ulong.MaxValue || native.Footprint.Width == 0)
        {
            throw new NativeGpuException("GetCopyableFootprints could not describe the selected subresource.", -1);
        }
        uint sliceCount = volume ? copy.Extent.Depth : copy.LayerCount;
        ulong elementBytes = rowBytes / native.Footprint.Width;
        ulong required = CopyByteCount(copy, sliceCount, elementBytes);
        if (required > memory.Size)
        {
            throw new ArgumentException("The memory range does not cover the texture copy bytes.", nameof(memory));
        }
        uint rowPitch = checked((uint)copy.RowPitch);
        bool wholePlane = copy.Origin.X == 0 && copy.Origin.Y == 0 && copy.Origin.Z == 0
            && copy.Extent.Width == native.Footprint.Width && copy.Extent.Height == native.Footprint.Height
            && native.Footprint.Depth == 1;
        for (uint slice = 0; slice < sliceCount; slice++)
        {
            var footprint = new SubresourceFootprint(native.Footprint.Format,
                copy.Extent.Width, copy.Extent.Height, 1, rowPitch);
            var placed = new PlacedSubresourceFootprint(checked(memory.Offset + slice * copy.ImagePitch), footprint);
            var bufferLocation = new TextureCopyLocation(linear.Resource.Handle, TextureCopyType.PlacedFootprint, placedFootprint: placed);
            var textureLocation = new TextureCopyLocation(texture.Resource.Handle, TextureCopyType.SubresourceIndex,
                subresourceIndex: checked(subresource + (volume ? 0u : slice * description.MipLevels)));
            uint z = checked(copy.Origin.Z + (volume ? slice : 0u));
            if (upload)
            {
                var box = new Box(0, 0, 0, copy.Extent.Width, copy.Extent.Height, 1);
                commands.CopyTextureRegion(&textureLocation, copy.Origin.X, copy.Origin.Y, z, &bufferLocation,
                    wholePlane ? null : &box);
            }
            else
            {
                var box = new Box(copy.Origin.X, copy.Origin.Y, z, checked(copy.Origin.X + copy.Extent.Width),
                    checked(copy.Origin.Y + copy.Extent.Height), checked(z + 1));
                commands.CopyTextureRegion(&bufferLocation, 0, 0, 0, &textureLocation, wholePlane ? null : &box);
            }
        }
    }

    internal static ulong CopyByteCount(NativeGpuTextureCopyFootprint copy, uint slices, ulong elementBytes)
    {
        if (slices == 0 || copy.Extent.Width == 0 || copy.Extent.Height == 0) { return 0; }
        return checked((slices - 1) * copy.ImagePitch + (copy.Extent.Height - 1) * copy.RowPitch + copy.Extent.Width * elementBytes);
    }
}
