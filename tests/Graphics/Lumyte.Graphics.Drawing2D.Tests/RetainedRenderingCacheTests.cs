using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks immutable compiled-asset publication and bounded idle eviction.</summary>
public sealed class RetainedRenderingCacheTests
{
    /// <summary>Checks abandoned uploads are not published and completed assets are reused.</summary>
    [Fact]
    public void OnlyCompletedUploadsBecomeResident()
    {
        using var cache = new RetainedDrawingCache2D(new());
        RetainedDrawingCache2D.Key key = Key();
        var cold = new Batch();
        cache.AcquireFrame();
        cache.ReleaseFrame([new(key, cold)], false);
        Assert.Equal(1, cold.Disposals);
        Assert.Null(cache.Take(key));
        var warm = new Batch();
        cache.AcquireFrame();
        var asset = new RetainedDrawingCache2D.Asset(key, warm);
        cache.ReleaseFrame([asset], true);
        Assert.Same(asset, cache.Take(key));
        Assert.True(asset.Resident);
        cache.AcquireFrame();
        cache.ReleaseFrame([asset], false);
        Assert.Same(asset, cache.Take(key));
        cache.AcquireFrame();
        cache.ReleaseFrame([asset], true);
    }

    /// <summary>Checks idle budgets evict only returned assets, never an active frame.</summary>
    [Fact]
    public void ZeroBudgetEvictsOnlyAfterReturn()
    {
        using var cache = new RetainedDrawingCache2D(new() { MaxCachedAssets = 0 });
        var batch = new Batch();
        cache.AcquireFrame();
        Assert.Equal(0, batch.Disposals);
        cache.Dispose();
        Assert.Equal(0, batch.Disposals);
        cache.ReleaseFrame([new(Key(), batch)], true);
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

    private static RetainedDrawingCache2D.Key Key() => new([], Matrix3x2.Identity, [], 64, 64, TextureFormat.Rgba8Srgb, 1);

    private sealed class Batch : IRetainedCommandBatch2D
    {
        public long EstimatedBytes => 16;

        internal int Disposals { get; private set; }

        public void DeclareResources(IDrawingResourceDeclarations2D resources)
        {
        }

        public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target)
        {
        }

        public void RecordRetained(IGraphicsCommandBuffer commands, IGraphicsTextureView target, bool resident)
        {
        }

        public void Dispose() => Disposals++;
    }
}
