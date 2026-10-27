using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks the linear-data and transform contracts for distance-field masks.</summary>
public sealed class DistanceFieldTests
{
    /// <summary>Distance fields snapshot source geometry and force raw linear atlas sampling.</summary>
    [Fact]
    public void DistanceFieldSnapshotsAtlasGeometryAndPreservesPaintSharing()
    {
        using var atlas = new DispatchTexture { Format = TextureFormat.Rgba16Float };
        using IGraphicsTextureView view = atlas.CreateView();
        Matrix3x2 transform = Matrix3x2.CreateScale(2, 3) * Matrix3x2.CreateTranslation(5, 7);
        var pattern = new ImagePattern2D { View = view, Source = new Rect2D(2, 3, 7, 8), PatternToLocal = transform };
        var ink = PaintGraph2D.Solid(new Color2D(0.1f, 0.2f, 0.3f, 0.5f));
        DistanceFieldPaintNode2D node = Assert.IsType<DistanceFieldPaintNode2D>(PaintGraph2D.DistanceField(pattern, 4, ink).Root);
        Assert.NotSame(pattern, node.Pattern);
        Assert.Same(view, node.Pattern.View);
        Assert.Equal(pattern.Source, node.Pattern.Source);
        Assert.Equal(transform, node.Pattern.PatternToLocal);
        Assert.Equal(4, node.DistanceRange);
        Assert.Equal(DistanceFieldMode2D.Msdf, node.Mode);
        Assert.Same(ink.Root, node.Child);
        Assert.Equal(AlphaRepresentation2D.Premultiplied, node.Pattern.Alpha);
        Assert.Equal(ImageTileMode2D.Decal, node.Pattern.TileX);
        Assert.Equal(ImageTileMode2D.Decal, node.Pattern.TileY);
        Assert.Equal(ImageFilter2D.Linear, node.Pattern.Filter);
    }

    /// <summary>Distance channels remain linear data and cannot use a color-decoding or invalid source.</summary>
    [Fact]
    public void DistanceFieldRejectsColorTexturesAndInvalidSamplingGeometry()
    {
        using var linear = new DispatchTexture { Format = TextureFormat.Rgba8Unorm };
        using var srgb = new DispatchTexture { Format = TextureFormat.Rgba8Srgb };
        using var unsampled = new DispatchTexture { Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.CopyDestination };
        using IGraphicsTextureView linearView = linear.CreateView();
        using IGraphicsTextureView srgbView = srgb.CreateView();
        using IGraphicsTextureView unsampledView = unsampled.CreateView();
        var pattern = new ImagePattern2D { View = linearView, Source = new Rect2D(2, 3, 7, 8) };
        var ink = PaintGraph2D.Solid(new Color2D(1, 0, 0, 0.5f));

        Assert.Throws<ArgumentOutOfRangeException>(() => PaintGraph2D.DistanceField(pattern, 0, ink));
        Assert.Throws<ArgumentOutOfRangeException>(() => PaintGraph2D.DistanceField(pattern, -1, ink));
        Assert.Throws<ArgumentOutOfRangeException>(() => PaintGraph2D.DistanceField(pattern, float.NaN, ink));
        Assert.Throws<ArgumentOutOfRangeException>(() => PaintGraph2D.DistanceField(pattern, float.PositiveInfinity, ink));
        Assert.Throws<ArgumentOutOfRangeException>(() => PaintGraph2D.DistanceField(pattern, 4, ink, (DistanceFieldMode2D)42));
        Assert.Throws<ArgumentException>(() => PaintGraph2D.DistanceField(pattern with { PatternToLocal = default }, 4, ink));
        Assert.Throws<ArgumentOutOfRangeException>(() => PaintGraph2D.DistanceField(pattern with { Source = new Rect2D(15, 0, 2, 1) }, 4, ink));
        Assert.Throws<ArgumentOutOfRangeException>(() => PaintGraph2D.DistanceField(pattern with { Source = new Rect2D(0.5f, 0, 2, 1) }, 4, ink));
        Assert.Throws<ArgumentException>(() => PaintGraph2D.DistanceField(pattern with { View = srgbView }, 4, ink));
        Assert.Throws<ArgumentException>(() => PaintGraph2D.DistanceField(pattern with { View = unsampledView }, 4, ink));
        Assert.Throws<ArgumentNullException>(() => PaintGraph2D.DistanceField(null!, 4, ink));
        Assert.Throws<ArgumentNullException>(() => PaintGraph2D.DistanceField(pattern, 4, null!));
    }

    /// <summary>Atlas sampling transforms affect the mask while the paint stays in the canvas coordinate system.</summary>
    [Fact]
    public void CompiledMaskRegistersAtlasAndKeepsPaintCoordinatesIndependent()
    {
        using var atlas = new DispatchTexture { Width = 64, Height = 32, Format = TextureFormat.Rgba8Unorm };
        using IGraphicsTextureView view = atlas.CreateView();
        var patternTransform = new Matrix3x2(2, 0, 0, 3, 5, 7);
        var pattern = new ImagePattern2D { View = view, Source = new Rect2D(8, 4, 16, 12), PatternToLocal = patternTransform };
        var ink = PaintGraph2D.LinearGradient(Vector2.Zero, new Vector2(40, 0), [new(0, new Color2D(1, 0, 0, 1)), new(1, new Color2D(0, 0, 1, 1))]);
        var paint = PaintGraph2D.DistanceField(pattern, 6, ink, DistanceFieldMode2D.Mtsdf);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.Translate(new Vector2(11, 13));
        canvas.Scale(new Vector2(2, 4));
        canvas.FillRect(new Rect2D(0, 0, 40, 40), paint);
        DrawingCommand2D command = Assert.Single(canvas.End().Commands);
        var target = new DrawingTargetInfo2D
        {
            Description = new TextureDesc { Width = 300, Height = 200, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment },
            PixelScale = 2,
            PixelOrigin = new Vector2(3, 4),
        };
        using var table = new RecordingTable();
        var program = new DrawingProgram2D(table, new ResourceReference<IGraphicsSampler>(), target);
        program.Compile(command);
        program.ValidateFiniteData();

        Assert.Same(view, Assert.Single(table.Views));
        Assert.Same(atlas, Assert.Single(program.Textures));
        DrawingInstruction mask = Assert.Single(program.Instructions, instruction => instruction.Kind == 15);
        DrawingInstruction gradient = Assert.Single(program.Instructions, instruction => instruction.Kind == 6);
        Assert.Equal(6, mask.D.X);
        Assert.Equal((float)DistanceFieldMode2D.Mtsdf, mask.D.Y);
        Assert.Equal(new Vector4(8, 4, 16, 12), mask.A);
        Assert.Equal(new Vector4(64, 32, 3, 3), mask.B);
        Assert.Equal(new Vector4(1, 1, 0, 0), mask.C);
        Assert.Equal((uint)program.Instructions.IndexOf(gradient), mask.First);
        Matrix3x2 canvasToPixel = command.State.Transform * Matrix3x2.CreateScale(2) * Matrix3x2.CreateTranslation(-target.PixelOrigin);
        Assert.True(Matrix3x2.Invert(canvasToPixel, out Matrix3x2 paintInverse));
        Assert.True(Matrix3x2.Invert(patternTransform * canvasToPixel, out Matrix3x2 maskInverse));
        Assert.Equal(new Vector4(paintInverse.M11, paintInverse.M21, paintInverse.M31, 0), gradient.T0);
        Assert.Equal(new Vector4(paintInverse.M12, paintInverse.M22, paintInverse.M32, 0), gradient.T1);
        Assert.Equal(new Vector4(maskInverse.M11, maskInverse.M21, maskInverse.M31, 0), mask.T0);
        Assert.Equal(new Vector4(maskInverse.M12, maskInverse.M22, maskInverse.M32, 0), mask.T1);
        Assert.True(program.Instructions.IndexOf(gradient) < program.Instructions.IndexOf(mask));
    }

    private sealed class RecordingTable : IArgumentTable
    {
        public uint TextureCapacity => 256;

        public uint SamplerCapacity => 256;

        public uint BufferCapacity => 2;

        internal List<IGraphicsTextureView> Views { get; } = [];

        public IGpuRef<IGraphicsTextureView> WriteTexture(uint slot, IGraphicsTextureView view)
        {
            Views.Add(view);
            return new ResourceReference<IGraphicsTextureView>();
        }

        public IGpuRef<IGraphicsSampler> WriteSampler(uint slot, IGraphicsSampler sampler) => new ResourceReference<IGraphicsSampler>();

        public IGpuRef<T> WriteBuffer<T>(uint slot, BufferSlice<T> range)
            where T : unmanaged => throw new NotSupportedException();

        public IGpuRef<T> WriteBuffer<T>(uint slot, ShaderDataSlice<T> range)
            where T : struct, IShaderData => throw new NotSupportedException();

        public void ReleaseTexture(uint slot)
        {
        }

        public void ReleaseSampler(uint slot)
        {
        }

        public void ReleaseBuffer(uint slot)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class ResourceReference<T> : IGpuRef<T>
    {
        public ulong Count => 1;

        public IGpuRef<T> GetElement(ulong index) => index == 0 ? this : throw new ArgumentOutOfRangeException(nameof(index));
    }
}
