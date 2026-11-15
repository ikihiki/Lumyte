using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class PresentationSwapchain : IGraphicsSwapchain
{
    public SwapchainDesc Configuration { get; private set; } = new() { Width = 32, Height = 16 };

    public PresentationFrame Frame { get; } = new();

    public SurfaceStatus AcquireStatus { get; set; } = SurfaceStatus.Success;

    public bool ThrowOnAcquire { get; set; }

    public int AcquireCount { get; private set; }

    public int DisposeCount { get; private set; }

    public int ReconfigureCount { get; private set; }

    public IGraphicsSemaphore? LastSignal { get; private set; }

    public void Reconfigure(SwapchainDesc desc)
    {
        ReconfigureCount++;
        Configuration = desc;
    }

    public ValueTask<SurfaceAcquireResult> AcquireNextFrameAsync(IGraphicsSemaphore? signalSemaphore = null, CancellationToken cancellationToken = default)
    {
        AcquireCount++;
        LastSignal = signalSemaphore;
        if (ThrowOnAcquire)
        {
            throw new InvalidOperationException("Native acquisition failed.");
        }

        return ValueTask.FromResult(new SurfaceAcquireResult(AcquireStatus, AcquireStatus is SurfaceStatus.Success or SurfaceStatus.Suboptimal ? Frame : null));
    }

    public void Dispose() => DisposeCount++;
}
