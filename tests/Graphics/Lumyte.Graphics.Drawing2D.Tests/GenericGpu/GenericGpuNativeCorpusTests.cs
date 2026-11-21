using System.Numerics;
using Lumyte.Execution;
using Lumyte.Graphics.Abstractions;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Actual Vulkan logical-command and endpoint pixel corpus.</summary>
public sealed partial class GenericGpuNativeTests
{
    /// <summary>Checks Rectangle using independent analytic RGBA and separated/fused native output.</summary>
    [GenericGpuNativeFact]
    public void CorpusRectangle() => AssertCorpus(2, new(1f, 0f, 0f, 1f));

    /// <summary>Checks RoundedRectangle using independent analytic RGBA and separated/fused native output.</summary>
    [GenericGpuNativeFact]
    public void CorpusRoundedRectangle() => AssertCorpus(3, new(1f, 0f, 0f, 1f));

    /// <summary>Checks Circle using independent analytic RGBA and separated/fused native output.</summary>
    [GenericGpuNativeFact]
    public void CorpusCircle() => AssertCorpus(4, new(1f, 0f, 0f, 1f));

    /// <summary>Checks Line using independent analytic RGBA and separated/fused native output.</summary>
    [GenericGpuNativeFact]
    public void CorpusLine() => AssertCorpus(5, new(1f, 0f, 0f, 1f));

    /// <summary>Checks FillPath using independent analytic RGBA and separated/fused native output.</summary>
    [GenericGpuNativeFact]
    public void CorpusFillPath() => AssertCorpus(6, new(1f, 0f, 0f, 1f));

    /// <summary>Checks StrokePath using independent analytic RGBA and separated/fused native output.</summary>
    [GenericGpuNativeFact]
    public void CorpusStrokePath() => AssertCorpus(7, new(1f, 0f, 0f, 1f));

    /// <summary>Checks Image using independent analytic RGBA and separated/fused native output.</summary>
    [GenericGpuNativeFact]
    public void CorpusImage() => AssertCorpus(8, new(0.5019607843137255f, 0f, 0f, 0.5019607843137255f));

    /// <summary>Checks CoverageMask using independent analytic RGBA and separated/fused native output.</summary>
    [GenericGpuNativeFact]
    public void CorpusCoverageMask() => AssertCorpus(9, new(0.5019607843137255f, 0f, 0f, 0.5019607843137255f));

    /// <summary>Checks Sdf using independent analytic RGBA and separated/fused native output.</summary>
    [GenericGpuNativeFact]
    public void CorpusSdf() => AssertCorpus(10, new(0.5019607843137255f, 0f, 0f, 0.5019607843137255f));

    /// <summary>Checks MeshCommand using independent analytic RGBA and separated/fused native output.</summary>
    [GenericGpuNativeFact]
    public void CorpusMeshCommand() => AssertCorpus(11, new(0.4375f, 0f, 0f, 0.4375f));

    /// <summary>Checks PaintGraphComposite using independent analytic RGBA and separated/fused native output.</summary>
    [GenericGpuNativeFact]
    public void CorpusPaintGraphComposite() => AssertCorpus(12, new(0.5f, 0f, 0.5f, 1f));

    /// <summary>Checks LinearGradient using independent analytic RGBA and separated/fused native output.</summary>
    [GenericGpuNativeFact]
    public void CorpusLinearGradient() => AssertCorpus(13, new(0.5f, 0f, 0.5f, 1f));

    /// <summary>Checks ImagePattern using independent analytic RGBA and separated/fused native output.</summary>
    [GenericGpuNativeFact]
    public void CorpusImagePattern() => AssertCorpus(16, new(0.5019607843137255f, 0f, 0f, 0.5019607843137255f));

    /// <summary>Checks DistanceFieldPaint using independent analytic RGBA and separated/fused native output.</summary>
    [GenericGpuNativeFact]
    public void CorpusDistanceFieldPaint()
    {
        // DistanceField normalizes to transparent extension and linear filtering.
        // A white 1x1 atlas has bilinear value (1-|x-.5|)*(1-|y-.5|).
        // Its four .125/.875 corner samples lie below the contour; the other 12 are inside.
        float coverage = 0;
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                float distance = (1 - MathF.Abs(((x + .5f) / 4) - .5f)) * (1 - MathF.Abs(((y + .5f) / 4) - .5f));
                coverage += Math.Clamp(((distance - .5f) * 16) + .5f, 0, 1) / 16;
            }
        }

        Assert.Equal(.75f, coverage);
        AssertCorpus(17, new(coverage, 0, 0, coverage));
    }

    /// <summary>Checks TransformClipLayersOpacity using independent analytic RGBA and separated/fused native output.</summary>
    [GenericGpuNativeFact]
    public void CorpusTransformClipLayersOpacity() => AssertCorpus(18, new(0.25f, 0f, 0.5f, 0.75f));

    /// <summary>Checks Msdf using independent analytic RGBA and separated/fused native output.</summary>
    [GenericGpuNativeFact]
    public void CorpusMsdf() => AssertCorpus(19, new(1f, 0f, 0f, 1f));

    /// <summary>Checks Mtsdf using independent analytic RGBA and separated/fused native output.</summary>
    [GenericGpuNativeFact]
    public void CorpusMtsdf() => AssertCorpus(20, new(1f, 0f, 0f, 1f));

    /// <summary>Checks nonlinear radial and sweep gradients against independently averaged sample coordinates.</summary>
    [GenericGpuNativeFact]
    public void CorpusRadialAndSweepGradients()
    {
        float radial = 0;
        float sweep = 0;
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                var p = new Vector2((x + .5f) / 4, (y + .5f) / 4);
                radial += p.Length() / 2 / 16;
                sweep += MathF.Atan2(p.Y + 1, p.X + 1) / MathF.Tau / 16;
            }
        }

        AssertCorpus(14, new(1 - radial, 0, radial, 1));
        AssertCorpus(15, new(1 - sweep, 0, sweep, 1));
    }

    /// <summary>Requires actual mesh endpoint pixels; unsupported native capability fails explicitly rather than passing.</summary>
    [GenericGpuNativeMeshFact]
    public void MeshEndpointExecutesActualPixels()
    {
        IGraphicDevice device = CreateDevice();
        using var owner = (IDisposable)device;
        Assert.True(
            device.Caps.Features.HasFlag(GraphicsFeatures.MeshShader),
            $"Actual Vulkan device does not enable MeshShader; limits={device.Caps.MeshShaderLimits}. Mesh endpoint execution is unqualified.");
        byte[] pixels = Render(true, 11, mesh: true);
        Assert.Equal(Render(true, 11), pixels);
        AssertPixel(pixels, new(.4375f, 0, 0, .4375f));
    }

    /// <summary>Exercises every corpus program on actual mesh dispatch rather than only shader creation.</summary>
    [GenericGpuNativeMeshFact]
    public void MeshEndpointExecutesCompleteCorpus()
    {
        for (int scenario = 0; scenario <= 22; scenario++)
        {
            Assert.Equal(Render(true, scenario), Render(true, scenario, mesh: true));
        }
    }

    /// <summary>Retains correlated transformed clip coverage and singular paint invisibility.</summary>
    [GenericGpuNativeFact]
    public void CorpusTransformedClipAndSingularPaint()
    {
        AssertCorpus(21, new(.5f, 0, 0, .5f));
        AssertCorpus(22, Vector4.Zero);
    }

    /// <summary>Integer outer scissor clips the second pixel for both decomposed and fused raster endpoints.</summary>
    /// <param name="fused">Whether to use the concrete fused raster endpoint.</param>
    [GenericGpuNativeTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void IntegerOuterScissorClipsRasterSecondPixel(bool fused)
    {
        byte[] pixels = Render(fused, 23);
        AssertPixel(pixels.AsSpan(0, 8).ToArray(), new(1, 0, 0, 1));
        Assert.Equal(new byte[8], pixels.AsSpan(8, 8).ToArray());
    }

    /// <summary>Native mesh uses the same integer outer scissor as the independent clip stage.</summary>
    [GenericGpuNativeMeshFact]
    public void IntegerOuterScissorClipsMeshSecondPixel()
    {
        byte[] pixels = Render(true, 23, mesh: true);
        AssertPixel(pixels.AsSpan(0, 8).ToArray(), new(1, 0, 0, 1));
        Assert.Equal(new byte[8], pixels.AsSpan(8, 8).ToArray());
    }

    private static void AssertCorpus(int scenario, Vector4 expected)
    {
        byte[] separated = Render(false, scenario);
        byte[] fused = Render(true, scenario);
        Assert.Equal(separated, fused);
        AssertPixel(separated, expected);
    }

    private static void AssertPixel(byte[] pixels, Vector4 expected)
    {
        for (int channel = 0; channel < 4; channel++)
        {
            float actual = (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(pixels, channel * 2));
            Assert.True(
                MathF.Abs(expected[channel] - actual) <= .0005f,
                $"Channel {channel}: expected {expected[channel]}, actual {actual}");
        }
    }

    private static void PopulateCorpus(Canvas2D canvas, NativeCorpusResources resources, int scenario)
    {
        var red = PaintGraph2D.Solid(new SRgba<float>(1, 0, 0, 1));
        var blue = PaintGraph2D.Solid(new SRgba<float>(0, 0, 1, 1));
        Rect2D rect = new(0, 0, 1, 1);
        var path = new PathBuilder2D();
        path.MoveTo(new(0, 0));
        path.LineTo(new(1, 0));
        path.LineTo(new(1, 1));
        path.LineTo(new(0, 1));
        path.Close();
        ColorStop2D[] stops = [new(0, new(1, 0, 0, 1)), new(1, new(0, 0, 1, 1))];
        switch (scenario)
        {
            case 2:
                canvas.FillRect(rect, red);
                break;
            case 3:
                canvas.FillRoundedRect(new(-1, -1, 3, 3), .25f, red);
                break;
            case 4:
                canvas.FillCircle(new(.5f, .5f), 2, red);
                break;
            case 5:
                canvas.DrawLine(new(-1, .5f), new(2, .5f), red, new() { Width = 1 });
                break;
            case 6:
                canvas.FillPath(path.Build(), red);
                break;
            case 7:
                var stroke = new PathBuilder2D();
                stroke.MoveTo(new(-1, .5f));
                stroke.LineTo(new(2, .5f));
                canvas.StrokePath(stroke.Build(), red, new() { Width = 1 });
                break;
            case 8:
                canvas.DrawImage(new(resources.Texture(TextureFormat.Rgba8Unorm, [255, 0, 0, 128]), resources.Sampler), rect, rect);
                break;
            case 9:
                canvas.DrawCoverageMask(new(resources.Texture(TextureFormat.R8Unorm, [128]), resources.Sampler), rect, rect, red);
                break;
            case 10:
                canvas.DrawDistanceField(new(resources.Texture(TextureFormat.R8Unorm, [128]), resources.Sampler, DistanceFieldEncoding2D.Sdf, 1), rect, rect, red);
                break;
            case 11:
                canvas.DrawMesh(new([new(new(0, 0), .25f), new(new(2, 0), .5f), new(new(0, 2), .75f)], [0, 1, 2]), red);
                break;
            case 12:
                canvas.DrawPaintGraph(PaintGraph2D.Composite(PaintGraph2D.Opacity(.5f, red), blue));
                break;
            case 13:
                canvas.DrawPaintGraph(PaintGraph2D.LinearGradient(new(0, 0), new(1, 0), stops));
                break;
            case 14:
                canvas.DrawPaintGraph(PaintGraph2D.RadialGradient(new(0, 0), 2, stops));
                break;
            case 15:
                canvas.DrawPaintGraph(PaintGraph2D.SweepGradient(new(-1, -1), 0, MathF.Tau, stops, ExtendMode2D.Pad));
                break;
            case 16:
                canvas.DrawPaintGraph(PaintGraph2D.ImagePattern(new() { View = resources.Texture(TextureFormat.Rgba8Unorm, [255, 0, 0, 128]), Source = rect, Filter = ImageFilter2D.Nearest }));
                break;
            case 17:
                canvas.DrawPaintGraph(PaintGraph2D.DistanceField(new() { View = resources.Texture(TextureFormat.Rgba8Unorm, [255, 255, 255, 255]), Source = rect, TileX = ImageTileMode2D.Clamp, TileY = ImageTileMode2D.Clamp }, 4, red));
                break;
            case 18:
                canvas.DrawPaintGraph(PaintGraph2D.Transform(Matrix3x2.Identity, PaintGraph2D.Clip(path.Build(), PaintGraph2D.Layers([PaintGraph2D.Opacity(.5f, red), PaintGraph2D.Opacity(.5f, blue)]))));
                break;
            case 19:
            case 20:
                canvas.DrawDistanceField(new(resources.Texture(TextureFormat.Rgba8Unorm, [255, 255, 255, 255]), resources.Sampler, scenario == 19 ? DistanceFieldEncoding2D.Msdf : DistanceFieldEncoding2D.Mtsdf, 4), rect, rect, red);
                break;
            case 21:
                var half = new PathBuilder2D();
                half.MoveTo(new(0, 0));
                half.LineTo(new(.5f, 0));
                half.LineTo(new(.5f, 1));
                half.LineTo(new(0, 1));
                half.Close();
                canvas.DrawPaintGraph(PaintGraph2D.Transform(Matrix3x2.CreateTranslation(.25f, 0), PaintGraph2D.Clip(half.Build(), red)));
                break;
            case 22:
                canvas.DrawPaintGraph(PaintGraph2D.Transform(default(Matrix3x2), red));
                break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
    }

    private sealed class NativeCorpusResources(IGraphicDevice device, ExecutionRuntime runtime) : IDisposable
    {
        private readonly List<IDisposable> _owned = [];
        private IGraphicsSampler? _sampler;

        public IGraphicsSampler Sampler => _sampler ??= Own(device.CreateSampler(new()));

        public IGraphicsTextureView Texture(TextureFormat format, byte[] texel)
        {
            IGraphicsTexture texture = Own(device.CreateTexture(new() { Width = 1, Height = 1, Format = format, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination }));
            using IGraphicsBuffer<byte> upload = device.CreateBuffer<byte>(new() { Count = 256, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
            Pump(runtime, upload.MapAsync().AsTask());
            byte[] data = new byte[256];
            texel.CopyTo(data, 0);
            upload.CopyFrom(data);
            upload.Unmap();
            using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
            commands.Barrier(new BufferBarrierDesc<byte> { Buffer = upload.SliceElements(0, 256), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
            commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.CopyDestination, Before = default, After = new(PipelineStage.Copy, ResourceAccess.CopyWrite) });
            commands.CopyBufferToTexture(new() { Buffer = upload.SliceElements(0, 256), BytesPerRow = 256, RowsPerImage = 1 }, new() { Texture = texture, Width = 1, Height = 1 });
            commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.CopyDestination, AfterState = TextureState.Sampled, Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.ComputeShader | PipelineStage.FragmentShader, ResourceAccess.ShaderRead) });
            commands.Finish();
            using IGraphicsSubmission submission = device.Queue.Submit([commands]);
            Pump(runtime, submission.WaitAsync().AsTask());
            return Own(texture.CreateView());
        }

        public void Dispose()
        {
            for (int i = _owned.Count - 1; i >= 0; i--)
            {
                _owned[i].Dispose();
            }
        }

        private T Own<T>(T resource)
            where T : IDisposable
        {
            _owned.Add(resource);
            return resource;
        }
    }
}
