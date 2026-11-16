using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Deterministic shape reuse, ownership, key and bounded-storage contracts.</summary>
public sealed class DrawingCpuShapeCacheTests
{
    private const long Budget = 128L * 1024 * 1024;

    /// <summary>Paint-only publication reuses strips while rebinding the current command.</summary>
    [Fact]
    public void PaintChangesReuseShapeAndReturnedArraysCannotPoisonCache()
    {
        var cache = new DrawingCpuShapeCache2D(1024 * 1024, 8);
        FillRectCommand2D red = Rect();
        FillRectCommand2D blue = red with { Paint = PaintGraph2D.Solid(SRgba<float>.Blue) };
        SparseStripPlan2D first = cache.GetSparse(new([red]), Target(), Budget);
        SparseStrip2D[] expected = first.Strips.ToArray();
        first.Strips[0] = default;
        first.Commands[0] = blue;
        SparseStripPlan2D second = cache.GetSparse(new([blue]), Target(), Budget);
        Assert.Equal(1L, cache.SparseBuildCount);
        Assert.Equal(expected, second.Strips);
        Assert.Same(blue, Assert.Single(second.Commands));
        Assert.NotSame(first.Strips, second.Strips);
        second.Strips[0] = default;
        Assert.Equal(expected, cache.GetSparse(new([red]), Target(), Budget).Strips);
        Assert.Equal(1, cache.AssetCount);
    }

    /// <summary>Mask owners are detached on cold and warm calls, not only command tables.</summary>
    [Fact]
    public void BoundaryMasksArePrivatelyOwned()
    {
        var cache = new DrawingCpuShapeCache2D(1024 * 1024, 8);
        FillRectCommand2D command = Rect() with { Rect = new(0.125f, 0.125f, 0.5f, 0.5f) };
        var list = new DrawList2D([command]);
        SparseStripPlan2D first = cache.GetSparse(list, Target(), Budget);
        Assert.Equal((ushort)0x0033, Assert.Single(first.BoundaryMasks));
        first.BoundaryMasks[0] = 0;
        SparseStripPlan2D next = cache.GetSparse(list, Target(), Budget);
        Assert.Equal((ushort)0x0033, Assert.Single(next.BoundaryMasks));
        next.BoundaryMasks[0] = 0;
        Assert.Equal((ushort)0x0033, Assert.Single(cache.GetSparse(list, Target(), Budget).BoundaryMasks));
        Assert.Equal(1L, cache.SparseBuildCount);
    }

    /// <summary>Shape, ordering, transform and the exact target grid participate in sparse keys.</summary>
    [Fact]
    public void SparseKeysIncludeGeometryTransformOrderExtentOriginAndScale()
    {
        var cache = new DrawingCpuShapeCache2D(1024 * 1024, 16);
        FillRectCommand2D first = Rect();
        FillRectCommand2D other = first with { Rect = new(2, 2, 3, 3) };
        var list = new DrawList2D([first, other]);
        cache.GetSparse(list, Target(), Budget);
        cache.GetSparse(new([other, first]), Target(), Budget);
        cache.GetSparse(new([first with { State = first.State with { Transform = Matrix3x2.CreateTranslation(1, 0) } }, other]), Target(), Budget);
        cache.GetSparse(list, Target() with { PixelOrigin = new(1, 0) }, Budget);
        cache.GetSparse(list, Target() with { PixelScale = 2 }, Budget);
        cache.GetSparse(list, Target() with { Description = Target().Description with { Width = 16 } }, Budget);
        Assert.Equal(6L, cache.SparseBuildCount);
        cache.GetSparse(list, Target(), Budget);
        Assert.Equal(6L, cache.SparseBuildCount);
    }

    /// <summary>Raw immutable geometry is shared only for identical path identity and shape state.</summary>
    [Fact]
    public void GeometryKeysIncludePathIdentityTransformRuleAndStroke()
    {
        var cache = new DrawingCpuShapeCache2D(1024 * 1024, 16);
        Path2D path = Path();
        ComputeGeometryPlan2D first = cache.GetGeometry(path, Matrix3x2.Identity, FillRule2D.NonZero, null, Budget);
        Assert.Same(first, cache.GetGeometry(path, Matrix3x2.Identity, FillRule2D.NonZero, null, Budget));
        Assert.NotSame(first, cache.GetGeometry(Path(), Matrix3x2.Identity, FillRule2D.NonZero, null, Budget));
        Assert.NotSame(first, cache.GetGeometry(path, Matrix3x2.CreateScale(2), FillRule2D.NonZero, null, Budget));
        Assert.NotSame(first, cache.GetGeometry(path, Matrix3x2.Identity, FillRule2D.EvenOdd, null, Budget));
        var stroke = new StrokeStyle2D { Width = 2 };
        ComputeGeometryPlan2D stroked = cache.GetGeometry(path, Matrix3x2.Identity, FillRule2D.NonZero, stroke, Budget);
        Assert.Same(stroked, cache.GetGeometry(path, Matrix3x2.Identity, FillRule2D.NonZero, stroke with { }, Budget));
        Assert.NotSame(stroked, cache.GetGeometry(path, Matrix3x2.Identity, FillRule2D.NonZero, stroke with { Width = 3 }, Budget));
        Assert.Equal(6L, cache.GeometryBuildCount);
    }

    /// <summary>Least-recent idle assets are evicted without invalidating borrowed immutable plans.</summary>
    [Fact]
    public void AssetBoundEvictsLeastRecentlyUsedAndClearReleasesAccounting()
    {
        var cache = new DrawingCpuShapeCache2D(1024 * 1024, 2);
        Path2D a = Path(), b = Path(), c = Path();
        ComputeGeometryPlan2D first = cache.GetGeometry(a, Matrix3x2.Identity, FillRule2D.NonZero, null, Budget);
        ComputeGeometryPlan2D second = cache.GetGeometry(b, Matrix3x2.Identity, FillRule2D.NonZero, null, Budget);
        Assert.Same(first, cache.GetGeometry(a, Matrix3x2.Identity, FillRule2D.NonZero, null, Budget));
        cache.GetGeometry(c, Matrix3x2.Identity, FillRule2D.NonZero, null, Budget);
        Assert.Equal(2, cache.AssetCount);
        Assert.NotSame(second, cache.GetGeometry(b, Matrix3x2.Identity, FillRule2D.NonZero, null, Budget));
        Assert.NotEmpty(first.Curves);
        Assert.InRange(cache.EstimatedBytes, 1L, 1024 * 1024L);
        cache.Clear();
        Assert.Equal(0L, cache.EstimatedBytes);
        Assert.Equal(0, cache.AssetCount);
    }

    /// <summary>Byte admission evicts even when the asset-count limit has spare capacity.</summary>
    [Fact]
    public void ByteLimitBoundsMixedAssetsAndRetainedPagesRebindPaint()
    {
        var probe = new DrawingCpuShapeCache2D(1024 * 1024, 8);
        Path2D a = Path(), b = Path();
        probe.GetGeometry(a, Matrix3x2.Identity, FillRule2D.NonZero, null, Budget);
        long oneAsset = probe.EstimatedBytes;
        var cache = new DrawingCpuShapeCache2D(oneAsset, 8);
        ComputeGeometryPlan2D first = cache.GetGeometry(a, Matrix3x2.Identity, FillRule2D.NonZero, null, Budget);
        cache.GetGeometry(b, Matrix3x2.Identity, FillRule2D.NonZero, null, Budget);
        Assert.Equal(1, cache.AssetCount);
        Assert.InRange(cache.EstimatedBytes, 1L, oneAsset);
        Assert.NotSame(first, cache.GetGeometry(a, Matrix3x2.Identity, FillRule2D.NonZero, null, Budget));

        FillRectCommand2D red = Rect();
        FillRectCommand2D blue = red with { Paint = PaintGraph2D.Solid(SRgba<float>.Blue) };
        var list = new DrawList2D([red]);
        DrawList2D changed = list.WithPage(0, new DrawList2D([blue]));
        probe.GetSparse(list, Target(), Budget);
        SparseStripPlan2D rebound = probe.GetSparse(changed, Target(), Budget);
        Assert.Same(blue, Assert.Single(rebound.Commands));
        Assert.Equal(1L, probe.SparseBuildCount);
    }

    /// <summary>Zero/undersized idle budgets disable retention, not the requested execution path.</summary>
    /// <param name="bytes">The idle byte bound.</param>
    /// <param name="assets">The idle asset bound.</param>
    [Theory]
    [InlineData(0, 8)]
    [InlineData(32, 8)]
    [InlineData(1024, 0)]
    public void DisabledOrUndersizedCacheDoesNotRetain(long bytes, int assets)
    {
        var cache = new DrawingCpuShapeCache2D(bytes, assets);
        var list = new DrawList2D([Rect()]);
        cache.GetSparse(list, Target(), Budget);
        cache.GetSparse(list, Target(), Budget);
        cache.GetGeometry(Path(), Matrix3x2.Identity, FillRule2D.NonZero, null, Budget);
        Assert.Equal(0, cache.AssetCount);
        Assert.Equal(0L, cache.EstimatedBytes);
        Assert.Equal(2L, cache.SparseBuildCount);
    }

    /// <summary>Warm hits enforce current budgets and cancellation just like cold preparations.</summary>
    [Fact]
    public void WarmCallsCannotBypassWorkingLimitsOrCancellation()
    {
        var cache = new DrawingCpuShapeCache2D(1024 * 1024, 8);
        var list = new DrawList2D([Rect()]);
        SparseStripPlan2D sparse = cache.GetSparse(list, Target(), Budget);
        Assert.Throws<NotSupportedException>(() => cache.GetSparse(list, Target(), sparse.WorkingBytes - 1));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => cache.GetSparse(list, Target(), Budget, cancellation.Token));
        Path2D path = Path();
        ComputeGeometryPlan2D geometry = cache.GetGeometry(path, Matrix3x2.Identity, FillRule2D.NonZero, null, Budget);
        Assert.Throws<NotSupportedException>(() => cache.GetGeometry(path, Matrix3x2.Identity, FillRule2D.NonZero, null, geometry.Capacity.WorkingBytes - 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DrawingCpuShapeCache2D(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DrawingCpuShapeCache2D(1, -1));
    }

    /// <summary>A shared immutable curve owner is not charged again solely because it is cached.</summary>
    [Fact]
    public void GeometryHitAcceptsItsExactCapacityWithoutDuplicateOwnerCharge()
    {
        var cache = new DrawingCpuShapeCache2D(1024 * 1024, 8);
        Path2D path = Path();
        ComputeGeometryPlan2D first = cache.GetGeometry(path, Matrix3x2.Identity, FillRule2D.NonZero, null, Budget);
        Assert.Same(first, cache.GetGeometry(path, Matrix3x2.Identity, FillRule2D.NonZero, null, first.Capacity.WorkingBytes));
        Assert.Equal(1L, cache.GeometryBuildCount);
    }

    private static FillRectCommand2D Rect() => new(new(1, 1, 4, 4), PaintGraph2D.Solid(SRgba<float>.Red), new(Matrix3x2.Identity, ClipStack2D.Empty));

    private static Path2D Path()
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(new(1, 1));
        builder.LineTo(new(4, 1));
        builder.LineTo(new(4, 4));
        builder.Close();
        return builder.Build();
    }

    private static DrawingTargetInfo2D Target() => new()
    {
        Description = new() { Width = 12, Height = 12, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment },
    };
}
