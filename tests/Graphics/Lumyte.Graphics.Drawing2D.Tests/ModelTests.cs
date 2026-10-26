using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Verifies immutable recording, transactional validation, and public image contracts.</summary>
public sealed class ModelTests
{
    /// <summary>Path snapshots survive subsequent builder edits and expose no mutable arrays.</summary>
    [Fact]
    public void PathBuilderCopiesSegmentsAndRejectsMissingMove()
    {
        var builder = new PathBuilder2D();
        Assert.Throws<InvalidOperationException>(() => builder.LineTo(Vector2.One));
        Assert.Throws<InvalidOperationException>(builder.Close);
        builder.MoveTo(Vector2.Zero);
        builder.LineTo(Vector2.One);
        Path2D first = builder.Build();
        builder.Close();
        Assert.Equal(2, first.Segments.Count);
        Assert.Equal(3, builder.Build().Segments.Count);
        Assert.Throws<NotSupportedException>(() => ((IList<PathSegment2D>)first.Segments)[0] = default);
    }

    /// <summary>Convenience gradients normalize without discarding duplicate stops or their order.</summary>
    [Fact]
    public void GradientFactoriesCopyStopsAndNormalizeGeometry()
    {
        ColorStop2D[] stops = [new(-1, new Color2D(1, 0, 0, 1)), new(-1, new Color2D(0, 1, 0, 1)), new(2, new Color2D(0, 0, 1, 1))];
        var paint = PaintGraph2D.LinearGradient(new Vector2(2, 3), new Vector2(5, 7), stops);
        LinearPaintNode2D linear = Assert.IsType<LinearPaintNode2D>(paint.Root);
        stops[0] = default;
        Assert.Equal(new Vector2(-2, 6), linear.P2);
        Assert.Equal(-1, linear.Stops[0].Offset);
        Assert.Equal(new Color2D(0, 1, 0, 1), linear.Stops[1].Color);
        RadialPaintNode2D radial = Assert.IsType<RadialPaintNode2D>(PaintGraph2D.RadialGradient(Vector2.One, 3, linear.Stops.ToArray()).Root);
        Assert.Equal(radial.C0, radial.C1);
        Assert.Equal(0, radial.R0);
        Assert.Equal(3, radial.R1);
        Assert.Throws<ArgumentException>(() => PaintGraph2D.LinearGradient(Vector2.Zero, Vector2.Zero, stops));
        Assert.Throws<ArgumentException>(() => PaintGraph2D.LinearGradient(Vector2.Zero, Vector2.One, [new(1, default), new(0, default)]));
    }

    /// <summary>Invalid drawing inputs leave state and command order intact.</summary>
    [Fact]
    public void CanvasValidationIsTransactionalAndRecordingsAreIndependent()
    {
        var canvas = new Canvas2D();
        Assert.Throws<InvalidOperationException>(() => canvas.FillRect(default, default(Color2D)));
        canvas.Begin();
        canvas.Translate(new Vector2(10, 20));
        canvas.Scale(new Vector2(2, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.Translate(new Vector2(float.NaN, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.FillRect(new Rect2D(0, 0, -1, 4), default(Color2D)));
        canvas.FillRect(new Rect2D(1, 2, 3, 4), new Color2D(1, 0, 0, 1));
        DrawList2D first = canvas.End();
        FillRectCommand2D command = Assert.IsType<FillRectCommand2D>(Assert.Single(first.Commands));
        Assert.Equal(new Vector2(12, 26), Vector2.Transform(new Vector2(1, 2), command.State.Transform));
        Assert.IsType<SolidPaintNode2D>(command.Paint.Root);
        canvas.Begin();
        Assert.Empty(canvas.End().Commands);
        Assert.Single(first.Commands);
    }

    /// <summary>Clip transforms are fixed at the clip call and later transforms do not mutate snapshots.</summary>
    [Fact]
    public void CanvasClipsCaptureTheirOwnTransforms()
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.Translate(new Vector2(5, 6));
        canvas.ClipRect(new Rect2D(0.25f, 0.5f, 10, 20));
        canvas.Translate(new Vector2(30, 40));
        canvas.FillCircle(Vector2.Zero, 3, default(Color2D));
        DrawingCommand2D command = Assert.Single(canvas.End().Commands);
        ClipElement2D clip = Assert.Single(command.State.Clip.Elements);
        Assert.Equal(new Vector2(5, 6), clip.Transform.Translation);
        Assert.Equal(new Vector2(35, 46), command.State.Transform.Translation);
        Assert.Equal(new Vector2(0.25f, 0.5f), clip.Path.Segments[0].P0);
    }

    /// <summary>Typed command memory preserves painter order without copying across preparation calls.</summary>
    [Fact]
    public void DrawListRetainsTypedStorageAndRejectsMixedRuns()
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new Rect2D(1, 0, 1, 1), default(Color2D));
        canvas.FillRect(new Rect2D(2, 0, 1, 1), default(Color2D));
        canvas.FillCircle(Vector2.Zero, 2, default(Color2D));
        canvas.FillRect(new Rect2D(3, 0, 1, 1), default(Color2D));
        DrawList2D draws = canvas.End();
        ReadOnlyMemory<FillRectCommand2D> first = draws.GetCommands<FillRectCommand2D>(0, 2);
        Assert.True(first.Equals(draws.GetCommands<FillRectCommand2D>(0, 2)));
        Assert.Equal(1, first.Span[0].Rect.X);
        Assert.Equal(2, first.Span[1].Rect.X);
        Assert.Equal(3, draws.GetCommands<FillRectCommand2D>(3, 1).Span[0].Rect.X);
        Assert.Throws<ArgumentException>(() => draws.GetCommands<FillRectCommand2D>(0, 4));
    }

    /// <summary>Image patterns validate coordinates in the selected mip and reject singular or comparison sampling.</summary>
    [Fact]
    public void ImageDescriptorsRejectInvalidSourceAndSamplingContracts()
    {
        using var texture = new ModelTexture();
        using IGraphicsTextureView view = texture.CreateView(new TextureViewDesc { BaseMipLevel = 1 });
        var pattern = new ImagePattern2D { View = view, Source = new Rect2D(1, 2, 4, 5) };
        ImagePaintNode2D node = Assert.IsType<ImagePaintNode2D>(PaintGraph2D.ImagePattern(pattern).Root);
        Assert.NotSame(pattern, node.Pattern);
        Assert.Same(view, node.Pattern.View);
        Assert.Throws<ArgumentOutOfRangeException>(() => PaintGraph2D.ImagePattern(pattern with { Source = new Rect2D(7, 0, 2, 1) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => PaintGraph2D.ImagePattern(pattern with { Source = new Rect2D(0.5f, 0, 1, 1) }));
        Assert.Throws<ArgumentException>(() => PaintGraph2D.ImagePattern(pattern with { PatternToLocal = default }));
        using var compare = new ModelSampler(new SamplerDesc { Compare = CompareFunction.Less });
        using var sampler = new ModelSampler(new SamplerDesc());
    }

    private sealed class ModelTexture : IGraphicsTexture
    {
        public uint Width => 16;

        public uint Height => 16;

        public uint MipLevels => 2;

        public uint ArrayLayers => 1;

        public TextureFormat Format => TextureFormat.Rgba8Unorm;

        public TextureUsage Usage => TextureUsage.Sampled;

        public (uint Width, uint Height) GetMipSize(uint mipLevel) => (Width >> (int)mipLevel, Height >> (int)mipLevel);

        public IGraphicsTextureView CreateView(TextureViewDesc? desc = null) => new ModelView(this, desc?.BaseMipLevel ?? 0);

        public void Dispose()
        {
        }
    }

    private sealed class ModelView(IGraphicsTexture texture, uint mip) : IGraphicsTextureView
    {
        public IGraphicsTexture Texture { get; } = texture;

        public TextureViewInfo Info { get; } = new()
        {
            Format = texture.Format,
            Dimension = TextureViewDimension.D2,
            BaseMipLevel = mip,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
        };

        public void Dispose()
        {
        }
    }

    private sealed class ModelSampler(SamplerDesc desc) : IGraphicsSampler
    {
        public SamplerDesc Desc { get; } = desc;

        public void Dispose()
        {
        }
    }
}
