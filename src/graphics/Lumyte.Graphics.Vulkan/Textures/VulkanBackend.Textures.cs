using Lumyte.Graphics.Native;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

public sealed unsafe partial class VulkanBackend
{
    public NativeGpuMemoryRequirements GetTextureMemoryRequirements(NativeGpuTextureDescription description, NativeGpuMemoryKind kind)
    {
        VerifyNotDisposed();
        if (kind is not (NativeGpuMemoryKind.CpuVisible or NativeGpuMemoryKind.GpuOnly or NativeGpuMemoryKind.Readback))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        Image image = CreateTextureImage(description);
        try
        {
            // Optimal images without external-memory inputs cannot require a dedicated allocation.
            // https://docs.vulkan.org/refpages/latest/refpages/source/VkMemoryDedicatedRequirements.html
            vk.GetImageMemoryRequirements(device, image, out MemoryRequirements requirements);
            var reservation = ReserveMemory(requirements.Size, requirements.Alignment, bufferImageGranularity);
            return new(reservation.Size, reservation.Alignment, new MemoryCompatibility(this, kind, requirements.MemoryTypeBits));
        }
        finally { vk.DestroyImage(device, image, null); }
    }

    public NativeGpuTextureHandle CreateTexture(NativeGpuTextureDescription description, NativeGpuHeap heap, ulong offset)
    {
        VerifyNotDisposed();
        HeapRecord allocation = RequireHeap(heap);
        Image image = CreateTextureImage(description);
        try
        {
            CheckDeviceResult(vk.BindImageMemory(device, image, allocation.Memory, offset), "vkBindImageMemory");
            return new TextureRecord(this, image, description, allocation, offset);
        }
        catch
        {
            vk.DestroyImage(device, image, null);
            throw;
        }
    }

    public void DestroyTexture(NativeGpuTextureHandle texture)
    {
        VerifyNotDisposed();
        ArgumentNullException.ThrowIfNull(texture);
        if (texture is not TextureRecord record || !ReferenceEquals(record.Owner, this))
        {
            throw new ArgumentException("Texture belongs to another device.", nameof(texture));
        }
        ObjectDisposedException.ThrowIf(record.Destroyed, texture);
        vk.DestroyImage(device, record.Image, null);
        record.Destroyed = true;
    }

    private Image CreateTextureImage(NativeGpuTextureDescription description)
    {
        ImageCreateInfo info = TextureImageDescription(description);
        fixed (uint* families = resourceQueueFamilies)
        {
            if (resourceQueueFamilies.Length != 0)
            {
                info.SharingMode = SharingMode.Concurrent;
                info.QueueFamilyIndexCount = checked((uint)resourceQueueFamilies.Length);
                info.PQueueFamilyIndices = families;
            }
            CheckDeviceResult(vk.CreateImage(device, in info, null, out Image image), "vkCreateImage");
            return image;
        }
    }

    internal static ImageCreateInfo TextureImageDescription(NativeGpuTextureDescription description)
    {
        ImageCreateFlags flags = ImageCreateFlags.CreateAliasBit;
        if (description.MutableFormat) { flags |= ImageCreateFlags.CreateMutableFormatBit; }
        // Cube selection belongs to the view. Preserve that option for eligible 2D arrays.
        // VkImageCreateInfo VUIDs 00949, 08865, 08866, and 02257 define these flag conditions.
        if (description.Dimension == NativeGpuTextureDimension.TwoD && description.Width == description.Height
            && description.LayerCount >= 6 && description.SampleCount == 1)
        {
            flags |= ImageCreateFlags.CreateCubeCompatibleBit;
        }
        if (description.Dimension == NativeGpuTextureDimension.ThreeD
            && (description.Usage & (NativeGpuTextureUsage.ColorAttachment | NativeGpuTextureUsage.DepthStencilAttachment)) != 0)
        {
            flags |= ImageCreateFlags.Create2DArrayCompatibleBit;
        }
        return new()
        {
            SType = StructureType.ImageCreateInfo,
            Flags = flags,
            ImageType = description.Dimension switch
            {
                NativeGpuTextureDimension.OneD => ImageType.Type1D,
                NativeGpuTextureDimension.TwoD => ImageType.Type2D,
                NativeGpuTextureDimension.ThreeD => ImageType.Type3D,
                _ => throw new ArgumentOutOfRangeException(nameof(description), "Unknown texture dimension."),
            },
            Format = TextureFormat(description.Format),
            Extent = new(description.Width, description.Height, description.Depth),
            MipLevels = description.MipCount,
            ArrayLayers = description.LayerCount,
            Samples = (SampleCountFlags)description.SampleCount,
            Tiling = ImageTiling.Optimal,
            Usage = TextureUsage(description.Usage),
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
    }

    private static Format TextureFormat(GpuFormat format) => format switch
    {
        GpuFormat.Rgba8Unorm => Format.R8G8B8A8Unorm,
        GpuFormat.Bgra8Unorm => Format.B8G8R8A8Unorm,
        GpuFormat.Rgba8UnormSrgb => Format.R8G8B8A8Srgb,
        GpuFormat.Bgra8UnormSrgb => Format.B8G8R8A8Srgb,
        GpuFormat.R8Unorm => Format.R8Unorm,
        GpuFormat.Rg8Unorm => Format.R8G8Unorm,
        GpuFormat.Rgba16Float => Format.R16G16B16A16Sfloat,
        GpuFormat.R32Float => Format.R32Sfloat,
        GpuFormat.D32Float => Format.D32Sfloat,
        GpuFormat.Depth24PlusStencil8 => Format.D24UnormS8Uint,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static ImageUsageFlags TextureUsage(NativeGpuTextureUsage usage)
    {
        const NativeGpuTextureUsage known = NativeGpuTextureUsage.Sampled | NativeGpuTextureUsage.Storage
            | NativeGpuTextureUsage.ColorAttachment | NativeGpuTextureUsage.DepthStencilAttachment
            | NativeGpuTextureUsage.CopySource | NativeGpuTextureUsage.CopyDestination;
        if ((usage & ~known) != 0) { throw new ArgumentOutOfRangeException(nameof(usage)); }
        ImageUsageFlags native = 0;
        if ((usage & NativeGpuTextureUsage.Sampled) != 0) { native |= ImageUsageFlags.SampledBit; }
        if ((usage & NativeGpuTextureUsage.Storage) != 0) { native |= ImageUsageFlags.StorageBit; }
        if ((usage & NativeGpuTextureUsage.ColorAttachment) != 0) { native |= ImageUsageFlags.ColorAttachmentBit; }
        if ((usage & NativeGpuTextureUsage.DepthStencilAttachment) != 0) { native |= ImageUsageFlags.DepthStencilAttachmentBit; }
        if ((usage & NativeGpuTextureUsage.CopySource) != 0) { native |= ImageUsageFlags.TransferSrcBit; }
        if ((usage & NativeGpuTextureUsage.CopyDestination) != 0) { native |= ImageUsageFlags.TransferDstBit; }
        return native;
    }

    private sealed class TextureRecord(VulkanBackend owner, Image image, NativeGpuTextureDescription description,
        HeapRecord allocation, ulong heapOffset) : NativeGpuTextureHandle
    {
        public VulkanBackend Owner { get; } = owner;
        public Image Image { get; } = image;
        public NativeGpuTextureDescription Description { get; } = description;
        public HeapRecord Allocation { get; } = allocation;
        public ulong HeapOffset { get; } = heapOffset;
        // Submission integration will retire this obligation only after native queue acceptance.
        public bool RequiresGeneralInitialization { get; set; } = true;
        public bool Destroyed;
    }
}
