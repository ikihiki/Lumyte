using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed class Direct3D12Swapchain : IGraphicsSwapchain
{
    private readonly Direct3D12Surface _surface;
    private nint _native;
    private bool _acquired;

    internal Direct3D12Swapchain(Direct3D12Surface surface, SwapchainDesc desc)
    {
        _surface = surface;
        Validate(desc);
        Owner.Check(NativeMethods.SwapchainCreate(Owner.Native, surface.Window, desc.Width, desc.Height, Direct3D12Texture.NativeFormat(desc.Format), desc.PresentMode == PresentMode.Immediate ? 1 : 0, out _native));
        Configuration = desc;
    }

    public SwapchainDesc Configuration { get; private set; }

    internal Direct3D12Device Owner => _surface.Owner;

    public void Reconfigure(SwapchainDesc desc)
    {
        ValidateAlive();
        Validate(desc);
        if (_acquired)
        {
            throw new InvalidOperationException("Dispose the acquired frame before reconfiguration.");
        }

        Owner.Check(NativeMethods.SwapchainResize(_native, desc.Width, desc.Height, Direct3D12Texture.NativeFormat(desc.Format), desc.PresentMode == PresentMode.Immediate ? 1 : 0));
        Configuration = desc;
    }

    public ValueTask<SurfaceAcquireResult> AcquireNextFrameAsync(IGraphicsSemaphore? signalSemaphore = null, CancellationToken cancellationToken = default)
    {
        ValidateAlive();
        cancellationToken.ThrowIfCancellationRequested();
        if (_acquired)
        {
            throw new InvalidOperationException("Dispose the previous frame before acquiring again.");
        }

        Direct3D12Semaphore? signal = null;
        if (signalSemaphore != null)
        {
            if (signalSemaphore is not Direct3D12Semaphore s || !ReferenceEquals(s.Owner, Owner))
            {
                throw new ArgumentException("Semaphore belongs to another device.");
            }

            _ = s.Native;
            s.ValidateSignal();
            signal = s;
        }

        Owner.Check(NativeMethods.SwapchainAcquire(_native, out nint texture));
        try
        {
            var frame = new Direct3D12SurfaceFrame(this, texture);
            texture = 0;
            try
            {
                signal?.Signal();
            }
            catch
            {
                frame.Dispose();
                throw;
            }

            _acquired = true;
            return ValueTask.FromResult(new SurfaceAcquireResult(SurfaceStatus.Success, frame));
        }
        catch
        {
            NativeMethods.Release(texture);
            throw;
        }
    }

    public void Dispose()
    {
        if (_native != 0)
        {
            NativeMethods.SwapchainDestroy(_native);
        }

        _native = 0;
    }

    internal void ReleaseFrame() => _acquired = false;

    internal SurfaceStatus Present()
    {
        ValidateAlive();
        int result = NativeMethods.SwapchainPresent(_native);
        Owner.Check(result);
        return result == 0x087A0001 ? SurfaceStatus.Suboptimal : SurfaceStatus.Success;
    }

    private void ValidateAlive()
    {
        ObjectDisposedException.ThrowIf(_native == 0, this);
        _surface.ValidateAlive();
    }

    private void Validate(SwapchainDesc desc)
    {
        ArgumentNullException.ThrowIfNull(desc);
        SurfaceCapabilities caps = _surface.GetCapabilities();
        if (desc.Width == 0 || desc.Height == 0 || desc.Width > caps.MaxWidth || desc.Height > caps.MaxHeight ||
            !caps.Formats.Contains(desc.Format) || !caps.PresentModes.Contains(desc.PresentMode) || !caps.AlphaModes.Contains(desc.AlphaMode) ||
            (desc.Usage & TextureUsage.RenderAttachment) == 0 || (desc.Usage & ~caps.SupportedUsage) != 0)
        {
            throw new ArgumentException("Unsupported swapchain configuration.", nameof(desc));
        }
    }
}
