using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanTexture : IGraphicsTexture
{
    private readonly VulkanDevice _owner;
    private readonly Image _native;
    private readonly DeviceMemory _memory;
    private readonly SurfaceFrameLifetime? _surfaceFrame;
    private int _viewCount;
    private bool _disposed;

    internal VulkanTexture(VulkanDevice owner, TextureDesc desc)
    {
        (_owner, Width, Height, MipLevels, ArrayLayers, Format, Usage) = (owner, desc.Width, desc.Height, desc.MipLevels, desc.ArrayLayers, desc.Format, desc.Usage);
        Format format = NativeFormat(Format);
        ImageUsageFlags usage = 0;
        if ((Usage & TextureUsage.CopySource) != 0)
        {
            usage |= ImageUsageFlags.TransferSrcBit;
        }

        if ((Usage & TextureUsage.CopyDestination) != 0)
        {
            usage |= ImageUsageFlags.TransferDstBit;
        }

        if ((Usage & TextureUsage.Sampled) != 0)
        {
            usage |= ImageUsageFlags.SampledBit;
        }

        if ((Usage & TextureUsage.RenderAttachment) != 0)
        {
            usage |= Format is TextureFormat.Depth32Float or TextureFormat.Depth24Stencil8 ? ImageUsageFlags.DepthStencilAttachmentBit : ImageUsageFlags.ColorAttachmentBit;
        }

        ImageCreateFlags flags = Width == Height && ArrayLayers >= 6 ? ImageCreateFlags.CreateCubeCompatibleBit : 0;
        ImageFormatProperties supported = default;
        Result capability = owner.Api.GetPhysicalDeviceImageFormatProperties(owner.PhysicalDevice, format, ImageType.Type2D, ImageTiling.Optimal, usage, flags, &supported);
        if (capability == Result.ErrorFormatNotSupported || (capability == Result.Success &&
            (Width > supported.MaxExtent.Width || Height > supported.MaxExtent.Height || MipLevels > supported.MaxMipLevels || ArrayLayers > supported.MaxArrayLayers ||
            (supported.SampleCounts & SampleCountFlags.Count1Bit) == 0)))
        {
            throw new NotSupportedException("Vulkan does not support the requested image format, usage or dimensions.");
        }

        Check(capability, "GetPhysicalDeviceImageFormatProperties");
        Image image = default;
        DeviceMemory memory = default;
        try
        {
            var info = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                Flags = flags,
                ImageType = ImageType.Type2D,
                Format = format,
                Extent = new Extent3D(Width, Height, 1),
                MipLevels = MipLevels,
                ArrayLayers = ArrayLayers,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = usage,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined,
            };
            Check(owner.Api.CreateImage(owner.NativeDevice, &info, null, &image), "CreateImage");
            MemoryRequirements requirements = default;
            owner.Api.GetImageMemoryRequirements(owner.NativeDevice, image, &requirements);
            PhysicalDeviceMemoryProperties properties = default;
            owner.Api.GetPhysicalDeviceMemoryProperties(owner.PhysicalDevice, &properties);
            uint type = SelectMemoryType(properties, requirements.MemoryTypeBits);
            var allocation = new MemoryAllocateInfo { SType = StructureType.MemoryAllocateInfo, AllocationSize = requirements.Size, MemoryTypeIndex = type };
            Check(owner.Api.AllocateMemory(owner.NativeDevice, &allocation, null, &memory), "AllocateMemory");
            Check(owner.Api.BindImageMemory(owner.NativeDevice, image, memory, 0), "BindImageMemory");
            (_native, _memory) = (image, memory);
        }
        catch
        {
            if (image.Handle != 0)
            {
                owner.Api.DestroyImage(owner.NativeDevice, image, null);
            }

            if (memory.Handle != 0)
            {
                owner.Api.FreeMemory(owner.NativeDevice, memory, null);
            }

            throw;
        }
    }

    internal VulkanTexture(VulkanDevice owner, TextureDesc desc, Image native, SurfaceFrameLifetime lifetime)
    {
        (_owner, Width, Height, MipLevels, ArrayLayers, Format, Usage, _native, _surfaceFrame) = (owner, desc.Width, desc.Height, 1, 1, desc.Format, desc.Usage, native, lifetime);
    }

    public uint Width { get; }

    public uint Height { get; }

    public uint MipLevels { get; }

    public uint ArrayLayers { get; }

    public TextureFormat Format { get; }

    public TextureUsage Usage { get; }

    internal Image Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _surfaceFrame?.ValidateRecording();
            return _native;
        }
    }

    internal VulkanDevice Owner => _owner;

    internal SurfaceFrameLifetime? SurfaceFrame => _surfaceFrame;

    public (uint Width, uint Height) GetMipSize(uint mipLevel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _surfaceFrame?.ValidateRecording();
        if (mipLevel >= MipLevels)
        {
            throw new ArgumentOutOfRangeException(nameof(mipLevel));
        }

        return (Math.Max(1U, Width >> (int)mipLevel), Math.Max(1U, Height >> (int)mipLevel));
    }

    public IGraphicsTextureView CreateView(TextureViewDesc? desc = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _surfaceFrame?.ValidateRecording();
        TextureViewInfo info = TextureValidation.Resolve(this, desc);
        ImageViewType dimension = info.Dimension switch
        {
            TextureViewDimension.D2 => ImageViewType.Type2D,
            TextureViewDimension.D2Array => ImageViewType.Type2DArray,
            TextureViewDimension.Cube => ImageViewType.TypeCube,
            TextureViewDimension.CubeArray => ImageViewType.TypeCubeArray,
            _ => throw new NotSupportedException("Unsupported view dimension."),
        };
        if (dimension == ImageViewType.TypeCubeArray && !_owner.SupportsCubeArrays)
        {
            throw new NotSupportedException("Vulkan cube array views require imageCubeArray.");
        }

        var descriptor = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _native,
            ViewType = dimension,
            Format = NativeFormat(Format),
            Components = new ComponentMapping(ComponentSwizzle.Identity, ComponentSwizzle.Identity, ComponentSwizzle.Identity, ComponentSwizzle.Identity),
            SubresourceRange = new ImageSubresourceRange(Aspect(Format), info.BaseMipLevel, info.MipLevelCount, info.BaseArrayLayer, info.ArrayLayerCount),
        };
        ImageView native = default;
        Check(_owner.Api.CreateImageView(_owner.NativeDevice, &descriptor, null, &native), "CreateImageView");
        var view = new VulkanTextureView(this, info, native);
        _viewCount++;
        return view;
    }

    public void Dispose()
    {
        if (_surfaceFrame != null)
        {
            throw new InvalidOperationException("The frame owns this borrowed presentation image.");
        }

        DisposeLease();
    }

    internal static ImageAspectFlags Aspect(TextureFormat format) => format switch
    {
        TextureFormat.Depth32Float => ImageAspectFlags.DepthBit,
        TextureFormat.Depth24Stencil8 => ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit,
        _ => ImageAspectFlags.ColorBit,
    };

    internal static Format NativeFormat(TextureFormat format) => format switch
    {
        TextureFormat.Rgba8Unorm => Silk.NET.Vulkan.Format.R8G8B8A8Unorm,
        TextureFormat.Rgba8Srgb => Silk.NET.Vulkan.Format.R8G8B8A8Srgb,
        TextureFormat.Bgra8Unorm => Silk.NET.Vulkan.Format.B8G8R8A8Unorm,
        TextureFormat.Bgra8Srgb => Silk.NET.Vulkan.Format.B8G8R8A8Srgb,
        TextureFormat.R8Unorm => Silk.NET.Vulkan.Format.R8Unorm,
        TextureFormat.Rg8Unorm => Silk.NET.Vulkan.Format.R8G8Unorm,
        TextureFormat.R16Float => Silk.NET.Vulkan.Format.R16Sfloat,
        TextureFormat.Rg16Float => Silk.NET.Vulkan.Format.R16G16Sfloat,
        TextureFormat.Rgba16Float => Silk.NET.Vulkan.Format.R16G16B16A16Sfloat,
        TextureFormat.Rgb10A2Unorm => Silk.NET.Vulkan.Format.A2B10G10R10UnormPack32,
        TextureFormat.Depth32Float => Silk.NET.Vulkan.Format.D32Sfloat,
        TextureFormat.Depth24Stencil8 => Silk.NET.Vulkan.Format.D24UnormS8Uint,
        _ => throw new NotSupportedException("Unsupported texture format."),
    };

    internal void DisposeLease()
    {
        if (_disposed)
        {
            return;
        }

        if (_viewCount != 0)
        {
            throw new InvalidOperationException("Dispose all views before disposing their texture.");
        }

        if (_surfaceFrame == null)
        {
            _owner.Api.DestroyImage(_owner.NativeDevice, _native, null);
            _owner.Api.FreeMemory(_owner.NativeDevice, _memory, null);
        }

        _disposed = true;
        if (_surfaceFrame == null)
        {
            _owner.ReleaseTexture();
        }
    }

    internal void ReleaseView() => _viewCount--;

    private static uint SelectMemoryType(PhysicalDeviceMemoryProperties properties, uint bits)
    {
        for (int i = 0; i < properties.MemoryTypeCount; i++)
        {
            if ((bits & (1U << i)) != 0 && (properties.MemoryTypes[i].PropertyFlags & MemoryPropertyFlags.DeviceLocalBit) != 0)
            {
                return (uint)i;
            }
        }

        throw new NotSupportedException("No device-local Vulkan image memory type is available.");
    }

    private static void Check(Result result, string operation)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan {operation} failed: {result}.");
        }
    }
}
