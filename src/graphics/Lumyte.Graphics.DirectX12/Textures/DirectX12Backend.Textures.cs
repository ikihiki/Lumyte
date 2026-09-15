using Lumyte.Graphics.Native;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace Lumyte.Graphics.DirectX12;

public sealed unsafe partial class DirectX12Backend
{
    public NativeGpuMemoryRequirements GetTextureMemoryRequirements(
        NativeGpuTextureDescription description, NativeGpuMemoryKind kind)
    {
        VerifyAvailable();
        _ = HeapTypeFor(kind);
        ResourceDesc1 nativeDescription = TextureDescription(description);
        ResourceAllocationInfo requirements = device10.GetResourceAllocationInfo2(
            0, 1, &nativeDescription, (ResourceAllocationInfo1*)null);
        if (requirements.SizeInBytes == ulong.MaxValue || requirements.Alignment == 0)
        {
            throw new InvalidOperationException("Direct3D 12 rejected the texture description.");
        }

        ulong alignment = requirements.Alignment;
        ulong reservation = checked((requirements.SizeInBytes + alignment - 1) / alignment * alignment);
        HeapFlags flags = (nativeDescription.Flags & (ResourceFlags.AllowRenderTarget | ResourceFlags.AllowDepthStencil)) != 0
            ? HeapFlags.AllowOnlyRTDSTextures : HeapFlags.AllowOnlyNonRTDSTextures;
        return new(reservation, alignment, new HeapCompatibility(this, kind, flags));
    }

    public NativeGpuTextureHandle CreateTexture(
        NativeGpuTextureDescription description, NativeGpuHeap heap, ulong offset)
    {
        VerifyAvailable();
        HeapRecord backing = RequireHeap(heap);
        ResourceDesc1 nativeDescription = TextureDescription(description);
        ComPtr<ID3D12Resource> resource = default;
        try
        {
            Check(device10.CreatePlacedResource2<ID3D12Heap, ID3D12Resource>(
                backing.Heap, offset, &nativeDescription, BarrierLayout.Undefined, null,
                0, (Format*)null, out resource), "ID3D12Device10.CreatePlacedResource2");
            return new TextureRecord(this, resource, description);
        }
        catch
        {
            resource.Dispose();
            throw;
        }
    }

    public void DestroyTexture(NativeGpuTextureHandle texture)
    {
        VerifyNotDisposed();
        ArgumentNullException.ThrowIfNull(texture);
        if (texture is not TextureRecord record || !ReferenceEquals(record.Owner, this))
        {
            throw new ArgumentException("The texture belongs to another device.", nameof(texture));
        }
        ObjectDisposedException.ThrowIf(record.Disposed, texture);
        record.Disposed = true;
        record.Resource.Dispose();
    }

    internal static ResourceDesc1 TextureDescription(NativeGpuTextureDescription description)
    {
        ResourceDimension dimension = description.Dimension switch
        {
            NativeGpuTextureDimension.OneD => ResourceDimension.Texture1D,
            NativeGpuTextureDimension.TwoD => ResourceDimension.Texture2D,
            NativeGpuTextureDimension.ThreeD => ResourceDimension.Texture3D,
            _ => throw new ArgumentOutOfRangeException(nameof(description), "Unknown texture dimension."),
        };
        bool volume = description.Dimension == NativeGpuTextureDimension.ThreeD;
        // Native DepthOrArraySize represents only one axis; reject the other component
        // instead of losing caller input before Direct3D 12 can diagnose the description.
        if ((!volume && description.Depth != 1) || (volume && description.LayerCount != 1))
        {
            throw new ArgumentException(
                "Direct3D 12 textures use depth for 3D resources and array layers for 1D or 2D resources.",
                nameof(description));
        }

        const NativeGpuTextureUsage knownUsage = NativeGpuTextureUsage.Sampled | NativeGpuTextureUsage.Storage
            | NativeGpuTextureUsage.ColorAttachment | NativeGpuTextureUsage.DepthStencilAttachment
            | NativeGpuTextureUsage.CopySource | NativeGpuTextureUsage.CopyDestination;
        if ((description.Usage & ~knownUsage) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(description), "Unknown texture usage flags.");
        }

        ResourceFlags flags = ResourceFlags.None;
        if ((description.Usage & NativeGpuTextureUsage.Storage) != 0)
        { flags |= ResourceFlags.AllowUnorderedAccess; }
        if ((description.Usage & NativeGpuTextureUsage.ColorAttachment) != 0)
        { flags |= ResourceFlags.AllowRenderTarget; }
        if ((description.Usage & NativeGpuTextureUsage.DepthStencilAttachment) != 0)
        { flags |= ResourceFlags.AllowDepthStencil; }
        bool sampledDepth = (description.Usage & NativeGpuTextureUsage.Sampled) != 0
            && description.Format is GpuFormat.D32Float or GpuFormat.Depth24PlusStencil8;
        return new ResourceDesc1
        {
            Dimension = dimension,
            Width = description.Width,
            Height = description.Height,
            DepthOrArraySize = checked((ushort)(volume ? description.Depth : description.LayerCount)),
            MipLevels = checked((ushort)description.MipCount),
            SampleDesc = new(description.SampleCount, 0),
            Format = TextureFormat(description.Format, description.MutableFormat || sampledDepth),
            Layout = TextureLayout.LayoutUnknown,
            Flags = flags,
        };
    }

    private static Format TextureFormat(GpuFormat format, bool typeless) => (format, typeless) switch
    {
        (GpuFormat.Rgba8Unorm or GpuFormat.Rgba8UnormSrgb, true) => Format.FormatR8G8B8A8Typeless,
        (GpuFormat.Bgra8Unorm or GpuFormat.Bgra8UnormSrgb, true) => Format.FormatB8G8R8A8Typeless,
        (GpuFormat.R32Float or GpuFormat.D32Float, true) => Format.FormatR32Typeless,
        (GpuFormat.R8Unorm, true) => Format.FormatR8Typeless,
        (GpuFormat.Rg8Unorm, true) => Format.FormatR8G8Typeless,
        (GpuFormat.Rgba16Float, true) => Format.FormatR16G16B16A16Typeless,
        (GpuFormat.Depth24PlusStencil8, true) => Format.FormatR24G8Typeless,
        (GpuFormat.Rgba8Unorm, false) => Format.FormatR8G8B8A8Unorm,
        (GpuFormat.Rgba8UnormSrgb, false) => Format.FormatR8G8B8A8UnormSrgb,
        (GpuFormat.Bgra8Unorm, false) => Format.FormatB8G8R8A8Unorm,
        (GpuFormat.Bgra8UnormSrgb, false) => Format.FormatB8G8R8A8UnormSrgb,
        (GpuFormat.R32Float, false) => Format.FormatR32Float,
        (GpuFormat.D32Float, false) => Format.FormatD32Float,
        (GpuFormat.R8Unorm, false) => Format.FormatR8Unorm,
        (GpuFormat.Rg8Unorm, false) => Format.FormatR8G8Unorm,
        (GpuFormat.Rgba16Float, false) => Format.FormatR16G16B16A16Float,
        (GpuFormat.Depth24PlusStencil8, false) => Format.FormatD24UnormS8Uint,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private sealed class TextureRecord(
        DirectX12Backend owner, ComPtr<ID3D12Resource> resource, NativeGpuTextureDescription description)
        : NativeGpuTextureHandle
    {
        public DirectX12Backend Owner { get; } = owner;
        public ComPtr<ID3D12Resource> Resource = resource;
        public NativeGpuTextureDescription Description { get; } = description;
        public bool IsSurfaceImage { get; init; }
        public bool Disposed;
    }
}
