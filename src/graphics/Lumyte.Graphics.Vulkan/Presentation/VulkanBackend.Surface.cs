using System.Runtime.InteropServices;

using Lumyte.Graphics.Native;

using Silk.NET.Core;
using Silk.NET.Vulkan;

using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace Lumyte.Graphics.Vulkan;

public sealed unsafe partial class VulkanBackend
{
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    private static partial nint GetPresentationModule(nint name);
    /// <summary>Creates a Win32 surface with explicit present fences and non-presenting image release.</summary>
    /// <remarks>Window presentation requires swapchain_maintenance1; headless Native use does not.</remarks>
    public INativeGpuSurface CreateWindowSurface(nint hwnd)
    {
        VerifyNotDisposed();
        if (!OperatingSystem.IsWindows() || !swapchainMaintenanceEnabled)
        { throw new NotSupportedException("Win32 presentation requires VK_EXT_surface_maintenance1 and VK_EXT_swapchain_maintenance1."); }
        if (hwnd == 0)
        { throw new ArgumentException("A live HWND is required.", nameof(hwnd)); }
        return new WindowSurface(this, hwnd);
    }
    private sealed class WindowSurface : INativeGpuSurface
    {
        private readonly VulkanBackend owner;
        private readonly delegate* unmanaged<Instance, SurfaceKHR, AllocationCallbacks*, void> destroySurface;
        private readonly delegate* unmanaged<PhysicalDevice, SurfaceKHR, SurfaceCapabilitiesKHR*, Result> capabilities;
        private readonly delegate* unmanaged<PhysicalDevice, SurfaceKHR, uint*, SurfaceFormatKHR*, Result> formats;
        private readonly delegate* unmanaged<Device, SwapchainCreateInfoKHR*, AllocationCallbacks*, SwapchainKHR*, Result> createSwapchain;
        private readonly delegate* unmanaged<Device, SwapchainKHR, AllocationCallbacks*, void> destroySwapchain;
        private readonly delegate* unmanaged<Device, SwapchainKHR, uint*, Image*, Result> getImages;
        private readonly delegate* unmanaged<Device, SwapchainKHR, ulong, VkSemaphore, Fence, uint*, Result> acquireImage;
        private readonly delegate* unmanaged<Queue, PresentInfoKHR*, Result> present;
        private readonly delegate* unmanaged<Device, ReleaseSwapchainImagesInfoEXT*, Result> releaseImage;
        private SurfaceKHR surface;
        private SwapchainKHR chain;
        private Image[] images = [];
        private bool[] initialized = [];
        private uint width, height, index;
        private NativeGpuSurfaceImage? acquired;
        private bool disposed;
        private Fence presentFence;
        private Fence acquireFence;
        private VkSemaphore presentReady;
        internal WindowSurface(VulkanBackend owner, nint hwnd)
        {
            this.owner = owner;
            var create = (delegate* unmanaged<Instance, Win32SurfaceCreateInfoKHR*, AllocationCallbacks*, SurfaceKHR*, Result>)(nint)owner.vk.GetInstanceProcAddr(owner.instance, "vkCreateWin32SurfaceKHR");
            destroySurface = (delegate* unmanaged<Instance, SurfaceKHR, AllocationCallbacks*, void>)(nint)owner.vk.GetInstanceProcAddr(owner.instance, "vkDestroySurfaceKHR");
            capabilities = (delegate* unmanaged<PhysicalDevice, SurfaceKHR, SurfaceCapabilitiesKHR*, Result>)(nint)owner.vk.GetInstanceProcAddr(owner.instance, "vkGetPhysicalDeviceSurfaceCapabilitiesKHR");
            formats = (delegate* unmanaged<PhysicalDevice, SurfaceKHR, uint*, SurfaceFormatKHR*, Result>)(nint)owner.vk.GetInstanceProcAddr(owner.instance, "vkGetPhysicalDeviceSurfaceFormatsKHR");
            createSwapchain = (delegate* unmanaged<Device, SwapchainCreateInfoKHR*, AllocationCallbacks*, SwapchainKHR*, Result>)(nint)owner.vk.GetDeviceProcAddr(owner.device, "vkCreateSwapchainKHR");
            destroySwapchain = (delegate* unmanaged<Device, SwapchainKHR, AllocationCallbacks*, void>)(nint)owner.vk.GetDeviceProcAddr(owner.device, "vkDestroySwapchainKHR");
            getImages = (delegate* unmanaged<Device, SwapchainKHR, uint*, Image*, Result>)(nint)owner.vk.GetDeviceProcAddr(owner.device, "vkGetSwapchainImagesKHR");
            acquireImage = (delegate* unmanaged<Device, SwapchainKHR, ulong, VkSemaphore, Fence, uint*, Result>)(nint)owner.vk.GetDeviceProcAddr(owner.device, "vkAcquireNextImageKHR");
            present = (delegate* unmanaged<Queue, PresentInfoKHR*, Result>)(nint)owner.vk.GetDeviceProcAddr(owner.device, "vkQueuePresentKHR");
            releaseImage = (delegate* unmanaged<Device, ReleaseSwapchainImagesInfoEXT*, Result>)(nint)owner.vk.GetDeviceProcAddr(owner.device, "vkReleaseSwapchainImagesEXT");
            Win32SurfaceCreateInfoKHR info = new() { SType = StructureType.Win32SurfaceCreateInfoKhr, Hwnd = hwnd, Hinstance = GetPresentationModule(0) };
            SurfaceKHR created = default;
            owner.CheckDeviceResult(create(owner.instance, &info, null, &created), "vkCreateWin32SurfaceKHR");
            surface = created;
            try
            {
                var support = (delegate* unmanaged<PhysicalDevice, uint, SurfaceKHR, Bool32*, Result>)(nint)owner.vk.GetInstanceProcAddr(owner.instance, "vkGetPhysicalDeviceSurfaceSupportKHR");
                Bool32 supported = default;
                owner.CheckDeviceResult(support(owner.presentationDevice, owner.mainQueue!.Family, surface, &supported), "vkGetPhysicalDeviceSurfaceSupportKHR");
                if (!supported)
                { throw new NotSupportedException("The selected main queue cannot present to this window."); }
            }
            catch { destroySurface(owner.instance, surface, null); throw; }
        }
        public ValueTask<NativeGpuSurfaceImage> AcquireAsync(uint requestedWidth, uint requestedHeight, CancellationToken cancellationToken = default)
            => new(Task.Run(() => Acquire(requestedWidth, requestedHeight, cancellationToken), cancellationToken));
        private NativeGpuSurfaceImage Acquire(uint requestedWidth, uint requestedHeight, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (acquired is not null || acquireFence.Handle != 0)
            { throw new InvalidOperationException("Return the previous surface image first."); }
            if (requestedWidth == 0 || requestedHeight == 0)
            { throw new ArgumentOutOfRangeException(nameof(requestedWidth), "Pause presentation while the window has zero extent."); }
            if (chain.Handle == 0 || width != requestedWidth || height != requestedHeight)
            { Recreate(requestedWidth, requestedHeight); }
            FenceCreateInfo fenceInfo = new() { SType = StructureType.FenceCreateInfo };
            owner.CheckDeviceResult(owner.vk.CreateFence(owner.device, &fenceInfo, null, out acquireFence), "vkCreateFence(acquire)");
            Fence fence = acquireFence;
            bool fenceIdle = true;
            try
            {
                Result result;
                uint next = 0;
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    fenceIdle = false;
                    result = acquireImage(owner.device, chain, 100_000_000, default, fence, &next);
                    fenceIdle = result is Result.Timeout or Result.NotReady or Result.ErrorOutOfDateKhr;
                    if (result == Result.ErrorOutOfDateKhr)
                    { Recreate(requestedWidth, requestedHeight); }
                } while (result is Result.Timeout or Result.NotReady or Result.ErrorOutOfDateKhr);
                if (result != Result.SuboptimalKhr)
                { owner.CheckDeviceResult(result, "vkAcquireNextImageKHR"); }
                // Once an image is acquired, finish its synchronization even if the caller cancels.
                index = next;
                var description = new NativeGpuTextureDescription(NativeGpuTextureDimension.TwoD, width, height, 1, 1, 1, 1, GpuFormat.Bgra8Unorm, NativeGpuTextureUsage.ColorAttachment);
                acquired = new(new TextureRecord(owner, images[index], description, null, 0) { RequiresGeneralInitialization = false }, description);
                WaitFence(fence);
                fenceIdle = true;
                Transition(images[index], initialized[index] ? ImageLayout.PresentSrcKhr : ImageLayout.Undefined, ImageLayout.General);
                initialized[index] = true;
                return acquired;
            }
            finally
            {
                // A failed observation does not prove that acquisition stopped using its fence.
                if (fenceIdle)
                { owner.vk.DestroyFence(owner.device, fence, null); acquireFence = default; }
            }
        }
        private void Recreate(uint requestedWidth, uint requestedHeight)
        {
            SurfaceCapabilitiesKHR caps = default;
            owner.CheckDeviceResult(capabilities(owner.presentationDevice, surface, &caps), "vkGetPhysicalDeviceSurfaceCapabilitiesKHR");
            uint count = 0;
            owner.CheckDeviceResult(formats(owner.presentationDevice, surface, &count, null), "vkGetPhysicalDeviceSurfaceFormatsKHR");
            var choices = new SurfaceFormatKHR[count];
            fixed (SurfaceFormatKHR* pointer = choices)
            { owner.CheckDeviceResult(formats(owner.presentationDevice, surface, &count, pointer), "vkGetPhysicalDeviceSurfaceFormatsKHR"); }
            if (!choices.Any(f => f.Format == Format.B8G8R8A8Unorm && f.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr))
            { throw new NotSupportedException("Surface has no BGRA8 SDR format."); }
            uint w = caps.CurrentExtent.Width == uint.MaxValue ? Math.Clamp(requestedWidth, caps.MinImageExtent.Width, caps.MaxImageExtent.Width) : caps.CurrentExtent.Width;
            uint h = caps.CurrentExtent.Height == uint.MaxValue ? Math.Clamp(requestedHeight, caps.MinImageExtent.Height, caps.MaxImageExtent.Height) : caps.CurrentExtent.Height;
            if (w == 0 || h == 0)
            { throw new InvalidOperationException("Window surface is minimized; resume after a nonzero resize."); }
            uint imageCount = Math.Max(2, caps.MinImageCount);
            if (caps.MaxImageCount != 0)
            { imageCount = Math.Min(imageCount, caps.MaxImageCount); }
            var alpha = CompositeAlphaFlagsKHR.OpaqueBitKhr;
            if ((caps.SupportedCompositeAlpha & alpha) == 0)
            { alpha = (CompositeAlphaFlagsKHR)((uint)caps.SupportedCompositeAlpha & unchecked(0u - (uint)caps.SupportedCompositeAlpha)); }
            SwapchainCreateInfoKHR info = new()
            {
                SType = StructureType.SwapchainCreateInfoKhr,
                Surface = surface,
                MinImageCount = imageCount,
                ImageFormat = Format.B8G8R8A8Unorm,
                ImageColorSpace = ColorSpaceKHR.SpaceSrgbNonlinearKhr,
                ImageExtent = new(w, h),
                ImageArrayLayers = 1,
                ImageUsage = ImageUsageFlags.ColorAttachmentBit,
                ImageSharingMode = SharingMode.Exclusive,
                PreTransform = caps.CurrentTransform,
                CompositeAlpha = alpha,
                PresentMode = PresentModeKHR.FifoKhr,
                Clipped = true,
                OldSwapchain = chain
            };
            SwapchainKHR created = default;
            owner.CheckDeviceResult(createSwapchain(owner.device, &info, null, &created), "vkCreateSwapchainKHR");
            if (chain.Handle != 0)
            { destroySwapchain(owner.device, chain, null); }
            chain = created;
            width = w;
            height = h;
            count = 0;
            owner.CheckDeviceResult(getImages(owner.device, chain, &count, null), "vkGetSwapchainImagesKHR");
            images = new Image[count];
            initialized = new bool[count];
            fixed (Image* pointer = images)
            { owner.CheckDeviceResult(getImages(owner.device, chain, &count, pointer), "vkGetSwapchainImagesKHR"); }
        }
        public ValueTask PresentAsync(NativeGpuSurfaceImage image) => new(Task.Run(() => Present(image)));
        private void Present(NativeGpuSurfaceImage image)
        {
            Require(image);
            SemaphoreCreateInfo semaphoreInfo = new() { SType = StructureType.SemaphoreCreateInfo };
            owner.CheckDeviceResult(owner.vk.CreateSemaphore(owner.device, &semaphoreInfo, null, out presentReady), "vkCreateSemaphore(present)");
            Transition(images[index], ImageLayout.General, ImageLayout.PresentSrcKhr, presentReady);
            FenceCreateInfo fenceInfo = new() { SType = StructureType.FenceCreateInfo };
            owner.CheckDeviceResult(owner.vk.CreateFence(owner.device, &fenceInfo, null, out presentFence), "vkCreateFence(present)");
            Fence fence = presentFence;
            uint imageIndex = index;
            SwapchainKHR swapchain = chain;
            SwapchainPresentFenceInfoEXT completion = new() { SType = StructureType.SwapchainPresentFenceInfoExt, SwapchainCount = 1, PFences = &fence };
            VkSemaphore ready = presentReady;
            PresentInfoKHR info = new() { SType = StructureType.PresentInfoKhr, PNext = &completion, WaitSemaphoreCount = 1, PWaitSemaphores = &ready, SwapchainCount = 1, PSwapchains = &swapchain, PImageIndices = &imageIndex };
            Result result = present(owner.mainQueue!.Handle, &info);
            if (result is not (Result.Success or Result.SuboptimalKhr or Result.ErrorOutOfDateKhr))
            { owner.CheckDeviceResult(result, "vkQueuePresentKHR"); }
            WaitFence(fence);
            owner.vk.DestroyFence(owner.device, fence, null);
            presentFence = default;
            owner.vk.DestroySemaphore(owner.device, ready, null);
            presentReady = default;
            ((TextureRecord)image.Texture).Destroyed = true;
            acquired = null;
            if (result == Result.ErrorOutOfDateKhr)
            { destroySwapchain(owner.device, chain, null); chain = default; }
        }
        public ValueTask DiscardAsync(NativeGpuSurfaceImage image)
        {
            Require(image);
            uint imageIndex = index;
            ReleaseSwapchainImagesInfoEXT info = new() { SType = StructureType.ReleaseSwapchainImagesInfoExt, Swapchain = chain, ImageIndexCount = 1, PImageIndices = &imageIndex };
            owner.CheckDeviceResult(releaseImage(owner.device, &info), "vkReleaseSwapchainImagesEXT");
            // No present transition took place. The next acquire discards this content from UNDEFINED.
            initialized[index] = false;
            ((TextureRecord)image.Texture).Destroyed = true;
            acquired = null;
            return ValueTask.CompletedTask;
        }
        private void Require(NativeGpuSurfaceImage image)
        { ObjectDisposedException.ThrowIf(disposed, this); if (!ReferenceEquals(image, acquired)) { throw new ArgumentException("Image is not acquired from this surface.", nameof(image)); } }
        private void WaitFence(Fence fence) => owner.CheckDeviceResult(owner.vk.WaitForFences(owner.device, 1, &fence, true, ulong.MaxValue), "vkWaitForFences(surface)");
        private void Transition(Image image, ImageLayout before, ImageLayout after, VkSemaphore signal = default)
        {
            CommandPoolCreateInfo poolInfo = new() { SType = StructureType.CommandPoolCreateInfo, QueueFamilyIndex = owner.mainQueue!.Family, Flags = CommandPoolCreateFlags.TransientBit };
            owner.CheckDeviceResult(owner.vk.CreateCommandPool(owner.device, &poolInfo, null, out CommandPool pool), "vkCreateCommandPool(surface)");
            Fence fence = default;
            bool accepted = false, finished = false;
            try
            {
                CommandBufferAllocateInfo allocate = new() { SType = StructureType.CommandBufferAllocateInfo, CommandPool = pool, Level = CommandBufferLevel.Primary, CommandBufferCount = 1 };
                CommandBuffer command = default;
                owner.CheckDeviceResult(owner.vk.AllocateCommandBuffers(owner.device, &allocate, &command), "vkAllocateCommandBuffers(surface)");
                CommandBufferBeginInfo begin = new() { SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit };
                owner.CheckDeviceResult(owner.vk.BeginCommandBuffer(command, &begin), "vkBeginCommandBuffer(surface)");
                ImageMemoryBarrier2 barrier = new()
                {
                    SType = StructureType.ImageMemoryBarrier2,
                    SrcStageMask = PipelineStageFlags2.AllCommandsBit,
                    SrcAccessMask = AccessFlags2.MemoryWriteBit,
                    DstStageMask = PipelineStageFlags2.AllCommandsBit,
                    DstAccessMask = AccessFlags2.MemoryReadBit | AccessFlags2.MemoryWriteBit,
                    OldLayout = before,
                    NewLayout = after,
                    SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                    DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                    Image = image,
                    SubresourceRange = new(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
                };
                DependencyInfo dependency = new() { SType = StructureType.DependencyInfo, ImageMemoryBarrierCount = 1, PImageMemoryBarriers = &barrier };
                owner.vk.CmdPipelineBarrier2(command, &dependency);
                owner.CheckDeviceResult(owner.vk.EndCommandBuffer(command), "vkEndCommandBuffer(surface)");
                FenceCreateInfo fenceInfo = new() { SType = StructureType.FenceCreateInfo };
                owner.CheckDeviceResult(owner.vk.CreateFence(owner.device, &fenceInfo, null, out fence), "vkCreateFence(surface)");
                SubmitInfo submit = new()
                {
                    SType = StructureType.SubmitInfo,
                    CommandBufferCount = 1,
                    PCommandBuffers = &command,
                    SignalSemaphoreCount = signal.Handle == 0 ? 0u : 1u,
                    PSignalSemaphores = &signal
                };
                accepted = true;
                owner.CheckDeviceResult(owner.vk.QueueSubmit(owner.mainQueue.Handle, 1, &submit, fence), "vkQueueSubmit(surface)");
                WaitFence(fence);
                finished = true;
            }
            finally
            {
                if (!accepted || finished)
                { if (fence.Handle != 0) { owner.vk.DestroyFence(owner.device, fence, null); } owner.vk.DestroyCommandPool(owner.device, pool, null); }
            }
        }
        public ValueTask DisposeAsync()
        {
            if (disposed)
            { return ValueTask.CompletedTask; }
            if (acquired is not null || acquireFence.Handle != 0 || presentFence.Handle != 0 || presentReady.Handle != 0)
            { throw new InvalidOperationException("Surface has unfinished presentation ownership."); }
            if (chain.Handle != 0)
            { destroySwapchain(owner.device, chain, null); }
            destroySurface(owner.instance, surface, null);
            disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
