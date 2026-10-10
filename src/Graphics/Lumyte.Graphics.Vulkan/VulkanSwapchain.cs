using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanSwapchain : IGraphicsSwapchain
{
    private readonly VulkanSurface _surface;
    private SwapchainKHR _native;
    private Image[] _images = [];
    private int _frameCount;
    private bool _outdated;
    private bool _retired;
    private bool _disposed;

    internal VulkanSwapchain(VulkanSurface surface, SwapchainDesc desc)
    {
        _surface = surface;
        Configuration = desc;
        Configure(desc);
    }

    public SwapchainDesc Configuration { get; private set; }

    internal VulkanDevice Owner => _surface.Owner;

    internal SwapchainKHR Native => _native;

    public void Reconfigure(SwapchainDesc desc)
    {
        ValidateAlive();
        if (_frameCount != 0)
        {
            throw new InvalidOperationException("Release all frames before reconfiguration.");
        }

        Configure(desc);
    }

    public ValueTask<SurfaceAcquireResult> AcquireNextFrameAsync(CancellationToken cancellationToken = default)
    {
        ValidateAlive();
        cancellationToken.ThrowIfCancellationRequested();
        if (_outdated)
        {
            return ValueTask.FromResult(new SurfaceAcquireResult(SurfaceStatus.Outdated, null));
        }

        var frame = new VulkanSurfaceFrame(this);
        uint index = 0;
        Result result = Owner.Presentation.Swapchain.AcquireNextImage(Owner.NativeDevice, _native, 0, frame.AcquireSemaphore, frame.AcquireFence, &index);
        SurfaceStatus status;
        try
        {
            status = VulkanPresentation.Status(result);
        }
        catch
        {
            frame.ReleaseUnacquired();
            throw;
        }

        if (status is not (SurfaceStatus.Success or SurfaceStatus.Suboptimal))
        {
            frame.ReleaseUnacquired();
            if (status == SurfaceStatus.Outdated)
            {
                _outdated = true;
            }

            return ValueTask.FromResult(new SurfaceAcquireResult(status, null));
        }

        frame.SetImage(index, _images[index]);
        _frameCount++;
        return ValueTask.FromResult(new SurfaceAcquireResult(status, frame));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_frameCount != 0)
        {
            throw new InvalidOperationException("Release all frames before swapchain disposal.");
        }

        Owner.Presentation.Swapchain.DestroySwapchain(Owner.NativeDevice, _native, null);
        _disposed = true;
        _surface.ReleaseSwapchain();
    }

    internal SurfaceStatus Present(uint index, Semaphore wait, Fence fence)
    {
        SwapchainKHR native = _native;
        var fences = new SwapchainPresentFenceInfoEXT { SType = StructureType.SwapchainPresentFenceInfoExt, SwapchainCount = 1, PFences = &fence };
        var present = new PresentInfoKHR { SType = StructureType.PresentInfoKhr, PNext = &fences, WaitSemaphoreCount = 1, PWaitSemaphores = &wait, SwapchainCount = 1, PSwapchains = &native, PImageIndices = &index };
        SurfaceStatus status = VulkanPresentation.Status(Owner.Presentation.Swapchain.QueuePresent(Owner.NativeQueue, &present));
        if (status == SurfaceStatus.Outdated)
        {
            _outdated = true;
        }

        return status;
    }

    internal void ReleaseImage(uint index)
    {
        var release = new ReleaseSwapchainImagesInfoKHR { SType = StructureType.ReleaseSwapchainImagesInfoExt, Swapchain = _native, ImageIndexCount = 1, PImageIndices = &index };
        VulkanPresentation.Check(Owner.Presentation.Maintenance.ReleaseSwapchainImages(Owner.NativeDevice, &release), "ReleaseSwapchainImagesEXT");
    }

    internal void ReleaseFrame() => _frameCount--;

    private void Configure(SwapchainDesc desc)
    {
        SurfaceValidation.Validate(desc, _surface.GetCapabilities());
        SurfaceCapabilitiesKHR caps = _surface.ReadNativeCapabilities();
        ImageUsageFlags usage = ImageUsageFlags.ColorAttachmentBit;
        if ((desc.Usage & TextureUsage.CopySource) != 0)
        {
            usage |= ImageUsageFlags.TransferSrcBit;
        }

        if ((desc.Usage & TextureUsage.CopyDestination) != 0)
        {
            usage |= ImageUsageFlags.TransferDstBit;
        }

        if ((desc.Usage & TextureUsage.Sampled) != 0)
        {
            usage |= ImageUsageFlags.SampledBit;
        }

        CompositeAlphaFlagsKHR alpha = desc.AlphaMode switch
        {
            SurfaceAlphaMode.Opaque => CompositeAlphaFlagsKHR.OpaqueBitKhr,
            SurfaceAlphaMode.Premultiplied => CompositeAlphaFlagsKHR.PreMultipliedBitKhr,
            _ => (CompositeAlphaFlagsKHR)((uint)caps.SupportedCompositeAlpha & (0U - (uint)caps.SupportedCompositeAlpha)),
        };
        uint count = caps.MinImageCount + 1;
        if (caps.MaxImageCount != 0)
        {
            count = Math.Min(count, caps.MaxImageCount);
        }

        var info = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = _surface.Native,
            MinImageCount = count,
            ImageFormat = VulkanTexture.NativeFormat(desc.Format),
            ImageColorSpace = ColorSpaceKHR.SpaceSrgbNonlinearKhr,
            ImageExtent = new(desc.Width, desc.Height),
            ImageArrayLayers = 1,
            ImageUsage = usage,
            ImageSharingMode = SharingMode.Exclusive,
            PreTransform = caps.CurrentTransform,
            CompositeAlpha = alpha,
            PresentMode = VulkanSurface.Mode(desc.PresentMode),
            Clipped = false,
            OldSwapchain = _retired ? default : _native,
        };
        SwapchainKHR next = default;
        _outdated = true;
        _retired = _native.Handle != 0;
        VulkanPresentation.Check(Owner.Presentation.Swapchain.CreateSwapchain(Owner.NativeDevice, &info, null, &next), "CreateSwapchainKHR");
        try
        {
            VulkanPresentation.Check(Owner.Presentation.Swapchain.GetSwapchainImages(Owner.NativeDevice, next, &count, null), "GetSwapchainImagesKHR");
            var images = new Image[count];
            fixed (Image* pointer = images)
            {
                VulkanPresentation.Check(Owner.Presentation.Swapchain.GetSwapchainImages(Owner.NativeDevice, next, &count, pointer), "GetSwapchainImagesKHR");
            }

            if (_native.Handle != 0)
            {
                Owner.Presentation.Swapchain.DestroySwapchain(Owner.NativeDevice, _native, null);
            }

            _native = next;
            _images = images;
            Configuration = desc;
            _outdated = false;
            _retired = false;
        }
        catch
        {
            Owner.Presentation.Swapchain.DestroySwapchain(Owner.NativeDevice, next, null);
            throw;
        }
    }

    private void ValidateAlive()
    {
        _surface.ValidateAlive();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
