using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.UI.Application.Tests;

/// <summary>Checks actual presenter frames return intermediate resources only after their complete lifetime.</summary>
public sealed class IntermediateTexturePoolTests
{
    /// <summary>Checks one texture and view serve one hundred completed frames.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task CompletedFramesReuseOneTextureAndViewAsync()
    {
        var factory = new Factory();
        using var pool = new UiIntermediateTexturePool(factory.Create);
        for (int index = 0; index < 100; index++)
        {
            UiIntermediateTexturePool.Entry entry = pool.Rent(640, 480, TextureFormat.Rgba8Srgb);
            Assert.Equal(index == 0 ? TextureState.Undefined : TextureState.Sampled, entry.ColorAttachmentBarrier.BeforeState);
            var frame = new UiHostPresenter.Frame(Guid.Empty) { Intermediate = entry, Submission = new Submission(), Image = new Image() };
            frame.Own(new Cleanup(() => Assert.Equal(0, pool.IdleCount)));
            entry.State = TextureState.Sampled;
            await frame.DrainAsync();
            frame.Dispose();
            frame.Dispose();
        }

        Texture texture = Assert.Single(factory.Created);
        Assert.Equal(1, texture.Views);
        Assert.Equal(1, pool.IdleCount);
        Assert.Equal(0, texture.Disposals);
        pool.Dispose();
        Assert.Equal(1, texture.Disposals);
        Assert.Equal(1, texture.View!.Disposals);
    }

    /// <summary>Checks GPU completion alone cannot return a still-presented target.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task BothBoundariesAndDependentCleanupPrecedeReturnAsync()
    {
        var factory = new Factory();
        using var pool = new UiIntermediateTexturePool(factory.Create);
        UiIntermediateTexturePool.Entry entry = pool.Rent(10, 10, TextureFormat.Rgba8Srgb);
        var submission = new Submission { Gate = new() };
        var image = new Image { Gate = new() };
        var frame = new UiHostPresenter.Frame(Guid.Empty) { Intermediate = entry, Submission = submission, Image = image };
        Task drain = frame.DrainAsync().AsTask();
        Assert.False(drain.IsCompleted);
        Assert.Throws<InvalidOperationException>(frame.Dispose);
        submission.Gate.SetResult();
        await image.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(drain.IsCompleted);
        Assert.Equal(0, pool.IdleCount);
        UiIntermediateTexturePool.Entry simultaneous = pool.Rent(10, 10, TextureFormat.Rgba8Srgb);
        Assert.NotSame(entry.Texture, simultaneous.Texture);
        frame.Own(new Cleanup(() => Assert.Equal(0, pool.IdleCount)));
        image.Gate.SetResult();
        await drain;
        frame.Dispose();
        Assert.Equal(1, pool.IdleCount);
        simultaneous.Release();
        Assert.Equal(2, pool.IdleCount);
    }

    /// <summary>Checks a failed wait makes the target ineligible even when retry eventually succeeds.</summary>
    /// <param name="submissionFailure">Whether submission or presentation wait fails.</param>
    /// <returns>The verification.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedWaitNeverRecyclesTheTargetAsync(bool submissionFailure)
    {
        var factory = new Factory();
        using var pool = new UiIntermediateTexturePool(factory.Create);
        UiIntermediateTexturePool.Entry entry = pool.Rent(10, 10, TextureFormat.Rgba8Srgb);
        var frame = new UiHostPresenter.Frame(Guid.Empty) { Intermediate = entry, Submission = new Submission { Failures = submissionFailure ? 1 : 0 }, Image = new Image { Failures = submissionFailure ? 0 : 1 } };
        await Assert.ThrowsAsync<ArithmeticException>(() => frame.DrainAsync().AsTask());
        Assert.Equal(0, pool.IdleCount);
        Assert.Equal(0, factory.Created[0].Disposals);
        await frame.DrainAsync();
        frame.Dispose();
        Assert.Equal(0, pool.IdleCount);
        Assert.Equal(1, factory.Created[0].Disposals);
    }

    /// <summary>Checks a failed dependent cleanup retains the target for retry and prevents later reuse.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task CleanupFailureDiscardsTheTargetAfterSuccessfulRetryAsync()
    {
        var factory = new Factory();
        using var pool = new UiIntermediateTexturePool(factory.Create);
        UiIntermediateTexturePool.Entry entry = pool.Rent(10, 10, TextureFormat.Rgba8Srgb);
        var frame = new UiHostPresenter.Frame(Guid.Empty) { Intermediate = entry, Submission = new Submission(), Image = new Image() };
        frame.Own(new Cleanup(() => { }) { Failures = 1 });
        await frame.DrainAsync();
        Assert.Throws<ArithmeticException>(frame.Dispose);
        Assert.Equal(0, pool.IdleCount);
        Assert.Equal(0, factory.Created[0].Disposals);
        frame.Dispose();
        Assert.Equal(0, pool.IdleCount);
        Assert.Equal(1, factory.Created[0].Disposals);
    }

    /// <summary>Checks failures before submission and confirmed device retirement both discard targets.</summary>
    /// <param name="retired">Whether the device has retired.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedRecordingAndRetirementDiscardTargets(bool retired)
    {
        var factory = new Factory();
        using var pool = new UiIntermediateTexturePool(factory.Create);
        UiIntermediateTexturePool.Entry entry = pool.Rent(10, 10, TextureFormat.Rgba8Srgb);
        var frame = new UiHostPresenter.Frame(Guid.Empty) { Intermediate = entry };
        if (retired)
        {
            frame.Submission = new Submission();
            frame.Image = new Image();
            pool.StopRenting();
            frame.ConfirmDeviceRetirement();
            Assert.Throws<ObjectDisposedException>(() => pool.Rent(10, 10, TextureFormat.Rgba8Srgb));
        }
        else
        {
            entry.Discard();
        }

        frame.Dispose();
        Assert.Equal(0, pool.IdleCount);
        Assert.Equal(1, factory.Created[0].Disposals);
    }

    /// <summary>Checks dimensions and format form the reuse key and only the two latest idle targets remain.</summary>
    [Fact]
    public void ResizeAndFormatChangesRespectTheTwoTargetLimit()
    {
        var factory = new Factory();
        using var pool = new UiIntermediateTexturePool(factory.Create);
        UiIntermediateTexturePool.Entry first = pool.Rent(10, 10, TextureFormat.Rgba8Srgb);
        first.Release();
        UiIntermediateTexturePool.Entry resized = pool.Rent(20, 10, TextureFormat.Rgba8Srgb);
        resized.Release();
        factory.Format = TextureFormat.Rgba16Float;
        UiIntermediateTexturePool.Entry differentFormat = pool.Rent(20, 10, TextureFormat.Rgba16Float);
        differentFormat.Release();
        Assert.Equal(2, pool.IdleCount);
        Assert.Equal(1, factory.Created[0].Disposals);
        Assert.Same(resized, pool.Rent(20, 10, TextureFormat.Rgba8Srgb));
        resized.Release();
        Assert.Same(differentFormat, pool.Rent(20, 10, TextureFormat.Rgba16Float));
        differentFormat.Release();
        Assert.Equal(3, factory.Created.Count);
    }

    /// <summary>Checks oversized targets are discarded and combined idle texel capacity is bounded.</summary>
    [Fact]
    public void IdleTargetsStayWithinSixtyFourMebibytes()
    {
        var factory = new Factory { Format = TextureFormat.Rgba16Float };
        using var pool = new UiIntermediateTexturePool(factory.Create);
        UiIntermediateTexturePool.Entry oversized = pool.Rent(4096, 4096, factory.Format);
        oversized.Release();
        Assert.Equal(0, pool.IdleCount);
        Assert.Equal(1, factory.Created[0].Disposals);
        UiIntermediateTexturePool.Entry first = pool.Rent(2048, 2048, factory.Format);
        UiIntermediateTexturePool.Entry second = pool.Rent(2048, 2048, factory.Format);
        first.Release();
        second.Release();
        Assert.Equal(64UL * 1024 * 1024, pool.IdleBytes);
        UiIntermediateTexturePool.Entry smaller = pool.Rent(1024, 1024, factory.Format);
        smaller.Release();
        Assert.Equal(2, pool.IdleCount);
        Assert.Equal(40UL * 1024 * 1024, pool.IdleBytes);
        Assert.Equal(1, factory.Created[1].Disposals);
    }

    /// <summary>Checks screenshot intermediates transition from copy-source rather than undefined on reuse.</summary>
    [Fact]
    public void ScreenshotTargetPreservesItsCopySourceBarrier()
    {
        var factory = new Factory();
        using var pool = new UiIntermediateTexturePool(factory.Create);
        UiIntermediateTexturePool.Entry entry = pool.Rent(10, 10, TextureFormat.Rgba8Srgb);
        entry.State = TextureState.CopySource;
        entry.Release();
        UiIntermediateTexturePool.Entry reused = pool.Rent(10, 10, TextureFormat.Rgba8Srgb);
        Assert.Equal(TextureState.CopySource, reused.ColorAttachmentBarrier.BeforeState);
        Assert.Equal(new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead), reused.ColorAttachmentBarrier.Before);
        Assert.Equal(TextureState.ColorAttachment, reused.ColorAttachmentBarrier.AfterState);
        reused.Release();
    }

    /// <summary>Checks failure while creating a view releases its backing texture.</summary>
    [Fact]
    public void FailedViewCreationReleasesTheTexture()
    {
        var texture = new Texture(10, 10, TextureFormat.Rgba8Srgb) { FailViewCreation = true };
        using var pool = new UiIntermediateTexturePool((_, _) => texture);
        Assert.Throws<ArithmeticException>(() => pool.Rent(10, 10, TextureFormat.Rgba8Srgb));
        Assert.Equal(1, texture.Disposals);
        Assert.Equal(0, pool.IdleCount);
    }

    /// <summary>Checks late completion races cannot repopulate or double-release a retired pool.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task ConcurrentFrameReturnsAndRetirementReleaseEveryTargetOnceAsync()
    {
        for (int iteration = 0; iteration < 20; iteration++)
        {
            var factory = new Factory();
            using var pool = new UiIntermediateTexturePool(factory.Create);
            UiHostPresenter.Frame[] frames = Enumerable.Range(0, 8).Select(_ => new UiHostPresenter.Frame(Guid.Empty) { Intermediate = pool.Rent(10, 10, TextureFormat.Rgba8Srgb) }).ToArray();
            await Task.WhenAll(frames.Select(frame => Task.Run(frame.Dispose)).Append(Task.Run(pool.Dispose)));
            Assert.Equal(0, pool.IdleCount);
            Assert.All(factory.Created, texture =>
            {
                Assert.Equal(1, texture.Disposals);
                Assert.Equal(1, texture.View!.Disposals);
            });
        }
    }

    /// <summary>Checks failed idle eviction keeps resources owned for cleanup and forbids returning a broken view.</summary>
    [Fact]
    public void FailedEvictionDisablesReuseAndAllowsCleanupRetry()
    {
        var factory = new Factory();
        using var pool = new UiIntermediateTexturePool(factory.Create);
        pool.Rent(10, 10, TextureFormat.Rgba8Srgb).Release();
        pool.Rent(20, 10, TextureFormat.Rgba8Srgb).Release();
        var frame = new UiHostPresenter.Frame(Guid.Empty) { Intermediate = pool.Rent(30, 10, TextureFormat.Rgba8Srgb) };
        factory.Created[0].View!.Failures = 1;
        Assert.Throws<ArithmeticException>(frame.Dispose);
        Assert.Throws<InvalidOperationException>(() => pool.Rent(10, 10, TextureFormat.Rgba8Srgb));
        Assert.Equal(0, factory.Created[0].Disposals);
        frame.Dispose();
        pool.Dispose();
        Assert.All(factory.Created, texture =>
        {
            Assert.Equal(1, texture.Disposals);
            Assert.Equal(1, texture.View!.Disposals);
        });
    }

    private sealed class Factory
    {
        internal TextureFormat Format { get; set; } = TextureFormat.Rgba8Srgb;

        internal List<Texture> Created { get; } = [];

        internal IGraphicsTexture Create(uint width, uint height)
        {
            var texture = new Texture(width, height, Format);
            Created.Add(texture);
            return texture;
        }
    }

    private sealed class Texture(uint width, uint height, TextureFormat format) : IGraphicsTexture
    {
        public uint Width => width;

        public uint Height => height;

        public uint MipLevels => 1;

        public uint ArrayLayers => 1;

        public TextureFormat Format => format;

        public TextureUsage Usage => TextureUsage.RenderAttachment | TextureUsage.Sampled | TextureUsage.CopySource;

        internal int Disposals { get; private set; }

        internal int Views { get; private set; }

        internal View? View { get; private set; }

        internal bool FailViewCreation { get; init; }

        public void Dispose() => Disposals++;

        public (uint Width, uint Height) GetMipSize(uint mipLevel) => (width, height);

        public IGraphicsTextureView CreateView(TextureViewDesc? desc = null)
        {
            if (FailViewCreation)
            {
                throw new ArithmeticException("view creation");
            }

            Views++;
            return View = new(this);
        }
    }

    private sealed class View(IGraphicsTexture texture) : IGraphicsTextureView
    {
        public IGraphicsTexture Texture => texture;

        public TextureViewInfo Info => new() { Format = texture.Format, Dimension = TextureViewDimension.D2, BaseMipLevel = 0, MipLevelCount = 1, BaseArrayLayer = 0, ArrayLayerCount = 1 };

        internal int Disposals { get; private set; }

        internal int Failures { get; set; }

        public void Dispose()
        {
            if (Failures-- > 0)
            {
                throw new ArithmeticException("view cleanup");
            }

            Disposals++;
        }
    }

    private sealed class Cleanup(Action cleanup) : IDisposable
    {
        internal int Failures { get; set; }

        public void Dispose()
        {
            if (Failures-- > 0)
            {
                throw new ArithmeticException("cleanup");
            }

            cleanup();
        }
    }

    private sealed class Submission : IGraphicsSubmission
    {
        public SubmissionStatus Status => SubmissionStatus.Pending;

        internal TaskCompletionSource? Gate { get; init; }

        internal int Failures { get; set; }

        public void Dispose()
        {
        }

        public ValueTask WaitAsync(CancellationToken cancellationToken = default)
        {
            if (Failures-- > 0)
            {
                throw new ArithmeticException("submission");
            }

            return Gate is { } gate ? new(gate.Task.WaitAsync(cancellationToken)) : ValueTask.CompletedTask;
        }
    }

    private sealed class Image : IGraphicsSurfaceFrame
    {
        public SurfaceFrameStatus Status => SurfaceFrameStatus.Presented;

        public IGraphicsTexture Texture => throw new NotSupportedException();

        internal TaskCompletionSource? Gate { get; init; }

        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int Failures { get; set; }

        public void Dispose()
        {
        }

        public SurfaceStatus Present(IReadOnlyList<IGraphicsSemaphore>? waitSemaphores = null) => SurfaceStatus.Success;

        public ValueTask WaitForReleaseAsync(CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            if (Failures-- > 0)
            {
                throw new ArithmeticException("presentation");
            }

            return Gate is { } gate ? new(gate.Task.WaitAsync(cancellationToken)) : ValueTask.CompletedTask;
        }
    }
}
