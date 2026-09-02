using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Core;

using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace Lumyte.Graphics.Vulkan;

public sealed unsafe class VulkanPresentation : IDisposable
{
    private readonly VulkanDevice owner;
    private readonly KhrSurface surfaceApi;
    private readonly KhrSwapchain swapchainApi;
    private readonly SurfaceKHR surface;
    private SwapchainKHR swapchain;
    private Format format;
    private Extent2D extent;
    private GpuTextureView[] views = [];
    private VkSemaphore imageAvailable;
    private VkSemaphore renderFinished;
    private bool disposed;

    public VulkanPresentation(VulkanDevice owner, ulong surfaceHandle, uint width, uint height)
    {
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        surface = new(surfaceHandle);
        if (!owner.Api.TryGetInstanceExtension(owner.Instance, out surfaceApi))
        {
            throw new VulkanException("VK_KHR_surface is unavailable.");
        }

        if (!owner.Api.TryGetDeviceExtension(owner.Instance, owner.Device, out swapchainApi))
        {
            throw new VulkanException("VK_KHR_swapchain is unavailable.");
        }

        surfaceApi.GetPhysicalDeviceSurfaceSupport(owner.PhysicalDevice, owner.QueueFamilyIndex, surface, out Bool32 supported);
        if (!supported)
        {
            throw new VulkanException("Selected graphics queue cannot present to this surface.");
        }

        CreateSwapchain(width, height, default);
        var semaphoreInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        Check(owner.Api.CreateSemaphore(owner.Device, in semaphoreInfo, null, out imageAvailable), "vkCreateSemaphore(acquire)");
        Check(owner.Api.CreateSemaphore(owner.Device, in semaphoreInfo, null, out renderFinished), "vkCreateSemaphore(present)");
    }

    public GpuFormat ColorFormat => format switch
    {
        Format.B8G8R8A8Unorm => GpuFormat.Bgra8Unorm,
        Format.B8G8R8A8Srgb => GpuFormat.Bgra8UnormSrgb,
        Format.R8G8B8A8Unorm => GpuFormat.Rgba8Unorm,
        _ => throw new NotSupportedException($"Unsupported swapchain format {format}."),
    };

    public uint Width => extent.Width;
    public uint Height => extent.Height;

    public VulkanPresentationFrame? Acquire()
    {
        uint index = 0;
        Result result = swapchainApi.AcquireNextImage(owner.Device, swapchain, ulong.MaxValue, imageAvailable, default, ref index);
        if (result == Result.ErrorOutOfDateKhr)
        {
            return null;
        }

        if (result is not Result.Success and not Result.SuboptimalKhr)
        {
            Check(result, "vkAcquireNextImageKHR");
        }

        return new(index, views[index]);
    }

    public bool SubmitAndPresent(VulkanPresentationFrame frame, GpuCommandBuffer commands, GpuSemaphore timeline, ulong value)
    {
        if (timeline is not VulkanDevice.VulkanSemaphore native || native.Owner != owner)
        {
            throw new ArgumentException("Timeline semaphore belongs to another device.", nameof(timeline));
        }

        native.ValidateSignal(value);
        CommandBuffer commandBuffer = owner.PreparePresent(commands, frame.View.Texture);
        commands.Finish();
        ulong* waitValues = stackalloc ulong[1] { 0 };
        ulong* signalValues = stackalloc ulong[2] { value, 0 };
        var timelineInfo = new TimelineSemaphoreSubmitInfo
        {
            SType = StructureType.TimelineSemaphoreSubmitInfo,
            WaitSemaphoreValueCount = 1,
            PWaitSemaphoreValues = waitValues,
            SignalSemaphoreValueCount = 2,
            PSignalSemaphoreValues = signalValues,
        };
        VkSemaphore* waits = stackalloc VkSemaphore[1] { imageAvailable };
        PipelineStageFlags* waitStages = stackalloc PipelineStageFlags[1] { PipelineStageFlags.ColorAttachmentOutputBit };
        VkSemaphore* signals = stackalloc VkSemaphore[2] { native.Handle, renderFinished };
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            PNext = &timelineInfo,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = waits,
            PWaitDstStageMask = waitStages,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer,
            SignalSemaphoreCount = 2,
            PSignalSemaphores = signals,
        };
        Check(owner.Api.QueueSubmit(owner.NativeQueue, 1, in submit, default), "vkQueueSubmit(present)");
        native.Track(value, [commandBuffer]);
        SwapchainKHR localSwapchain = swapchain;
        VkSemaphore presentWait = renderFinished;
        uint imageIndex = frame.ImageIndex;
        var present = new PresentInfoKHR
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &presentWait,
            SwapchainCount = 1,
            PSwapchains = &localSwapchain,
            PImageIndices = &imageIndex,
        };
        Result result = swapchainApi.QueuePresent(owner.NativeQueue, in present);
        return result is Result.ErrorOutOfDateKhr or Result.SuboptimalKhr;
    }

    public void Resize(uint width, uint height)
    {
        if (width == 0 || height == 0)
        {
            return;
        }

        owner.Api.DeviceWaitIdle(owner.Device);
        SwapchainKHR old = swapchain;
        DestroyViews();
        CreateSwapchain(width, height, old);
        swapchainApi.DestroySwapchain(owner.Device, old, null);
    }

    private void CreateSwapchain(uint width, uint height, SwapchainKHR old)
    {
        surfaceApi.GetPhysicalDeviceSurfaceCapabilities(owner.PhysicalDevice, surface, out SurfaceCapabilitiesKHR capabilities);
        uint formatCount = 0;
        surfaceApi.GetPhysicalDeviceSurfaceFormats(owner.PhysicalDevice, surface, ref formatCount, null);
        SurfaceFormatKHR[] formats = new SurfaceFormatKHR[formatCount];
        fixed (SurfaceFormatKHR* pointer = formats)
        {
            surfaceApi.GetPhysicalDeviceSurfaceFormats(owner.PhysicalDevice, surface, ref formatCount, pointer);
        }

        SurfaceFormatKHR selected = formats.FirstOrDefault(candidate => candidate.Format == Format.B8G8R8A8Unorm);
        if (selected.Format == Format.Undefined)
        {
            selected = formats[0];
        }

        format = selected.Format;
        extent = capabilities.CurrentExtent.Width != uint.MaxValue
            ? capabilities.CurrentExtent
            : new(Math.Clamp(width, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width),
                Math.Clamp(height, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height));
        uint imageCount = Math.Max(capabilities.MinImageCount, 2);
        if (capabilities.MaxImageCount != 0)
        {
            imageCount = Math.Min(imageCount, capabilities.MaxImageCount);
        }

        var create = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = surface,
            MinImageCount = imageCount,
            ImageFormat = format,
            ImageColorSpace = selected.ColorSpace,
            ImageExtent = extent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.ColorAttachmentBit,
            ImageSharingMode = SharingMode.Exclusive,
            PreTransform = capabilities.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = PresentModeKHR.FifoKhr,
            Clipped = true,
            OldSwapchain = old,
        };
        Check(swapchainApi.CreateSwapchain(owner.Device, in create, null, out swapchain), "vkCreateSwapchainKHR");
        uint count = 0;
        swapchainApi.GetSwapchainImages(owner.Device, swapchain, ref count, null);
        Image[] images = new Image[count];
        fixed (Image* pointer = images)
        {
            swapchainApi.GetSwapchainImages(owner.Device, swapchain, ref count, pointer);
        }

        views = new GpuTextureView[count];
        GpuFormat gpuFormat = ColorFormat;
        var description = new GpuTextureDescription(extent.Width, extent.Height, gpuFormat, GpuTextureUsage.ColorAttachment);
        for (int index = 0; index < images.Length; index++)
        {
            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = images[index],
                ViewType = ImageViewType.Type2D,
                Format = format,
                SubresourceRange = new(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
            };
            Check(owner.Api.CreateImageView(owner.Device, in viewInfo, null, out ImageView view), "vkCreateImageView(swapchain)");
            views[index] = owner.RegisterSwapchainImage(images[index], description, view);
        }
    }

    private void DestroyViews()
    {
        foreach (GpuTextureView view in views)
        {
            ImageView nativeView = owner.UnregisterSwapchainImage(view);
            owner.Api.DestroyImageView(owner.Device, nativeView, null);
        }
        views = [];
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        owner.Api.DeviceWaitIdle(owner.Device);
        DestroyViews();
        owner.Api.DestroySemaphore(owner.Device, imageAvailable, null);
        owner.Api.DestroySemaphore(owner.Device, renderFinished, null);
        swapchainApi.DestroySwapchain(owner.Device, swapchain, null);
        surfaceApi.DestroySurface(owner.Instance, surface, null);
        swapchainApi.Dispose();
        surfaceApi.Dispose();
    }

    private static void Check(Result result, string operation)
    {
        if (result != Result.Success)
        {
            throw new VulkanException($"{operation} failed: {result}.");
        }
    }
}

public readonly record struct VulkanPresentationFrame(uint ImageIndex, GpuTextureView View);
