using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Validation.Tests;

/// <summary>Exercises presentation through the public validation decorator.</summary>
public sealed class PresentationTests
{
    /// <summary>Checks canonical wrappers and borrowed native ownership.</summary>
    [Fact]
    public void SurfaceIsCanonicalAndBorrowed()
    {
        using var device = new ValidationDevice(new PresentationBackend());
        var raw = new PresentationSurface();
        IGraphicsSurface surface = device.WrapSurface(raw);
        Assert.Same(surface, device.WrapSurface(raw));
        Assert.NotSame(raw, surface);
        Assert.Same(raw.Capabilities, surface.GetCapabilities());
        surface.Dispose();
        surface.Dispose();
        Assert.Equal(0, raw.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => surface.GetCapabilities());
    }

    /// <summary>Rejects redecorating a surface.</summary>
    [Fact]
    public void AlreadyWrappedSurfaceIsRejected()
    {
        using var device = new ValidationDevice(new PresentationBackend());
        using IGraphicsSurface surface = device.WrapSurface(new PresentationSurface());
        Assert.Throws<ArgumentException>(() => device.WrapSurface(surface));
    }

    /// <summary>Rejects null before native work.</summary>
    [Fact]
    public void NullSurfaceIsRejected()
    {
        using var device = new ValidationDevice(new PresentationBackend());
        Assert.Throws<ArgumentNullException>(() => device.WrapSurface(null!));
    }

    /// <summary>Protects a surface while its owned swapchain is alive.</summary>
    [Fact]
    public void SurfaceCannotDisposeOrCreateSecondSwapchainWithLiveChild()
    {
        using var device = new ValidationDevice(new PresentationBackend());
        var raw = new PresentationSurface();
        using IGraphicsSurface surface = device.WrapSurface(raw);
        using IGraphicsSwapchain swapchain = surface.CreateSwapchain(new() { Width = 32, Height = 16 });
        Assert.Throws<InvalidOperationException>(surface.Dispose);
        Assert.Throws<InvalidOperationException>(() => surface.CreateSwapchain(new() { Width = 64, Height = 32 }));
        Assert.Equal(1, raw.CreateCount);
    }

    /// <summary>Forwards configuration and disposes the native swapchain exactly once.</summary>
    [Fact]
    public void SwapchainConfigurationAndDisposalAreForwarded()
    {
        using var device = new ValidationDevice(new PresentationBackend());
        var raw = new PresentationSurface();
        using IGraphicsSurface surface = device.WrapSurface(raw);
        var desc = new SwapchainDesc { Width = 64, Height = 32 };
        IGraphicsSwapchain swapchain = surface.CreateSwapchain(desc);
        Assert.Same(desc, swapchain.Configuration);
        SwapchainDesc next = desc with { Width = 128 };
        swapchain.Reconfigure(next);
        Assert.Same(next, swapchain.Configuration);
        swapchain.Dispose();
        swapchain.Dispose();
        Assert.Equal(1, raw.Swapchain.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => swapchain.Configuration);
    }

    /// <summary>Returns a wrapped frame and a borrowed, wrapped image.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task AcquisitionWrapsFrameAndTextureAsync()
    {
        using var device = new ValidationDevice(new PresentationBackend());
        var raw = new PresentationSurface();
        using IGraphicsSurface surface = device.WrapSurface(raw);
        using IGraphicsSwapchain swapchain = surface.CreateSwapchain(new() { Width = 32, Height = 16 });
        SurfaceAcquireResult result = await swapchain.AcquireNextFrameAsync();
        IGraphicsSurfaceFrame frame = Assert.IsAssignableFrom<IGraphicsSurfaceFrame>(result.Frame);
        Assert.Equal(SurfaceStatus.Success, result.Status);
        Assert.NotSame(raw.Swapchain.Frame, frame);
        Assert.NotSame(raw.Swapchain.Frame.Image, frame.Texture);
        Assert.Same(frame.Texture, frame.Texture);
        Assert.Equal(32u, frame.Texture.Width);
        Assert.Equal(16u, frame.Texture.Height);
        Assert.Equal(1u, frame.Texture.MipLevels);
        Assert.Equal(1u, frame.Texture.ArrayLayers);
        Assert.Equal(TextureFormat.Bgra8Unorm, frame.Texture.Format);
        Assert.Equal(TextureUsage.RenderAttachment, frame.Texture.Usage);
        Assert.Equal((32u, 16u), frame.Texture.GetMipSize(0));
        await frame.WaitForReleaseAsync();
        frame.Dispose();
        Assert.Equal(0, raw.Swapchain.Frame.Image.DisposeCount);
    }

    /// <summary>Preserves every native recovery outcome without returning a lease.</summary>
    /// <param name="status">The recovery status.</param>
    /// <returns>The asynchronous test.</returns>
    [Theory]
    [InlineData(SurfaceStatus.Timeout)]
    [InlineData(SurfaceStatus.Outdated)]
    [InlineData(SurfaceStatus.Lost)]
    [InlineData(SurfaceStatus.DeviceLost)]
    public async Task RecoveryAcquisitionHasNoFrameAsync(SurfaceStatus status)
    {
        using var device = new ValidationDevice(new PresentationBackend());
        var raw = new PresentationSurface();
        raw.Swapchain.AcquireStatus = status;
        using IGraphicsSurface surface = device.WrapSurface(raw);
        using IGraphicsSwapchain swapchain = surface.CreateSwapchain(new() { Width = 32, Height = 16 });
        SurfaceAcquireResult result = await swapchain.AcquireNextFrameAsync();
        Assert.Equal(status, result.Status);
        Assert.Null(result.Frame);
        Assert.Equal(0, raw.Swapchain.Frame.DisposeCount);
    }

    /// <summary>Suboptimal acquisition still wraps the usable frame.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task SuboptimalAcquisitionReturnsLeaseAsync()
    {
        using var device = new ValidationDevice(new PresentationBackend());
        var raw = new PresentationSurface();
        raw.Swapchain.AcquireStatus = SurfaceStatus.Suboptimal;
        using IGraphicsSurface surface = device.WrapSurface(raw);
        using IGraphicsSwapchain swapchain = surface.CreateSwapchain(new() { Width = 32, Height = 16 });
        SurfaceAcquireResult result = await swapchain.AcquireNextFrameAsync();
        Assert.Equal(SurfaceStatus.Suboptimal, result.Status);
        Assert.NotNull(result.Frame);
        await result.Frame.WaitForReleaseAsync();
        result.Frame.Dispose();
    }

    /// <summary>Pre-cancellation never calls the native acquire or poisons reconfiguration.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task CanceledAcquireDoesNotReachBackendAsync()
    {
        using var device = new ValidationDevice(new PresentationBackend());
        var raw = new PresentationSurface();
        using IGraphicsSurface surface = device.WrapSurface(raw);
        using IGraphicsSwapchain swapchain = surface.CreateSwapchain(new() { Width = 32, Height = 16 });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await swapchain.AcquireNextFrameAsync(cancellationToken: cancellation.Token));
        Assert.Equal(0, raw.Swapchain.AcquireCount);
        swapchain.Reconfigure(new() { Width = 64, Height = 32 });
        Assert.Equal(2, raw.Swapchain.ReconfigureCount);
    }

    /// <summary>Unwraps acquire signals and retains them until explicit native release.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task AcquireSignalIsRawAndRetainedUntilReleaseAsync()
    {
        var backend = new PresentationBackend();
        using var device = new ValidationDevice(backend);
        using IGraphicsSemaphore signal = device.CreateSemaphore();
        var raw = new PresentationSurface();
        using IGraphicsSurface surface = device.WrapSurface(raw);
        using IGraphicsSwapchain swapchain = surface.CreateSwapchain(new() { Width = 32, Height = 16 });
        SurfaceAcquireResult result = await swapchain.AcquireNextFrameAsync(signal);
        Assert.Same(backend.Semaphore, raw.Swapchain.LastSignal);
        Assert.Throws<InvalidOperationException>(signal.Dispose);
        Assert.NotNull(result.Frame);
        await result.Frame.WaitForReleaseAsync();
        result.Frame.Dispose();
        signal.Dispose();
        Assert.Equal(1, backend.Semaphore.DisposeCount);
    }

    /// <summary>Rejects raw semaphores rather than leaking them across the domain boundary.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task RawAcquireSemaphoreIsRejectedAsync()
    {
        using var device = new ValidationDevice(new PresentationBackend());
        var raw = new PresentationSurface();
        using IGraphicsSurface surface = device.WrapSurface(raw);
        using IGraphicsSwapchain swapchain = surface.CreateSwapchain(new() { Width = 32, Height = 16 });
        using var signal = new PresentationSemaphore();
        await Assert.ThrowsAsync<ArgumentException>(async () => await swapchain.AcquireNextFrameAsync(signal));
        Assert.Equal(0, raw.Swapchain.AcquireCount);
    }

    /// <summary>Rejects foreign-domain semaphores before native acquisition.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task ForeignAcquireSemaphoreIsRejectedAsync()
    {
        using var other = new ValidationDevice(new PresentationBackend());
        using IGraphicsSemaphore signal = other.CreateSemaphore();
        using var device = new ValidationDevice(new PresentationBackend());
        var raw = new PresentationSurface();
        using IGraphicsSurface surface = device.WrapSurface(raw);
        using IGraphicsSwapchain swapchain = surface.CreateSwapchain(new() { Width = 32, Height = 16 });
        await Assert.ThrowsAsync<ArgumentException>(async () => await swapchain.AcquireNextFrameAsync(signal));
        Assert.Equal(0, raw.Swapchain.AcquireCount);
    }

    /// <summary>Requires an explicit release observation and enforces borrowed-image ownership.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task FrameDisposalRequiresReleaseAndNeverDisposesImageAsync()
    {
        using var device = new ValidationDevice(new PresentationBackend());
        var raw = new PresentationSurface();
        using IGraphicsSurface surface = device.WrapSurface(raw);
        using IGraphicsSwapchain swapchain = surface.CreateSwapchain(new() { Width = 32, Height = 16 });
        SurfaceAcquireResult result = await swapchain.AcquireNextFrameAsync();
        Assert.NotNull(result.Frame);
        IGraphicsSurfaceFrame frame = result.Frame;
        IGraphicsTexture image = frame.Texture;
        Assert.Throws<InvalidOperationException>(image.Dispose);
        Assert.Throws<InvalidOperationException>(frame.Dispose);
        Assert.Throws<InvalidOperationException>(swapchain.Dispose);
        Assert.Throws<InvalidOperationException>(() => swapchain.Reconfigure(new() { Width = 64, Height = 32 }));
        Assert.Equal(0, raw.Swapchain.Frame.DisposeCount);
        await frame.WaitForReleaseAsync();
        frame.Dispose();
        frame.Dispose();
        Assert.Equal(SurfaceFrameStatus.Disposed, frame.Status);
        Assert.Equal(1, raw.Swapchain.Frame.DisposeCount);
        Assert.Equal(0, raw.Swapchain.Frame.Image.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => image.Width);
        Assert.Throws<ObjectDisposedException>(() => frame.Texture);
    }

    /// <summary>A live view prevents releasing its borrowed image owner.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task ViewRetainsFrameAndReturnsCanonicalTextureAsync()
    {
        using var device = new ValidationDevice(new PresentationBackend());
        var raw = new PresentationSurface();
        using IGraphicsSurface surface = device.WrapSurface(raw);
        using IGraphicsSwapchain swapchain = surface.CreateSwapchain(new() { Width = 32, Height = 16 });
        SurfaceAcquireResult result = await swapchain.AcquireNextFrameAsync();
        Assert.NotNull(result.Frame);
        IGraphicsSurfaceFrame frame = result.Frame;
        IGraphicsTexture image = frame.Texture;
        IGraphicsTextureView view = image.CreateView();
        Assert.Same(image, view.Texture);
        Assert.NotSame(raw.Swapchain.Frame.Image.LastView, view);
        Assert.Same(raw.Swapchain.Frame.Image.LastView!.Info, view.Info);
        await frame.WaitForReleaseAsync();
        Assert.Throws<InvalidOperationException>(frame.Dispose);
        Assert.Equal(0, raw.Swapchain.Frame.DisposeCount);
        view.Dispose();
        view.Dispose();
        Assert.Equal(1, raw.Swapchain.Frame.Image.LastView.DisposeCount);
        frame.Dispose();
        Assert.Throws<ObjectDisposedException>(() => view.Info);
    }

    /// <summary>Cancelled release waiting cannot authorize disposal.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task CanceledReleaseWaitRetainsFrameAsync()
    {
        using var device = new ValidationDevice(new PresentationBackend());
        using IGraphicsSurface surface = device.WrapSurface(new PresentationSurface());
        using IGraphicsSwapchain swapchain = surface.CreateSwapchain(new() { Width = 32, Height = 16 });
        SurfaceAcquireResult result = await swapchain.AcquireNextFrameAsync();
        Assert.NotNull(result.Frame);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await result.Frame.WaitForReleaseAsync(cancellation.Token));
        Assert.Throws<InvalidOperationException>(result.Frame.Dispose);
        await result.Frame.WaitForReleaseAsync();
        result.Frame.Dispose();
    }

    /// <summary>Present is rejected before native use without a submitted Present transition.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task PresentWithoutTransitionIsRejectedAsync()
    {
        using var device = new ValidationDevice(new PresentationBackend());
        var raw = new PresentationSurface();
        using IGraphicsSurface surface = device.WrapSurface(raw);
        using IGraphicsSwapchain swapchain = surface.CreateSwapchain(new() { Width = 32, Height = 16 });
        SurfaceAcquireResult result = await swapchain.AcquireNextFrameAsync();
        Assert.NotNull(result.Frame);
        Assert.Throws<InvalidOperationException>(() => result.Frame.Present());
        Assert.Equal(0, raw.Swapchain.Frame.PresentCount);
        await result.Frame.WaitForReleaseAsync();
        result.Frame.Dispose();
    }

    /// <summary>A failed wrapper constructor must roll back its partial registration.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task FailedFrameConstructionMustNotStrandSwapchainAsync()
    {
        var device = new ValidationDevice(new PresentationBackend());
        var raw = new PresentationSurface();
        IGraphicsSurface surface = device.WrapSurface(raw);
        IGraphicsSwapchain swapchain = surface.CreateSwapchain(new() { Width = 32, Height = 16 });
        raw.Swapchain.Frame.ThrowOnTexture = true;
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await swapchain.AcquireNextFrameAsync());
        Assert.Equal(1, raw.Swapchain.Frame.DisposeCount);
        swapchain.Reconfigure(new() { Width = 64, Height = 32 });
        swapchain.Dispose();
        surface.Dispose();
        device.Dispose();
    }

    /// <summary>Recording a borrowed image retains its complete presentation ownership chain.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task RecordedImageUseRetainsFrameUntilCommandDisposalAsync()
    {
        using var device = new ValidationDevice(new PresentationBackend());
        var raw = new PresentationSurface();
        using IGraphicsSurface surface = device.WrapSurface(raw);
        using IGraphicsSwapchain swapchain = surface.CreateSwapchain(new() { Width = 32, Height = 16 });
        SurfaceAcquireResult result = await swapchain.AcquireNextFrameAsync();
        Assert.NotNull(result.Frame);
        IGraphicsSurfaceFrame frame = result.Frame;
        await frame.WaitForReleaseAsync();
        IGraphicsCommandBuffer command = device.CreateCommandBuffer(new());
        command.Barrier(new TextureBarrierDesc
        {
            Texture = frame.Texture,
            Range = new(0, 1, 0, 1),
            BeforeState = TextureState.Undefined,
            AfterState = TextureState.Present,
            Before = new(PipelineStage.None, ResourceAccess.None),
            After = new(PipelineStage.AllCommands, ResourceAccess.None),
        });
        Assert.Throws<InvalidOperationException>(frame.Dispose);
        Assert.Equal(0, raw.Swapchain.Frame.DisposeCount);
        command.Dispose();
        frame.Dispose();
        Assert.Equal(1, raw.Swapchain.Frame.DisposeCount);
    }

    /// <summary>Opaque native acquisition failure prohibits unsafe reuse or disposal.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task NativeAcquireFailureQuarantinesSwapchainAsync()
    {
        var device = new ValidationDevice(new PresentationBackend());
        var raw = new PresentationSurface();
        IGraphicsSurface surface = device.WrapSurface(raw);
        IGraphicsSwapchain swapchain = surface.CreateSwapchain(new() { Width = 32, Height = 16 });
        raw.Swapchain.ThrowOnAcquire = true;
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await swapchain.AcquireNextFrameAsync());
        raw.Swapchain.ThrowOnAcquire = false;
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await swapchain.AcquireNextFrameAsync());
        Assert.Throws<InvalidOperationException>(() => swapchain.Reconfigure(new() { Width = 64, Height = 32 }));
        Assert.Throws<InvalidOperationException>(swapchain.Dispose);
        Assert.Equal(1, raw.Swapchain.AcquireCount);
        Assert.Equal(0, raw.Swapchain.DisposeCount);
    }
}
