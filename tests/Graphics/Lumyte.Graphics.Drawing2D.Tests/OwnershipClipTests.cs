using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks device-center ownership separately from ordinary antialiased clipping.</summary>
public sealed class OwnershipClipTests
{
    /// <summary>Explicit endpoints preserve a shared half-pixel edge without origin plus rounded width.</summary>
    [Fact]
    public void ExplicitEndpointsPartitionFractionalTileEdge()
    {
        DrawingCommand2D left = Record(c => c.ClipOwnershipRect(new Vector2(-100000000f, 0), new Vector2(5.5f, 20)));
        DrawingCommand2D right = Record(c => c.ClipOwnershipRect(new Vector2(5.5f, 0), new Vector2(932.0881f, 20)));
        Assert.Equal(0, Contains(Normalize(left, Target()).Bounds, 5));
        Assert.Equal(1, Contains(Normalize(right, Target()).Bounds, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Record(c => c.ClipOwnershipRect(new Vector2(1, 0), Vector2.Zero)));
    }

    /// <summary>Adjacent owners partition every pixel at fractional DPR and reflected/translated coordinates.</summary>
    /// <param name="scale">The target device scale.</param>
    /// <param name="reflect">Reflects the captured geometry.</param>
    [Theory]
    [InlineData(1f, false)]
    [InlineData(1.5f, false)]
    [InlineData(2f, false)]
    [InlineData(1f, true)]
    [InlineData(1.5f, true)]
    [InlineData(2f, true)]
    public void AdjacentOwnersPartitionDeviceCenters(float scale, bool reflect)
    {
        Matrix3x2 transform = Matrix3x2.CreateScale(reflect ? -1 : 1, 1) * Matrix3x2.CreateTranslation(reflect ? 12.25f : 0.25f, 0);
        DrawingTargetInfo2D target = Target() with { PixelScale = scale, PixelOrigin = new(0.25f, 0) };
        NormalizedClip2D a = Normalize(
            Record(c =>
            {
                c.ConcatTransform(transform);
                c.ClipOwnershipRect(new(0, 0, 5.5f, 20));
            }),
            target);
        NormalizedClip2D b = Normalize(
            Record(c =>
            {
                c.ConcatTransform(transform);
                c.ClipOwnershipRect(new(5.5f, 0, 6.5f, 20));
            }),
            target);
        Assert.Empty(a.Mask);
        Assert.Empty(b.Mask);
        for (int x = 0; x < 32; x++)
        {
            // Reflection uses a device-space [minimum,maximum) convention, so boundary ownership flips.
            double lo = (0.25 * scale) - target.PixelOrigin.X;
            double hi = (12.25 * scale) - target.PixelOrigin.X;
            Assert.Equal(x + 0.5 >= lo && x + 0.5 < hi ? 1 : 0, Contains(a.Bounds, x) + Contains(b.Bounds, x));
        }
    }

    /// <summary>Equivalent path geometry must not alias hard ownership and AA cache entries.</summary>
    [Fact]
    public void CacheAndOuterAaKeepDistinctSemantics()
    {
        DrawingCommand2D command = Record(
            c => c.ClipOwnershipRect(new(0, 0, 5.5f, 20)));
        ClipElement2D hard = Assert.Single(command.State.Clip.Elements);
        var cache = new ClipNormalizationCache2D(Target());
        NormalizedClip2D aa = cache.Get(new object(), Matrix3x2.Identity, [hard with { IsOwnership = false }]);
        NormalizedClip2D owner = cache.Get(new object(), Matrix3x2.Identity, [hard]);
        Assert.Equal(NormalizedClipKind2D.Analytic, aa.Kind);
        Assert.Equal(NormalizedClipKind2D.Scissor, owner.Kind);
        Assert.Equal(new ScissorRect(0, 0, 5, 20), owner.Bounds);
        Assert.Same(owner, cache.Get(new object(), Matrix3x2.Identity, [hard with { }]));
        NormalizedClip2D combined = Normalize(
            Record(c =>
            {
                c.ClipRect(new(0.25f, 0, 20, 20));
                c.ClipOwnershipRect(new(0, 0, 5.5f, 20));
            }),
            Target());
        Assert.Equal(NormalizedClipKind2D.Analytic, combined.Kind);
        Assert.Equal(new Vector4(1, 0, 20, 80), combined.Samples);
    }

    /// <summary>Replay preserves metadata and rejects unsupported accumulated rotation/shear explicitly.</summary>
    [Fact]
    public void ReplayPreservesOwnershipAndRejectsRotation()
    {
        var child = new Canvas2D();
        child.Begin();
        child.ClipOwnershipRect(new(0, 0, 5.5f, 20));
        child.FillRect(new(0, 0, 20, 20), new SRgba<float>(0, 0, 1, 0.5f));
        DrawList2D list = child.End();
        DrawingCommand2D command = Record(
            c =>
            {
                c.Translate(new(2, 0));
                c.Replay(list);
            },
            fill: false);
        Assert.True(Assert.Single(command.State.Clip.Elements).IsOwnership);
        Assert.Equal(new ScissorRect(2, 0, 5, 20), Normalize(command, Target()).Bounds);
        foreach (Matrix3x2 transform in new[] { Matrix3x2.CreateRotation(0.2f), new Matrix3x2(1, 0, 0.2f, 1, 0, 0) })
        {
            command = Record(
                c =>
                {
                    c.ConcatTransform(transform);
                    c.Replay(list);
                },
                fill: false);
            Assert.Throws<NotSupportedException>(() => Normalize(command, Target()));
        }
    }

    /// <summary>Empty extents and singular captured transforms own no pixels.</summary>
    [Fact]
    public void EmptyOwnershipIsEmpty()
    {
        Assert.Equal(NormalizedClipKind2D.Empty, Normalize(Record(c => c.ClipOwnershipRect(new(0, 0, 0, 20))), Target()).Kind);
        DrawingCommand2D singular = Record(c =>
        {
            c.Scale(new(0, 1));
            c.ClipOwnershipRect(new(0, 0, 20, 20));
        });
        Assert.Equal(NormalizedClipKind2D.Empty, Normalize(singular, Target()).Kind);
    }

    /// <summary>Retained page rebasing keeps the ownership predicate and rejects unsupported scene transforms.</summary>
    /// <returns>The asynchronous preparation checks.</returns>
    [Fact]
    public async Task RetainedPagesPreserveOwnershipAsync()
    {
        DrawingCommand2D command = Record(c => c.ClipOwnershipRect(new(0, 0, 5.5f, 20)));
        var list = new DrawList2D([command]);
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var texture = new DispatchTexture { Width = 32, Height = 32 };
        using IGraphicsTextureView view = texture.CreateView();
        using var snapshot = new DrawingSnapshot2D(1, [new(default, list, Matrix3x2.CreateTranslation(2, 0), [], 1, 1)]);
        using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(snapshot, new() { View = view, PixelScale = 1.5f });
        Assert.NotEmpty(device.Records);

        // Fractional ownership has no residual analytic/mask clip payload after rebasing.
        Assert.All(device.Records, record => Assert.Equal(0u, record.ClipId));
        frame.Dispose();
        using var rotated = new DrawingSnapshot2D(2, [new(default, list, Matrix3x2.CreateRotation(0.2f), [], 1, 1)]);
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            using PreparedDrawingFrame2D rejected = await renderer.PrepareAsync(rotated, new() { View = view });
        });
    }

    private static int Contains(ScissorRect bounds, int x) => x >= bounds.X && x < bounds.X + bounds.Width ? 1 : 0;

    private static NormalizedClip2D Normalize(DrawingCommand2D command, DrawingTargetInfo2D target) => NormalizedClip2D.Create(command.State.Clip.Elements, target);

    private static DrawingTargetInfo2D Target() => new() { Description = new() { Width = 32, Height = 32, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment } };

    private static DrawingCommand2D Record(Action<Canvas2D> action, bool fill = true)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        action(canvas);
        if (fill)
        {
            canvas.FillRect(new(0, 0, 32, 32), new SRgba<float>(1, 0, 0, 1));
        }

        return Assert.Single(canvas.End().Commands);
    }
}
