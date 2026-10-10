using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanSurfaceFrame : IGraphicsSurfaceFrame
{
    private readonly VulkanSwapchain _swapchain;
    private VulkanTexture? _texture;
    private uint _index;
    private bool _presented;
    private bool _deviceLost;

    internal VulkanSurfaceFrame(VulkanSwapchain swapchain)
    {
        _swapchain = swapchain;
        Lifetime = new(IsNativeReleased);
        var semaphoreInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
        try
        {
            VulkanPresentation.Check(Owner.Api.CreateSemaphore(Owner.NativeDevice, &semaphoreInfo, null, out Semaphore acquire), "CreateSemaphore");
            AcquireSemaphore = acquire;
            VulkanPresentation.Check(Owner.Api.CreateSemaphore(Owner.NativeDevice, &semaphoreInfo, null, out Semaphore render), "CreateSemaphore");
            RenderSemaphore = render;
            VulkanPresentation.Check(Owner.Api.CreateFence(Owner.NativeDevice, &fenceInfo, null, out Fence acquireFence), "CreateFence");
            AcquireFence = acquireFence;
            VulkanPresentation.Check(Owner.Api.CreateFence(Owner.NativeDevice, &fenceInfo, null, out Fence presentFence), "CreateFence");
            PresentFence = presentFence;
        }
        catch
        {
            ReleaseUnacquired();
            throw;
        }
    }

    public SurfaceFrameStatus Status => Lifetime.Status;

    public IGraphicsTexture Texture => _texture ?? throw new InvalidOperationException("This frame has no acquired image.");

    internal VulkanDevice Owner => _swapchain.Owner;

    internal SurfaceFrameLifetime Lifetime { get; }

    internal Semaphore AcquireSemaphore { get; }

    internal Semaphore RenderSemaphore { get; }

    internal Fence AcquireFence { get; }

    internal Fence PresentFence { get; }

    public SurfaceStatus Present()
    {
        Lifetime.ValidatePresent();
        SurfaceStatus status = _swapchain.Present(_index, RenderSemaphore, PresentFence);
        _presented = true;
        _deviceLost = status == SurfaceStatus.DeviceLost;
        Lifetime.MarkPresented();
        return status;
    }

    public ValueTask WaitForReleaseAsync(CancellationToken cancellationToken = default) => Lifetime.WaitForReleaseAsync(cancellationToken);

    public void Dispose()
    {
        if (Status == SurfaceFrameStatus.Disposed)
        {
            return;
        }

        Lifetime.ValidateRelease();
        _texture!.DisposeLease();
        if (!_presented && !_deviceLost)
        {
            _swapchain.ReleaseImage(_index);
        }

        ReleaseUnacquired();
        Lifetime.MarkDisposed();
        _swapchain.ReleaseFrame();
    }

    internal void SetImage(uint index, Image image)
    {
        _index = index;
        SwapchainDesc desc = _swapchain.Configuration;
        _texture = new(Owner, new() { Width = desc.Width, Height = desc.Height, Format = desc.Format, Usage = desc.Usage }, image, Lifetime);
    }

    internal void ReleaseUnacquired()
    {
        if (PresentFence.Handle != 0)
        {
            Owner.Api.DestroyFence(Owner.NativeDevice, PresentFence, null);
        }

        if (AcquireFence.Handle != 0)
        {
            Owner.Api.DestroyFence(Owner.NativeDevice, AcquireFence, null);
        }

        if (RenderSemaphore.Handle != 0)
        {
            Owner.Api.DestroySemaphore(Owner.NativeDevice, RenderSemaphore, null);
        }

        if (AcquireSemaphore.Handle != 0)
        {
            Owner.Api.DestroySemaphore(Owner.NativeDevice, AcquireSemaphore, null);
        }
    }

    private bool IsNativeReleased()
    {
        if (_deviceLost)
        {
            return true;
        }

        Result acquired = Owner.Api.GetFenceStatus(Owner.NativeDevice, AcquireFence);
        if (acquired == Result.ErrorDeviceLost)
        {
            _deviceLost = true;
            return true;
        }

        if (acquired == Result.NotReady)
        {
            return false;
        }

        VulkanPresentation.Check(acquired, "GetFenceStatus");
        if (!_presented)
        {
            return true;
        }

        Result presented = Owner.Api.GetFenceStatus(Owner.NativeDevice, PresentFence);
        if (presented == Result.ErrorDeviceLost)
        {
            _deviceLost = true;
            return true;
        }

        if (presented == Result.NotReady)
        {
            return false;
        }

        VulkanPresentation.Check(presented, "GetFenceStatus");
        return true;
    }
}
