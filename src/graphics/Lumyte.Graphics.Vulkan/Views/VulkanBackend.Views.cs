using Lumyte.Graphics.Native;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

public sealed unsafe partial class VulkanBackend
{
    public NativeGpuRenderViewHandle CreateRenderView(NativeGpuTextureView view, NativeGpuRenderViewFlags flags = NativeGpuRenderViewFlags.None)
    {
        VerifyNotDisposed();
        TextureRecord texture = RequireCommandTexture(view.Texture);
        VerifyRenderViewFlags(view.Aspect, flags);
        ImageViewUsageCreateInfo usage = new()
        {
            SType = StructureType.ImageViewUsageCreateInfo,
            Usage = view.Aspect == NativeGpuTextureAspect.Color ? ImageUsageFlags.ColorAttachmentBit : ImageUsageFlags.DepthStencilAttachmentBit,
        };
        ImageViewCreateInfo info = TextureViewDescription(view, texture.Image);
        info.PNext = &usage;
        CheckDeviceResult(vk.CreateImageView(device, in info, null, out ImageView imageView), "vkCreateImageView");
        try { return new RenderViewRecord(this, imageView, texture, view, flags); }
        catch { vk.DestroyImageView(device, imageView, null); throw; }
    }

    public void DestroyRenderView(NativeGpuRenderViewHandle view)
    {
        VerifyNotDisposed();
        ArgumentNullException.ThrowIfNull(view);
        if (view is not RenderViewRecord record || !ReferenceEquals(record.Owner, this))
        {
            throw new ArgumentException("Render view belongs to another backend.", nameof(view));
        }
        ObjectDisposedException.ThrowIf(record.Destroyed, view);
        vk.DestroyImageView(device, record.ImageView, null);
        record.Destroyed = true;
    }

    internal static ImageViewCreateInfo TextureViewDescription(NativeGpuTextureView view, Image image) => new()
    {
        SType = StructureType.ImageViewCreateInfo,
        Image = image,
        ViewType = view.Dimension switch
        {
            NativeGpuTextureViewDimension.OneD => ImageViewType.Type1D,
            NativeGpuTextureViewDimension.TwoD => ImageViewType.Type2D,
            NativeGpuTextureViewDimension.TwoDArray => ImageViewType.Type2DArray,
            NativeGpuTextureViewDimension.ThreeD => ImageViewType.Type3D,
            NativeGpuTextureViewDimension.Cube => ImageViewType.TypeCube,
            NativeGpuTextureViewDimension.CubeArray => ImageViewType.TypeCubeArray,
            _ => throw new ArgumentOutOfRangeException(nameof(view), "Unknown view dimension."),
        },
        Format = TextureFormat(view.Format),
        SubresourceRange = new(TextureAspects(view.Aspect), view.BaseMip, view.MipCount, view.BaseLayer, view.LayerCount),
    };

    internal static void VerifyRenderViewFlags(NativeGpuTextureAspect aspect, NativeGpuRenderViewFlags flags)
    {
        const NativeGpuRenderViewFlags known = NativeGpuRenderViewFlags.DepthReadOnly | NativeGpuRenderViewFlags.StencilReadOnly;
        if ((flags & ~known) != 0) { throw new ArgumentOutOfRangeException(nameof(flags)); }
        // VkImageView has no equivalent flags, so native validation cannot diagnose lost metadata.
        NativeGpuRenderViewFlags available = aspect switch
        {
            NativeGpuTextureAspect.Depth => NativeGpuRenderViewFlags.DepthReadOnly,
            NativeGpuTextureAspect.Stencil => NativeGpuRenderViewFlags.StencilReadOnly,
            NativeGpuTextureAspect.DepthStencil => known,
            _ => NativeGpuRenderViewFlags.None,
        };
        if ((flags & ~available) != 0)
        {
            throw new ArgumentException("Read-only flags must refer to an aspect present in the render view.", nameof(flags));
        }
    }

    private sealed class RenderViewRecord(VulkanBackend owner, ImageView imageView, TextureRecord texture,
        NativeGpuTextureView view, NativeGpuRenderViewFlags flags) : NativeGpuRenderViewHandle(flags)
    {
        public VulkanBackend Owner { get; } = owner;
        public ImageView ImageView { get; } = imageView;
        public TextureRecord Texture { get; } = texture;
        public NativeGpuTextureView View { get; } = view;
        public bool Destroyed;
    }
}
