using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanSurfaceFrame : IGraphicsSurfaceFrame
{
    private readonly VulkanSwapchain _swapchain;
    private readonly object? _acquisitionOperation;
    private VulkanTexture? _texture;
    private uint _index;
    private bool _presented;
    private bool _deviceLost;

    internal VulkanSurfaceFrame(VulkanSwapchain swapchain)
    {
        _swapchain = swapchain;
        _acquisitionOperation = Owner.Validation != null ? new object() : null;
        Lifetime = new(IsNativeReleased);
        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
        try
        {
            Owner.CheckResult(Owner.Api.CreateFence(Owner.NativeDevice, &fenceInfo, null, out Fence acquireFence), "CreateFence");
            AcquireFence = acquireFence;
            Owner.CheckResult(Owner.Api.CreateFence(Owner.NativeDevice, &fenceInfo, null, out Fence presentFence), "CreateFence");
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

    internal object AcquisitionOperation => _acquisitionOperation!;

    internal VulkanDevice Owner => _swapchain.Owner;

    internal SurfaceFrameLifetime Lifetime { get; }

    internal Fence AcquireFence { get; }

    internal Fence PresentFence { get; }

    public SurfaceStatus Present(IReadOnlyList<IGraphicsSemaphore>? waitSemaphores = null)
    {
        Lifetime.ValidatePresent();
        IReadOnlyList<IGraphicsSemaphore> validationWaits = waitSemaphores ?? [];
        Owner.Validation?.ValidatePresentTexture(_texture!, validationWaits);
        Owner.Validation?.PresentationWaitValidate(validationWaits);

        var waits = new Silk.NET.Vulkan.Semaphore[waitSemaphores?.Count ?? 0];
        for (int i = 0; i < waits.Length; i++)
        {
            if (waitSemaphores![i] is not VulkanSemaphore semaphore || !ReferenceEquals(semaphore.Owner, Owner))
            {
                throw new ArgumentException("Presentation semaphore belongs to another device.", nameof(waitSemaphores));
            }

            waits[i] = semaphore.Native;
        }

        SurfaceStatus status = _swapchain.Present(_index, waits, PresentFence);
        if (status is SurfaceStatus.Success or SurfaceStatus.Suboptimal)
        {
            Owner.Validation?.PresentationWaited(this, validationWaits);
            Owner.Validation?.Use(this, _texture!);
        }

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

        if (Owner.Validation != null)
        {
            if (!IsNativeReleased())
            {
                throw new InvalidOperationException("Validation: native presentation synchronization is still in flight.");
            }

            // Check live views before touching the native image or its parent.
            Owner.Validation.Release(_texture!);
        }

        if (!_presented && !_deviceLost && Owner.Status == GraphicsDeviceStatus.Ready)
        {
            try
            {
                _swapchain.ReleaseImage(_index);
            }
            catch
            {
                // Preserve the ownership chain and borrowed lease for a retry.
                Owner.Validation?.Register(_texture!, this);
                Owner.Validation?.SetTextureState(_texture!, TextureState.Undefined);
                throw;
            }
        }

        Owner.Validation?.Release(this);
        _texture!.DisposeLease();
        ReleaseUnacquired();
        Lifetime.MarkDisposed();
    }

    internal void SetImage(uint index, Image image)
    {
        _index = index;
        SwapchainDesc desc = _swapchain.Configuration;
        _texture = new(Owner, new() { Width = desc.Width, Height = desc.Height, Format = desc.Format, Usage = desc.Usage }, image, Lifetime);
        Owner.Validation?.Register(this, _swapchain);
        Owner.Validation?.Register(_texture, this);
        Owner.Validation?.SetTextureState(_texture, TextureState.Undefined);
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
    }

    private bool IsNativeReleased()
    {
        if (_deviceLost || Owner.Status == GraphicsDeviceStatus.Lost)
        {
            CompleteNativeUses();
            return true;
        }

        Result acquired = Owner.Api.GetFenceStatus(Owner.NativeDevice, AcquireFence);
        if (acquired == Result.ErrorDeviceLost)
        {
            Owner.ReportLoss("Vulkan surface fence failed: device lost.");
            _deviceLost = true;
            CompleteNativeUses();
            return true;
        }

        if (acquired == Result.NotReady)
        {
            return false;
        }

        Owner.CheckResult(acquired, "GetFenceStatus");
        if (_acquisitionOperation != null)
        {
            Owner.Validation?.CompleteSemaphores(_acquisitionOperation);
        }

        if (!_presented)
        {
            CompleteNativeUses();
            return true;
        }

        Result presented = Owner.Api.GetFenceStatus(Owner.NativeDevice, PresentFence);
        if (presented == Result.ErrorDeviceLost)
        {
            Owner.ReportLoss("Vulkan surface fence failed: device lost.");
            _deviceLost = true;
            CompleteNativeUses();
            return true;
        }

        if (presented == Result.NotReady)
        {
            return false;
        }

        Owner.CheckResult(presented, "GetFenceStatus");
        CompleteNativeUses();
        return true;
    }

    private void CompleteNativeUses()
    {
        if (_acquisitionOperation != null)
        {
            Owner.Validation?.CompleteSemaphores(_acquisitionOperation);
        }

        Owner.Validation?.CompleteSemaphores(this);
        Owner.Validation?.Complete(this);
    }
}
