using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.UI.Application.Tests;

/// <summary>Checks the actual presenter frame owner retains GPU resources when explicit waits fail.</summary>
public sealed class FrameTests
{
    /// <summary>Checks submission and presentation are independently confirmed and failed waits can be retried.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task FailedWaitRetainsEveryChildUntilBothBoundariesCompleteAsync()
    {
        var submission = new Submission { Failures = 1 };
        var image = new Image { Failures = 1 };
        var resource = new Resource();
        var frame = new UiHostPresenter.Frame(Guid.NewGuid()) { Submission = submission, Image = image };
        frame.Own(resource);
        Assert.Throws<InvalidOperationException>(frame.Dispose);
        await Assert.ThrowsAsync<ArithmeticException>(() => frame.DrainAsync().AsTask());
        Assert.Equal(0, resource.Disposals);
        Assert.Equal(0, image.Waits);
        await Assert.ThrowsAsync<ArithmeticException>(() => frame.DrainAsync().AsTask());
        Assert.Equal(0, resource.Disposals);
        Assert.Equal(2, submission.Waits);
        Assert.Throws<InvalidOperationException>(frame.Dispose);
        await frame.DrainAsync();
        Assert.Equal(2, submission.Waits);
        Assert.Equal(2, image.Waits);
        frame.Dispose();
        frame.Dispose();
        Assert.Equal(1, resource.Disposals);
        Assert.Equal(1, submission.Disposals);
        Assert.Equal(1, image.Disposals);
    }

    /// <summary>Checks confirmed retirement wakes a pending completion wait before releasing its resources.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task RetirementUnblocksOutstandingGpuWaitAsync()
    {
        var submission = new PendingSubmission();
        var frame = new UiHostPresenter.Frame(Guid.NewGuid()) { Submission = submission };
        Task waiting = frame.DrainAsync().AsTask();
        Assert.False(waiting.IsCompleted);
        frame.ConfirmDeviceRetirement();
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(frame.IsRetired);
        frame.Dispose();
    }

    /// <summary>Checks confirmed lost-device retirement allows releasing frames without accepting a failed wait as completion.</summary>
    [Fact]
    public void ConfirmedRetirementReleasesTheRetainedFrame()
    {
        var frame = new UiHostPresenter.Frame(Guid.NewGuid()) { Submission = new Submission(), Image = new Image() };
        Resource resource = frame.Own(new Resource());
        Assert.Throws<InvalidOperationException>(frame.Dispose);
        frame.ConfirmDeviceRetirement();
        frame.Dispose();
        Assert.Equal(1, resource.Disposals);
    }

    /// <summary>Checks backend preference order, sRGB encoding choice and optional readback usage.</summary>
    /// <param name="first">The preferred format.</param>
    /// <param name="second">The fallback format.</param>
    /// <param name="expected">The selected format.</param>
    /// <param name="copy">Whether native readback is supported.</param>
    [Theory]
    [InlineData(TextureFormat.Rgba8Unorm, TextureFormat.Bgra8Unorm, TextureFormat.Rgba8Unorm, true)]
    [InlineData(TextureFormat.Rgba8Unorm, TextureFormat.Bgra8Srgb, TextureFormat.Bgra8Srgb, false)]
    [InlineData(TextureFormat.Rgba8Srgb, TextureFormat.Bgra8Srgb, TextureFormat.Rgba8Srgb, true)]
    public void PresentationHonorsPreferredFormatAndReadbackCapability(TextureFormat first, TextureFormat second, TextureFormat expected, bool copy)
    {
        using var surface = new Surface([first, second], copy);
        SwapchainDesc configuration = UiHostPresenter.Configuration(surface, 800, 600);
        Assert.Equal(expected, configuration.Format);
        Assert.Equal(SurfaceAlphaMode.Opaque, configuration.AlphaMode);
        Assert.Equal(TextureUsage.RenderAttachment | (copy ? TextureUsage.CopySource : 0), configuration.Usage);
    }

    private sealed class Surface(IReadOnlyList<TextureFormat> formats, bool copy) : IGraphicsSurface
    {
        public void Dispose()
        {
        }

        public SurfaceCapabilities GetCapabilities() => new()
        {
            Formats = formats,
            AlphaModes = [SurfaceAlphaMode.Opaque],
            PresentModes = [PresentMode.Fifo],
            SupportedUsage = TextureUsage.RenderAttachment | (copy ? TextureUsage.CopySource : 0),
            MaxWidth = 4096,
            MaxHeight = 4096,
        };

        public IGraphicsSwapchain CreateSwapchain(SwapchainDesc desc) => throw new NotSupportedException();
    }

    private sealed class Resource : IDisposable
    {
        internal int Disposals { get; private set; }

        public void Dispose() => Disposals++;
    }

    private sealed class PendingSubmission : IGraphicsSubmission
    {
        private readonly TaskCompletionSource _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SubmissionStatus Status => SubmissionStatus.Pending;

        public void Dispose()
        {
        }

        public ValueTask WaitAsync(CancellationToken cancellationToken = default) => new(_pending.Task.WaitAsync(cancellationToken));
    }

    private sealed class Submission : IGraphicsSubmission
    {
        public SubmissionStatus Status => SubmissionStatus.Pending;

        internal int Failures { get; set; }

        internal int Waits { get; private set; }

        internal int Disposals { get; private set; }

        public void Dispose() => Disposals++;

        public ValueTask WaitAsync(CancellationToken cancellationToken = default)
        {
            Waits++;
            if (Failures-- > 0)
            {
                throw new ArithmeticException("submission");
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class Image : IGraphicsSurfaceFrame
    {
        public SurfaceFrameStatus Status => SurfaceFrameStatus.Presented;

        public IGraphicsTexture Texture => throw new NotSupportedException();

        internal int Failures { get; set; }

        internal int Waits { get; private set; }

        internal int Disposals { get; private set; }

        public void Dispose() => Disposals++;

        public SurfaceStatus Present(IReadOnlyList<IGraphicsSemaphore>? waitSemaphores = null) => throw new NotSupportedException();

        public ValueTask WaitForReleaseAsync(CancellationToken cancellationToken = default)
        {
            Waits++;
            if (Failures-- > 0)
            {
                throw new ArithmeticException("presentation");
            }

            return ValueTask.CompletedTask;
        }
    }
}
