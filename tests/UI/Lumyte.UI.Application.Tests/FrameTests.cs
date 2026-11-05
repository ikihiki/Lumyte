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

    /// <summary>Checks exact submission completion releases commands before publishing retained residency.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task RetainedCompletionDisposesCommandsBeforePublishingAsync()
    {
        var fixture = new RetainedFrameFixture();
        var submission = new Submission();
        var image = new Image();
        fixture.Frame.Image = image;
        fixture.Record();
        fixture.Submit(submission);
        Assert.Throws<InvalidOperationException>(fixture.Frame.Dispose);
        Assert.Empty(fixture.Publications);
        await fixture.Frame.DrainAsync();
        Assert.Empty(fixture.Publications);
        fixture.Frame.Dispose();
        fixture.Frame.Dispose();
        Assert.Equal(new[] { "commands", "retained" }, fixture.Events);
        Assert.Equal(new[] { true }, fixture.Publications);
        Assert.Equal(1, submission.Disposals);
        Assert.Equal(1, image.Disposals);
        Assert.Null(fixture.Frame.Retained);
    }

    /// <summary>Checks neither failed GPU completion nor failed presentation releases real retained ownership.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task FailedWaitsKeepRetainedPreparedOwnerAliveAsync()
    {
        var fixture = new RetainedFrameFixture();
        var submission = new Submission { Failures = 1 };
        var image = new Image { Failures = 1 };
        fixture.Frame.Image = image;
        fixture.Record();
        fixture.Submit(submission);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            await Assert.ThrowsAsync<ArithmeticException>(() => fixture.Frame.DrainAsync().AsTask());
            Assert.Throws<InvalidOperationException>(fixture.Frame.Dispose);
            Assert.Same(fixture.Prepared, fixture.Frame.Retained);
            Assert.Empty(fixture.Publications);
            Assert.Equal(0, fixture.Commands.DisposeAttempts);
            Assert.Equal(0, submission.Disposals);
            Assert.Equal(0, image.Disposals);
        }

        await fixture.Frame.DrainAsync();
        fixture.Frame.Dispose();
        Assert.Equal(new[] { true }, fixture.Publications);
        Assert.Equal(2, submission.Waits);
        Assert.Equal(2, image.Waits);
    }

    /// <summary>Checks explicitly never-submitted recorded work aborts only after command disposal.</summary>
    [Fact]
    public void UnsubmittedRetainedFrameAbortsWithoutPublishing()
    {
        var fixture = new RetainedFrameFixture();
        fixture.Record();
        fixture.Frame.Dispose();
        fixture.Frame.Dispose();
        Assert.Equal(new[] { "commands", "retained" }, fixture.Events);
        Assert.Equal(new[] { false }, fixture.Publications);
        Assert.Null(fixture.Frame.Retained);
    }

    /// <summary>Checks preparation abandoned before recording releases ownership without residency publication.</summary>
    [Fact]
    public void UnrecordedRetainedFrameDisposesWithoutPublishing()
    {
        var fixture = new RetainedFrameFixture();
        fixture.Frame.Dispose();
        fixture.Frame.Dispose();
        Assert.Equal(new[] { false }, fixture.Publications);
        Assert.Equal(1, fixture.ReleaseAttempts);
        Assert.Null(fixture.Frame.Retained);
    }

    /// <summary>Checks a throwing submit with no tracker remains quarantined until device retirement.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task UncertainSubmitRetainsOwnerUntilConfirmedDeviceRetirementAsync()
    {
        var fixture = new RetainedFrameFixture();
        fixture.Record();
        fixture.Frame.SubmissionAttempted = true;
        fixture.Commands.Submit();
        await fixture.Frame.DrainAsync();
        Assert.Throws<InvalidOperationException>(fixture.Frame.Dispose);
        Assert.Empty(fixture.Publications);
        Assert.Equal(0, fixture.Commands.DisposeAttempts);
        fixture.Frame.ConfirmDeviceRetirement();
        fixture.Frame.Dispose();
        fixture.Frame.Dispose();
        Assert.Equal(new[] { "commands", "retained" }, fixture.Events);
        Assert.Equal(new[] { false }, fixture.Publications);
    }

    /// <summary>Checks confirmed retirement never publishes residency even with an attached pending submission.</summary>
    [Fact]
    public void RetirementReleasesPendingRetainedSubmissionWithoutPublishing()
    {
        var fixture = new RetainedFrameFixture();
        var submission = new Submission();
        fixture.Record();
        fixture.Submit(submission);
        fixture.Frame.ConfirmDeviceRetirement();
        fixture.Frame.Dispose();
        Assert.Equal(new[] { false }, fixture.Publications);
        Assert.Equal(0, submission.Waits);
        Assert.Equal(1, submission.Disposals);
    }

    /// <summary>Checks failed command or retained cleanup can be retried without prematurely losing ownership.</summary>
    /// <param name="commandFailure">Whether command disposal fails rather than retained release.</param>
    /// <returns>The verification.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RetainedCleanupFailurePreservesOwnerForRetryAsync(bool commandFailure)
    {
        var fixture = new RetainedFrameFixture();
        var submission = new Submission();
        fixture.Commands.Failures = commandFailure ? 1 : 0;
        fixture.ReleaseFailures = commandFailure ? 0 : 1;
        fixture.Record();
        fixture.Submit(submission);
        await fixture.Frame.DrainAsync();
        Assert.Throws<ArithmeticException>(fixture.Frame.Dispose);
        Assert.Same(fixture.Prepared, fixture.Frame.Retained);
        Assert.Empty(fixture.Publications);
        Assert.Equal(0, submission.Disposals);
        fixture.Frame.Dispose();
        Assert.Equal(new[] { true }, fixture.Publications);
        Assert.Equal(commandFailure ? 2 : 1, fixture.Commands.DisposeAttempts);
        Assert.Equal(commandFailure ? 1 : 2, fixture.ReleaseAttempts);
        Assert.Equal(1, submission.Disposals);
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
        public SubmissionStatus Status { get; private set; } = SubmissionStatus.Pending;

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

            Status = SubmissionStatus.Completed;
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
