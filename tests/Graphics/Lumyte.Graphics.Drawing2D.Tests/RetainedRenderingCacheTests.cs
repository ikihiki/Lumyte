using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks frame-owned returns and retryable cleanup of bounded compiled assets.</summary>
public sealed class RetainedRenderingCacheTests
{
    /// <summary>Checks abandoned uploads are not published and completed assets are reused.</summary>
    [Fact]
    public void OnlyCompletedUploadsBecomeResident()
    {
        using var cache = new RetainedDrawingCache2D(new());
        RetainedDrawingCache2D.Key key = Key();
        var cold = new Batch();
        RetainedDrawingCache2D.FrameLease abandoned = cache.AcquireFrame();
        abandoned.Assets.Add(new(key, cold));
        cache.ReleaseFrame(abandoned, false);
        Assert.Equal(1, cold.Disposals);
        Assert.Null(cache.Take(key));
        var warm = new Batch();
        var asset = new RetainedDrawingCache2D.Asset(key, warm);
        RetainedDrawingCache2D.FrameLease first = cache.AcquireFrame();
        first.Assets.Add(asset);
        cache.ReleaseFrame(first, true);
        RetainedDrawingCache2D.FrameLease second = cache.AcquireFrame();
        Assert.Same(asset, cache.Take(key));
        second.Assets.Add(asset);
        Assert.True(asset.Resident);
        cache.ReleaseFrame(second, false);
        RetainedDrawingCache2D.FrameLease third = cache.AcquireFrame();
        Assert.Same(asset, cache.Take(key));
        third.Assets.Add(asset);
        cache.ReleaseFrame(third, true);
    }

    /// <summary>Checks idle budgets evict only returned assets, never an active frame.</summary>
    [Fact]
    public void ZeroBudgetEvictsOnlyAfterReturn()
    {
        using var cache = new RetainedDrawingCache2D(new() { MaxCachedAssets = 0 });
        var batch = new Batch();
        RetainedDrawingCache2D.FrameLease frame = cache.AcquireFrame();
        frame.Assets.Add(new(Key(), batch));
        Assert.Equal(0, batch.Disposals);
        cache.Dispose();
        Assert.Equal(0, batch.Disposals);
        cache.ReleaseFrame(frame, true);
        Assert.Equal(1, batch.Disposals);
    }

    /// <summary>Checks order-independent keys include exact target and property values.</summary>
    [Fact]
    public void TargetAndPropertyChangesInvalidateOnlyTheirAssets()
    {
        RetainedDrawingCache2D.Key key = Key();
        Assert.True(key.Matches(Key()));
        Assert.False(key.Matches(key with { Width = 2048 }));
        Assert.False(key.Matches(key with { Format = TextureFormat.Rgba16Float }));
        Assert.False(key.Matches(key with { PixelScale = 2 }));
        Assert.False(key.Matches(key with { Transform = Matrix3x2.CreateTranslation(1, 0) }));
    }

    /// <summary>Checks failed eviction cannot reissue a partially disposed asset and can be retried.</summary>
    [Fact]
    public void FailedEvictionQuarantinesTheAssetUntilCleanupSucceeds()
    {
        using var cache = new RetainedDrawingCache2D(new() { MaxCachedAssets = 0 });
        var batch = new Batch { FailuresRemaining = 1 };
        RetainedDrawingCache2D.FrameLease frame = cache.AcquireFrame();
        frame.Assets.Add(new(Key(), batch));
        Assert.Throws<InvalidOperationException>(() => cache.ReleaseFrame(frame, true));
        Assert.Null(cache.Take(Key()));
        Assert.Equal(0, cache.Statistics.CachedAssets);
        Assert.Equal(0, cache.Statistics.ActiveFrames);
        cache.ReleaseFrame(frame, true);
        Assert.Equal(0, cache.Statistics.ActiveFrames);
        Assert.Equal(2, batch.Disposals);
    }

    /// <summary>Checks a failed old return cannot publish an asset owned by a newer frame.</summary>
    [Fact]
    public void OldReturnRetryCannotReturnAnAssetBorrowedAgain()
    {
        using var cache = new RetainedDrawingCache2D(new() { MaxCachedAssets = 1 });
        var a = new Batch { FailuresRemaining = 1 };
        var b = new Batch();
        var assetB = new RetainedDrawingCache2D.Asset(Key() with { Width = 128 }, b);
        RetainedDrawingCache2D.FrameLease first = cache.AcquireFrame();
        first.Assets.Add(new(Key(), a));
        first.Assets.Add(assetB);
        Assert.Throws<InvalidOperationException>(() => cache.ReleaseFrame(first, true));
        Assert.Equal(1, cache.Statistics.CachedAssets);
        RetainedDrawingCache2D.FrameLease second = cache.AcquireFrame();
        Assert.Same(assetB, cache.Take(assetB.Key));
        second.Assets.Add(assetB);
        cache.ReleaseFrame(first, true);
        Assert.Equal(1, cache.Statistics.ActiveFrames);
        Assert.Null(cache.Take(assetB.Key));
        RetainedDrawingCache2D.FrameLease third = cache.AcquireFrame();
        Assert.Null(cache.Take(assetB.Key));
        cache.ReleaseFrame(third, false);
        Assert.Equal(0, b.Disposals);
        Assert.Equal(1, cache.Statistics.ActiveFrames);
        cache.ReleaseFrame(second, true);
        Assert.Equal(0, cache.Statistics.ActiveFrames);
        Assert.Equal(1, cache.Statistics.CachedAssets);
    }

    /// <summary>Checks failed abandoned cleanup never consumes inaccessible frame slots.</summary>
    [Fact]
    public void AbandonedCleanupFailuresReturnSlotsAndRemainOwned()
    {
        using var cache = new RetainedDrawingCache2D(new() { MaxFramesInFlight = 1 });
        var batches = new List<Batch>();
        for (int index = 0; index < 3; index++)
        {
            RetainedDrawingCache2D.FrameLease frame = cache.AcquireFrame();
            var batch = new Batch { FailuresRemaining = 1 };
            batches.Add(batch);
            frame.Assets.Add(new(Key(), batch));
            Assert.Throws<InvalidOperationException>(() => cache.ReleaseFrame(frame, false));
            Assert.Equal(0, cache.Statistics.ActiveFrames);
            Assert.Null(cache.Take(Key()));
        }

        RetainedDrawingCache2D.FrameLease next = cache.AcquireFrame();
        cache.ReleaseFrame(next, false);
        Assert.All(batches, batch => Assert.Equal(2, batch.Disposals));
        Assert.Equal(0, cache.Statistics.ActiveFrames);
    }

    /// <summary>Checks cleanup visits every failed resource and retries without caller-owned frames.</summary>
    [Fact]
    public void AbandonedCleanupAttemptsEveryAssetBeforeThrowing()
    {
        var cache = new RetainedDrawingCache2D(new());
        var first = new Batch { FailuresRemaining = 1 };
        var second = new Batch { FailuresRemaining = 1 };
        RetainedDrawingCache2D.FrameLease frame = cache.AcquireFrame();
        frame.Assets.Add(new(Key(), first));
        frame.Assets.Add(new(Key(), second));
        AggregateException failure = Assert.Throws<AggregateException>(() => cache.ReleaseFrame(frame, false));
        Assert.Equal(2, failure.InnerExceptions.Count);
        Assert.Equal(1, first.Disposals);
        Assert.Equal(1, second.Disposals);
        Assert.Equal(0, cache.Statistics.ActiveFrames);
        cache.Dispose();
        cache.Dispose();
        Assert.Equal(2, first.Disposals);
        Assert.Equal(2, second.Disposals);
    }

    /// <summary>Checks a batch run retains each failed cleanup and does not stop at the first throw.</summary>
    [Fact]
    public void PartialBatchRunCleanupRetriesOnlyFailedBatches()
    {
        var first = new Batch { FailuresRemaining = 1 };
        var second = new Batch();
        var third = new Batch { FailuresRemaining = 1 };
        var run = new RetainedBatchRun2D([first, second, third], false);
        Assert.Throws<AggregateException>(() => run.Dispose());
        Assert.Equal(1, first.Disposals);
        Assert.Equal(1, second.Disposals);
        Assert.Equal(1, third.Disposals);
        run.Dispose();
        run.Dispose();
        Assert.Equal(2, first.Disposals);
        Assert.Equal(1, second.Disposals);
        Assert.Equal(2, third.Disposals);
    }

    private static RetainedDrawingCache2D.Key Key() => new([], Matrix3x2.Identity, [], 64, 64, TextureFormat.Rgba8Srgb, 1);

    private sealed class Batch : IRetainedCommandBatch2D
    {
        public long EstimatedBytes => 16;

        internal int Disposals { get; private set; }

        internal int FailuresRemaining { get; set; }

        public void DeclareResources(IDrawingResourceDeclarations2D resources)
        {
        }

        public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target)
        {
        }

        public void RecordRetained(IGraphicsCommandBuffer commands, IGraphicsTextureView target, bool resident)
        {
        }

        public void Dispose()
        {
            Disposals++;
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new InvalidOperationException("Injected resource disposal failure.");
            }
        }
    }
}
