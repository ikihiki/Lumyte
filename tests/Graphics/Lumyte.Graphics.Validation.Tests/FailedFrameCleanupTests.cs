using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Validation.Tests;

/// <summary>Failed wrapper cleanup must retain opaque native acquisition effects.</summary>
public sealed class FailedFrameCleanupTests
{
    /// <summary>Failed native disposal preserves the original error and quarantines all owners.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task FailedCleanupRetainsAcquisitionAsync()
    {
        var device = new ValidationDevice(new PresentationBackend());
        var raw = new FailingSurface();
        IGraphicsSurface surface = device.WrapSurface(raw);
        IGraphicsSwapchain swapchain = surface.CreateSwapchain(new() { Width = 32, Height = 16 });
        IGraphicsSemaphore signal = device.CreateSemaphore();
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await swapchain.AcquireNextFrameAsync(signal));
        Assert.Same(raw.Swapchain.Frame.ConstructionError, error);
        Assert.Equal(1, raw.Swapchain.Frame.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => swapchain.Reconfigure(new() { Width = 64, Height = 32 }));
        Assert.Throws<InvalidOperationException>(signal.Dispose);
        Assert.Throws<InvalidOperationException>(swapchain.Dispose);
        Assert.Throws<InvalidOperationException>(surface.Dispose);
        Assert.Throws<InvalidOperationException>(device.Dispose);
        await device.WaitForRetirementAsync();
        Assert.Equal(1, raw.Swapchain.Frame.DisposeCount);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await swapchain.AcquireNextFrameAsync());
    }

    /// <summary>Successful cleanup retires native use without consuming an issued binary signal.</summary>
    /// <param name="status">The successful native acquisition outcome.</param>
    /// <returns>The asynchronous test.</returns>
    [Theory]
    [InlineData(SurfaceStatus.Success)]
    [InlineData(SurfaceStatus.Suboptimal)]
    public async Task SuccessfulCleanupPreservesIssuedSignalAsync(SurfaceStatus status)
    {
        var backend = new QueueBackend();
        using var device = new ValidationDevice(backend);
        var raw = new FailingSurface();
        raw.Swapchain.AcquireStatus = status;
        raw.Swapchain.Frame.ThrowOnDispose = false;
        using IGraphicsSurface surface = device.WrapSurface(raw);
        using IGraphicsSwapchain swapchain = surface.CreateSwapchain(new() { Width = 32, Height = 16 });
        using IGraphicsSemaphore signal = device.CreateSemaphore();
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await swapchain.AcquireNextFrameAsync(signal));
        Assert.Same(raw.Swapchain.Frame.ConstructionError, error);
        Assert.Equal(1, raw.Swapchain.Frame.DisposeCount);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await swapchain.AcquireNextFrameAsync(signal));
        Assert.Equal(1, raw.Swapchain.AcquireCount);
        Assert.Throws<InvalidOperationException>(() => device.Queue.Submit(new QueueSubmitDesc { SignalSemaphores = [signal] }));
        Assert.Equal(0, backend.NativeQueue.SubmitCount);
        swapchain.Reconfigure(new() { Width = 64, Height = 32 });

        using IGraphicsSubmission wait = device.Queue.Submit(new QueueSubmitDesc
        {
            WaitSemaphores = [new() { Semaphore = signal, Stages = PipelineStage.AllCommands }],
        });
        Assert.IsType<PresentationSemaphore>(backend.NativeQueue.Last!.WaitSemaphores[0].Semaphore);
        backend.NativeQueue.Submission.Complete();
        await wait.WaitAsync();
        error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await swapchain.AcquireNextFrameAsync(signal));
        Assert.Same(raw.Swapchain.Frame.ConstructionError, error);
        Assert.Equal(2, raw.Swapchain.AcquireCount);
        Assert.Equal(2, raw.Swapchain.Frame.DisposeCount);
        Assert.Equal(1, backend.NativeQueue.SubmitCount);
        signal.Dispose();
    }

    /// <summary>A cleaned-up failed frame permits disposing its still-signaled semaphore.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task SuccessfulCleanupReleasesSemaphoreUseAsync()
    {
        using var device = new ValidationDevice(new QueueBackend());
        var raw = new FailingSurface();
        raw.Swapchain.Frame.ThrowOnDispose = false;
        using IGraphicsSurface surface = device.WrapSurface(raw);
        using IGraphicsSwapchain swapchain = surface.CreateSwapchain(new() { Width = 32, Height = 16 });
        IGraphicsSemaphore signal = device.CreateSemaphore();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await swapchain.AcquireNextFrameAsync(signal));
        signal.Dispose();
        swapchain.Reconfigure(new() { Width = 64, Height = 32 });
    }

    /// <summary>Native success without a lease retains its signal and quarantines the invalid contract.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task SuccessfulAcquireWithoutFrameIsQuarantinedAsync()
    {
        var device = new ValidationDevice(new QueueBackend());
        var raw = new FailingSurface();
        raw.Swapchain.ReturnNoFrame = true;
        IGraphicsSurface surface = device.WrapSurface(raw);
        IGraphicsSwapchain swapchain = surface.CreateSwapchain(new() { Width = 32, Height = 16 });
        IGraphicsSemaphore signal = device.CreateSemaphore();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await swapchain.AcquireNextFrameAsync(signal));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await swapchain.AcquireNextFrameAsync(signal));
        Assert.Equal(1, raw.Swapchain.AcquireCount);
        Assert.Equal(0, raw.Swapchain.Frame.DisposeCount);
        Assert.Throws<InvalidOperationException>(signal.Dispose);
        Assert.Throws<InvalidOperationException>(swapchain.Dispose);
        Assert.Throws<InvalidOperationException>(surface.Dispose);
        Assert.Throws<InvalidOperationException>(device.Dispose);
    }

    private sealed class FailingSurface : IGraphicsSurface
    {
        public FailingSwapchain Swapchain { get; } = new();

        public SurfaceCapabilities GetCapabilities() => new PresentationSurface().GetCapabilities();

        public IGraphicsSwapchain CreateSwapchain(SwapchainDesc desc) => Swapchain;

        public void Dispose()
        {
        }
    }

    private sealed class FailingSwapchain : IGraphicsSwapchain
    {
        public FailingFrame Frame { get; } = new();

        public SurfaceStatus AcquireStatus { get; set; } = SurfaceStatus.Success;

        public int AcquireCount { get; private set; }

        public bool ReturnNoFrame { get; set; }

        public SwapchainDesc Configuration { get; } = new() { Width = 32, Height = 16 };

        public void Reconfigure(SwapchainDesc desc)
        {
        }

        public ValueTask<SurfaceAcquireResult> AcquireNextFrameAsync(IGraphicsSemaphore? signalSemaphore = null, CancellationToken cancellationToken = default)
        {
            AcquireCount++;
            return ValueTask.FromResult(new SurfaceAcquireResult(AcquireStatus, ReturnNoFrame ? null : Frame));
        }

        public void Dispose()
        {
        }
    }

    private sealed class FailingFrame : IGraphicsSurfaceFrame
    {
        public InvalidOperationException ConstructionError { get; } = new("Texture lookup failed.");

        public int DisposeCount { get; private set; }

        public bool ThrowOnDispose { get; set; } = true;

        public SurfaceFrameStatus Status => SurfaceFrameStatus.Acquired;

        public IGraphicsTexture Texture => throw ConstructionError;

        public SurfaceStatus Present(IReadOnlyList<IGraphicsSemaphore>? waitSemaphores = null) => throw new NotSupportedException();

        public ValueTask WaitForReleaseAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void Dispose()
        {
            DisposeCount++;
            if (ThrowOnDispose)
            {
                throw new InvalidOperationException("Native acquisition is still pending.");
            }
        }
    }
}
