using System.Buffers.Binary;
using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.ImageEffects;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Lumyte.Resources;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks the complete 2D pipeline against independently specified GPU pixel results.</summary>
public sealed class Drawing2DTests
{
    private static readonly Color2D _white = new(1, 1, 1, 1);
    private static readonly Color2D _red = new(1, 0, 0, 1);
    private static readonly Color2D _blue = new(0, 0, 1, 1);
    private static readonly BarrierScope _copyRead = new(PipelineStage.Copy, ResourceAccess.CopyRead);
    private static readonly BarrierScope _copyWrite = new(PipelineStage.Copy, ResourceAccess.CopyWrite);
    private static readonly BarrierScope _sampleRead = new(PipelineStage.VertexShader | PipelineStage.FragmentShader | PipelineStage.ComputeShader, ResourceAccess.ShaderRead);

    /// <summary>Checks geometry, winding, transformed clips and coverage on Vulkan.</summary>
    /// <returns>The asynchronous GPU checks.</returns>
    [GpuFact]
    public async Task VulkanGeometryAndCoverageAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckGeometryAsync(device);
        await CheckClipsAsync(device);
    }

    /// <summary>Checks geometry, winding, transformed clips and coverage on wgpu.</summary>
    /// <returns>The asynchronous GPU checks.</returns>
    [GpuFact]
    public async Task WgpuGeometryAndCoverageAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckGeometryAsync(device);
        await CheckClipsAsync(device);
    }

    /// <summary>Checks linear-light paints and all COLRv1 composite modes on Vulkan.</summary>
    /// <returns>The asynchronous GPU checks.</returns>
    [GpuFact]
    public async Task VulkanPaintAndCompositeAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckGradientsAsync(device);
        await CheckCompositesAsync(device);
        await CheckPaintOpacityAsync(device);
    }

    /// <summary>Checks linear-light paints and all COLRv1 composite modes on wgpu.</summary>
    /// <returns>The asynchronous GPU checks.</returns>
    [GpuFact]
    public async Task WgpuPaintAndCompositeAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckGradientsAsync(device);
        await CheckCompositesAsync(device);
        await CheckPaintOpacityAsync(device);
    }

    /// <summary>Checks images, atlas sampling and external glyph representations on Vulkan.</summary>
    /// <returns>The asynchronous GPU checks.</returns>
    [GpuFact]
    public async Task VulkanImagesAndGlyphRepresentationsAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckImagesAsync(device);
        await CheckGlyphRepresentationsAsync(device);
    }

    /// <summary>Checks images, atlas sampling and external glyph representations on wgpu.</summary>
    /// <returns>The asynchronous GPU checks.</returns>
    [GpuFact]
    public async Task WgpuImagesAndGlyphRepresentationsAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckImagesAsync(device);
        await CheckGlyphRepresentationsAsync(device);
    }

    /// <summary>Checks layer effects, clip timing and backdrop replacement on Vulkan.</summary>
    /// <returns>The asynchronous GPU checks.</returns>
    [GpuFact]
    public async Task VulkanLayersEffectsAndBackdropAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckLayersAsync(device);
        await CheckEffectsAsync(device);
        await CheckBackdropAsync(device);
        await CheckComposedEffectsAsync(device);
    }

    /// <summary>Checks layer effects, clip timing and backdrop replacement on wgpu.</summary>
    /// <returns>The asynchronous GPU checks.</returns>
    [GpuFact]
    public async Task WgpuLayersEffectsAndBackdropAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckLayersAsync(device);
        await CheckEffectsAsync(device);
        await CheckBackdropAsync(device);
        await CheckComposedEffectsAsync(device);
    }

    /// <summary>Checks standalone preparation and sRGB encoding on Vulkan.</summary>
    /// <returns>The asynchronous GPU checks.</returns>
    [GpuFact]
    public async Task VulkanStandaloneSrgbTargetAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckStandaloneAsync(device);
    }

    /// <summary>Checks standalone preparation and sRGB encoding on wgpu.</summary>
    /// <returns>The asynchronous GPU checks.</returns>
    [GpuFact]
    public async Task WgpuStandaloneSrgbTargetAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckStandaloneAsync(device);
    }

    internal static async Task CheckStandaloneAsync(IGraphicDevice device, Renderer2D? managedRenderer = null, ResourceManager? manager = null)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(1, 0, 2, 2), new Color2D(0.5f, 0.5f, 0.5f, 1));
        canvas.FillCircle(Vector2.Zero, 1, new Color2D(0, 0, 0, 0));
        DrawList2D list = canvas.End();
        using IGraphicsTexture target = device.CreateTexture(new() { Width = 4, Height = 2, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView view = target.CreateView();
        var programs = new ExternalDrawingPrograms();
        using Renderer2D renderer = managedRenderer ?? new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), programs);
        PreparedDraw2D prepared = await renderer.PrepareAsync(list, new() { View = view, LoadOp = AttachmentLoadOp.Clear, ClearColor = new(0, 0, 1, 0.25f) });
        if (managedRenderer is not null)
        {
            // Only the prepared batches now protect shared pipeline/modules/sampler during the native draw.
            renderer.Dispose();
            await manager!.TrimAsync();
        }

        uint pitch = Math.Max(16, device.GetTextureCopyLayout(TextureFormat.Rgba8Srgb).BytesPerRowAlignment);
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch * 2, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        IGraphicsSubmission? submission = null;
        var colorScope = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
        try
        {
            commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = colorScope });
            prepared.Record(commands);
            Assert.Throws<InvalidOperationException>(() => prepared.Record(commands));
            commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = colorScope, After = _copyRead });
            commands.CopyTextureToBuffer(new() { Texture = target, Width = 4, Height = 2 }, new() { Buffer = readback.Slice(0, readback.Count), BytesPerRow = pitch, RowsPerImage = 2 });
            commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.Slice(0, readback.Count), Before = _copyWrite, After = new(PipelineStage.Host, ResourceAccess.HostRead) });
            commands.Finish();
            submission = device.Queue.Submit([commands]);
            await submission.WaitAsync();
            await readback.MapAsync();
            byte[] pixels = new byte[checked((int)readback.Count)];
            readback.CopyTo(pixels);
            readback.Unmap();
            for (int y = 0; y < 2; y++)
            {
                int row = checked((int)(y * pitch));
                Assert.InRange(pixels[row + 2], (byte)136, (byte)138);
                Assert.InRange(pixels[row + 3], (byte)63, (byte)64);
                for (int component = 0; component < 3; component++)
                {
                    Assert.InRange(pixels[row + 4 + component], (byte)127, (byte)128);
                }

                Assert.Equal(255, pixels[row + 7]);
            }
        }
        finally
        {
            commands.Dispose();
            if (managedRenderer is null)
            {
                Assert.True(programs.Acquired > 0);
                Assert.Equal(0, programs.Returned);
            }

            prepared.Dispose();
            Assert.Equal(programs.Acquired, programs.Returned);
            submission?.Dispose();
        }
    }

    private static async Task CheckGeometryAsync(IGraphicDevice device)
    {
        var winding = new PathBuilder2D();
        AddRectangle(winding, 1, 1, 8, 8);
        AddRectangle(winding, 3, 3, 4, 4, reverse: true);
        var evenOdd = new PathBuilder2D();
        AddRectangle(evenOdd, 12, 1, 8, 8);
        AddRectangle(evenOdd, 14, 3, 4, 4);
        var stroke = new PathBuilder2D();
        stroke.MoveTo(new(23, 13));
        stroke.LineTo(new(29, 13));
        stroke.LineTo(new(29, 18));
        var curves = new PathBuilder2D();
        curves.MoveTo(new(33, 1));
        curves.QuadraticTo(new(39, 1), new(39, 7));
        curves.CubicTo(new(39, 9), new(33, 9), new(33, 1));
        curves.Close();
        Vector4[] pixels = await RenderAsync(device, 40, 20, canvas =>
        {
            canvas.FillPath(winding.Build(), _white);
            canvas.FillPath(evenOdd.Build(), _white, FillRule2D.EvenOdd);
            canvas.FillRoundedRect(new(23, 1, 8, 8), 3, _white);
            canvas.FillPath(curves.Build(), _white);
            canvas.FillCircle(new(5, 15), 3, _white);
            canvas.DrawLine(new(11, 15), new(18, 15), _white, new() { Width = 2 });
            canvas.StrokePath(stroke.Build(), _white, new() { Width = 2, Join = LineJoin2D.Round });
            canvas.DrawMesh(new([new(new(33, 11), 0.5f), new(new(39, 11), 0.5f), new(new(33, 19), 0.5f)], [0, 1, 2]), PaintGraph2D.Solid(_white));
        });
        AssertPixel(pixels, 40, 2, 2, Vector4.One);
        AssertPixel(pixels, 40, 4, 4, Vector4.Zero);
        AssertPixel(pixels, 40, 13, 2, Vector4.One);
        AssertPixel(pixels, 40, 15, 4, Vector4.Zero);
        AssertPixel(pixels, 40, 27, 5, Vector4.One);
        AssertPixel(pixels, 40, 23, 1, Vector4.Zero, 0.03f);
        AssertPixel(pixels, 40, 36, 5, Vector4.One);
        AssertPixel(pixels, 40, 32, 5, Vector4.Zero);
        AssertPixel(pixels, 40, 4, 14, Vector4.One);
        Assert.InRange(pixels[(13 * 40) + 7].W, 0.2f, 0.9f);
        AssertPixel(pixels, 40, 1, 11, Vector4.Zero);
        AssertPixel(pixels, 40, 14, 14, Vector4.One);
        AssertPixel(pixels, 40, 10, 14, Vector4.Zero);
        AssertPixel(pixels, 40, 25, 12, Vector4.One);
        AssertPixel(pixels, 40, 28, 16, Vector4.One);
        AssertPixel(pixels, 40, 34, 12, new(0.5f));
        AssertPixel(pixels, 40, 38, 18, Vector4.Zero);
    }

    private static async Task CheckClipsAsync(IGraphicDevice device)
    {
        Vector4[] correlated = await RenderAsync(device, 8, 4, canvas =>
        {
            canvas.ClipRect(new(0.5f, 0, 6, 4));
            canvas.FillRect(new(0.5f, 0, 6, 4), PaintGraph2D.Clip(Rectangle(0.5f, 0, 6, 4), PaintGraph2D.Solid(_white)));
        });
        AssertPixel(correlated, 8, 0, 1, new(0.5f));
        AssertPixel(correlated, 8, 1, 1, Vector4.One);
        AssertPixel(correlated, 8, 6, 1, new(0.5f));
        AssertPixel(correlated, 8, 7, 1, Vector4.Zero);

        Vector4[] transformed = await RenderAsync(
            device,
            6,
            4,
            canvas =>
        {
            canvas.Save();
            canvas.ConcatTransform(new(1, 0, 1, 1, 0, 0));
            canvas.ClipRect(new(0, 0, 2, 2));
            canvas.FillRect(new(-10, -10, 20, 20), _white);
            canvas.Restore();
            canvas.Translate(new(2, 1));
            canvas.FillRect(new(0, 0, 0.5f, 0.5f), _red);
        },
            pixelScale: 2);
        AssertPixel(transformed, 6, 0, 2, Vector4.Zero);
        AssertPixel(transformed, 6, 3, 2, Vector4.One);
        AssertPixel(transformed, 6, 4, 2, new(1, 0, 0, 1));
    }

    private static async Task CheckGradientsAsync(IGraphicDevice device)
    {
        ColorStop2D[] transparentBlueToRed = [new(0, new(0, 0, 1, 0)), new(1, _red)];
        ColorStop2D[] blackToWhite = [new(0, new(0, 0, 0, 1)), new(1, _white)];
        Vector4[] pixels = await RenderAsync(device, 8, 4, canvas =>
        {
            canvas.FillRect(new(0, 0, 8, 1), PaintGraph2D.LinearGradient(Vector2.Zero, new(8, 0), transparentBlueToRed));
            canvas.FillRect(new(0, 1, 8, 1), PaintGraph2D.LinearGradient(Vector2.Zero, new(4, 0), new(4, 4), blackToWhite, ExtendMode2D.Pad));
            canvas.FillRect(new(0, 2, 8, 1), new Color2D(0.5f, 0.5f, 0.5f, 1));
            canvas.FillRect(new(0, 3, 8, 1), PaintGraph2D.LinearGradient(Vector2.Zero, new(4, 0), blackToWhite, ExtendMode2D.Reflect));
        });
        AssertPixel(pixels, 8, 3, 0, new(0.4375f, 0, 0, 0.4375f));
        AssertPixel(pixels, 8, 3, 1, new(0.5f, 0.5f, 0.5f, 1));
        AssertPixel(pixels, 8, 3, 2, new(0.21404114f, 0.21404114f, 0.21404114f, 1));
        AssertPixel(pixels, 8, 4, 3, new(0.875f, 0.875f, 0.875f, 1));

        Vector4[] otherGradients = await RenderAsync(device, 12, 4, canvas =>
        {
            canvas.FillRect(new(0, 0, 4, 4), PaintGraph2D.RadialGradient(new(0.5f, 0.5f), 4, blackToWhite));
            canvas.FillRect(new(4, 0, 4, 4), PaintGraph2D.SweepGradient(new(4.5f, 0.5f), 0, MathF.PI, blackToWhite, ExtendMode2D.Pad));
            canvas.FillRect(new(8, 0, 4, 4), PaintGraph2D.RadialGradient(new(8, 0.5f), 1, new(9, 0.5f), 3, blackToWhite, ExtendMode2D.Pad));
        });
        AssertPixel(otherGradients, 12, 2, 0, new(0.5f, 0.5f, 0.5f, 1), 0.02f);
        AssertPixel(otherGradients, 12, 4, 2, new(0.5f, 0.5f, 0.5f, 1), 0.02f);
        AssertPixel(otherGradients, 12, 10, 0, new(0.5f, 0.5f, 0.5f, 1), 0.02f);
    }

    private static async Task CheckCompositesAsync(IGraphicDevice device)
    {
        // Source is straight red at alpha 1/2; backdrop is straight blue at alpha 1/4.
        // The W3C blend equation contributes (3/8, 0, 1/8) plus B(Cb,Cs)/8.
        (CompositeMode2D Mode, Vector4 Expected)[] cases =
        [
            (CompositeMode2D.Clear, Vector4.Zero),
            (CompositeMode2D.Src, new(0.5f, 0, 0, 0.5f)),
            (CompositeMode2D.Dest, new(0, 0, 0.25f, 0.25f)),
            (CompositeMode2D.SrcOver, new(0.5f, 0, 0.125f, 0.625f)),
            (CompositeMode2D.DestOver, new(0.375f, 0, 0.25f, 0.625f)),
            (CompositeMode2D.SrcIn, new(0.125f, 0, 0, 0.125f)),
            (CompositeMode2D.DestIn, new(0, 0, 0.125f, 0.125f)),
            (CompositeMode2D.SrcOut, new(0.375f, 0, 0, 0.375f)),
            (CompositeMode2D.DestOut, new(0, 0, 0.125f, 0.125f)),
            (CompositeMode2D.SrcAtop, new(0.125f, 0, 0.125f, 0.25f)),
            (CompositeMode2D.DestAtop, new(0.375f, 0, 0.125f, 0.5f)),
            (CompositeMode2D.Xor, new(0.375f, 0, 0.125f, 0.5f)),
            (CompositeMode2D.Plus, new(0.5f, 0, 0.25f, 0.75f)),
            (CompositeMode2D.Screen, new(0.5f, 0, 0.25f, 0.625f)),
            (CompositeMode2D.Overlay, new(0.375f, 0, 0.25f, 0.625f)),
            (CompositeMode2D.Darken, new(0.375f, 0, 0.125f, 0.625f)),
            (CompositeMode2D.Lighten, new(0.5f, 0, 0.25f, 0.625f)),
            (CompositeMode2D.ColorDodge, new(0.375f, 0, 0.25f, 0.625f)),
            (CompositeMode2D.ColorBurn, new(0.375f, 0, 0.25f, 0.625f)),
            (CompositeMode2D.HardLight, new(0.5f, 0, 0.125f, 0.625f)),
            (CompositeMode2D.SoftLight, new(0.375f, 0, 0.25f, 0.625f)),
            (CompositeMode2D.Difference, new(0.5f, 0, 0.25f, 0.625f)),
            (CompositeMode2D.Exclusion, new(0.5f, 0, 0.25f, 0.625f)),
            (CompositeMode2D.Multiply, new(0.375f, 0, 0.125f, 0.625f)),
            (CompositeMode2D.HslHue, new(0.42083333f, 0, 0.125f, 0.625f)),
            (CompositeMode2D.HslSaturation, new(0.375f, 0, 0.25f, 0.625f)),
            (CompositeMode2D.HslColor, new(0.42083333f, 0, 0.125f, 0.625f)),
            (CompositeMode2D.HslLuminosity, new(0.4016854f, 0.026685394f, 0.25f, 0.625f)),
        ];
        Assert.Equal(Enum.GetValues<CompositeMode2D>(), cases.Select(item => item.Mode));
        var source = PaintGraph2D.Solid(new(1, 0, 0, 0.5f));
        var backdrop = PaintGraph2D.Solid(new(0, 0, 1, 0.25f));
        Vector4[] pixels = await RenderAsync(device, cases.Length, 1, canvas =>
        {
            for (int x = 0; x < cases.Length; x++)
            {
                canvas.FillRect(new(x, 0, 1, 1), PaintGraph2D.Composite(source, backdrop, cases[x].Mode));
            }
        });
        for (int x = 0; x < cases.Length; x++)
        {
            AssertPixel(pixels, cases.Length, x, 0, cases[x].Expected);
        }

        Vector4[] transparentDomain = await RenderAsync(device, 4, 1, canvas => canvas.DrawPaintGraph(PaintGraph2D.Composite(
            PaintGraph2D.Clip(Rectangle(0, 0, 1, 1), PaintGraph2D.Solid(_red)),
            PaintGraph2D.Clip(Rectangle(0, 0, 4, 1), PaintGraph2D.Solid(_blue)),
            CompositeMode2D.DestIn)));
        AssertPixel(transparentDomain, 4, 0, 0, new(0, 0, 1, 1));
        AssertPixel(transparentDomain, 4, 2, 0, Vector4.Zero);
    }

    private static async Task CheckPaintOpacityAsync(IGraphicDevice device)
    {
        var paint = PaintGraph2D.Opacity(0.5f, PaintGraph2D.Layers(
        [
            PaintGraph2D.Clip(Rectangle(0, 0, 4, 2), PaintGraph2D.Solid(_red)),
            PaintGraph2D.Clip(Rectangle(2, 0, 4, 2), PaintGraph2D.Solid(_blue)),
        ]));
        Vector4[] pixels = await RenderAsync(device, 8, 3, canvas => canvas.DrawPaintGraph(PaintGraph2D.Transform(Matrix3x2.CreateTranslation(1, 1), paint)));
        AssertPixel(pixels, 8, 1, 1, new(0.5f, 0, 0, 0.5f));
        AssertPixel(pixels, 8, 3, 1, new(0, 0, 0.5f, 0.5f));
        AssertPixel(pixels, 8, 6, 1, new(0, 0, 0.5f, 0.5f));
        AssertPixel(pixels, 8, 0, 1, Vector4.Zero);
    }

    private static async Task CheckImagesAsync(IGraphicDevice device)
    {
        using IGraphicsTexture atlas = device.CreateTexture(new() { Width = 4, Height = 1, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination });
        using IGraphicsTextureView view = atlas.CreateView();
        using IGraphicsSampler sampler = device.CreateSampler(new() { AddressU = AddressMode.ClampToEdge, AddressV = AddressMode.ClampToEdge });
        await UploadAsync(device, atlas, [0, 255, 0, 255, 255, 0, 0, 255, 0, 0, 255, 0, 0, 255, 0, 255]);
        var repeating = new ImagePattern2D
        {
            View = view,
            Source = new(1, 0, 2, 1),
            PatternToLocal = Matrix3x2.CreateTranslation(0.5f, 0),
            TileX = ImageTileMode2D.Repeat,
            TileY = ImageTileMode2D.Clamp,
            Filter = ImageFilter2D.Linear,
        };
        Vector4[] pixels = await RenderAsync(
            device,
            6,
            3,
            canvas =>
        {
            canvas.FillRect(new(0, 0, 6, 1), PaintGraph2D.ImagePattern(repeating));
            canvas.FillRect(new(0, 1, 6, 1), PaintGraph2D.ImagePattern(repeating with { TileX = ImageTileMode2D.Decal }));
            canvas.DrawImage(new(view, sampler), new(1, 0, 2, 1), new(0, 2, 4, 1));
        },
            [atlas]);
        for (int x = 0; x < 6; x++)
        {
            AssertPixel(pixels, 6, x, 0, new(0.5f, 0, 0, 0.5f));
        }

        AssertPixel(pixels, 6, 4, 1, Vector4.Zero);
        AssertPixel(pixels, 6, 1, 2, new(0.75f, 0, 0, 0.75f), 0.02f);
        AssertPixel(pixels, 6, 2, 2, new(0.25f, 0, 0, 0.25f), 0.02f);

        Vector4[] mirrored = await RenderAsync(
            device,
            6,
            1,
            canvas => canvas.FillRect(new(0, 0, 6, 1), PaintGraph2D.ImagePattern(repeating with
            {
                PatternToLocal = Matrix3x2.CreateTranslation(2, 0),
                TileX = ImageTileMode2D.Mirror,
                Filter = ImageFilter2D.Nearest,
            })),
            [atlas]);
        AssertPixel(mirrored, 6, 0, 0, Vector4.Zero);
        AssertPixel(mirrored, 6, 1, 0, new(1, 0, 0, 1));
        AssertPixel(mirrored, 6, 2, 0, new(1, 0, 0, 1));
        AssertPixel(mirrored, 6, 3, 0, Vector4.Zero);
        AssertPixel(mirrored, 6, 4, 0, Vector4.Zero);
        AssertPixel(mirrored, 6, 5, 0, new(1, 0, 0, 1));
    }

    private static async Task CheckGlyphRepresentationsAsync(IGraphicDevice device)
    {
        using IGraphicsTexture coverage = device.CreateTexture(new() { Width = 3, Height = 1, Format = TextureFormat.R8Unorm, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination });
        using IGraphicsTextureView coverageView = coverage.CreateView();
        using IGraphicsTexture msdf = device.CreateTexture(new() { Width = 2, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination });
        using IGraphicsTextureView msdfView = msdf.CreateView();
        using IGraphicsSampler sampler = device.CreateSampler(new() { MinFilter = FilterMode.Nearest, MagFilter = FilterMode.Nearest, MipmapFilter = FilterMode.Nearest, AddressU = AddressMode.ClampToEdge, AddressV = AddressMode.ClampToEdge });
        await UploadAsync(device, coverage, [0, 128, 255]);

        // Deliberately oppose alpha to the RGB median to distinguish the two stored distances.
        await UploadAsync(device, msdf, [0, 255, 255, 0, 255, 0, 0, 255]);
        Vector4[] pixels = await RenderAsync(
            device,
            3,
            4,
            canvas =>
        {
            canvas.DrawCoverageMask(new(coverageView, sampler), new(0, 0, 3, 1), new(0, 0, 3, 1), PaintGraph2D.Solid(_red));
            canvas.DrawDistanceField(new(coverageView, sampler, DistanceFieldEncoding2D.Sdf, 4), new(0, 0, 3, 1), new(0, 1, 3, 1), PaintGraph2D.Solid(_white));
            canvas.DrawDistanceField(new(msdfView, sampler, DistanceFieldEncoding2D.Msdf, 4), new(0, 0, 2, 1), new(0, 2, 2, 1), PaintGraph2D.Solid(_white));
            canvas.DrawDistanceField(new(msdfView, sampler, DistanceFieldEncoding2D.Mtsdf, 4), new(0, 0, 2, 1), new(0, 3, 2, 1), PaintGraph2D.Solid(_white));
        },
            [coverage, msdf]);
        AssertPixel(pixels, 3, 0, 0, Vector4.Zero);
        AssertPixel(pixels, 3, 1, 0, new(128f / 255, 0, 0, 128f / 255));
        AssertPixel(pixels, 3, 2, 0, new(1, 0, 0, 1));
        AssertPixel(pixels, 3, 0, 1, Vector4.Zero);
        AssertPixel(pixels, 3, 2, 1, Vector4.One);
        AssertPixel(pixels, 3, 0, 2, Vector4.One);
        AssertPixel(pixels, 3, 1, 2, Vector4.Zero);
        AssertPixel(pixels, 3, 0, 3, Vector4.One);
        AssertPixel(pixels, 3, 1, 3, Vector4.Zero);
    }

    private static async Task CheckLayersAsync(IGraphicDevice device)
    {
        Vector4[] opacity = await RenderAsync(device, 6, 2, canvas =>
        {
            canvas.BeginLayer(new() { ContentBounds = new(0, 0, 6, 2), Opacity = 0.5f });
            canvas.FillRect(new(0, 0, 4, 2), _red);
            canvas.FillRect(new(2, 0, 4, 2), _blue);
            canvas.EndLayer();
        });
        AssertPixel(opacity, 6, 0, 0, new(0.5f, 0, 0, 0.5f));
        AssertPixel(opacity, 6, 3, 0, new(0, 0, 0.5f, 0.5f));

        Vector4[] translated = await RenderAsync(device, 8, 4, canvas =>
        {
            canvas.ClipRect(new(5, 0, 1, 4));
            canvas.BeginLayer(new() { ContentBounds = new(1, 1, 2, 2), Effects = graph => graph.AppendComposite(graph.Source, graph.AppendOpacity(graph.Source, 0), EffectCompositeMode.SrcOver, 4, 0) });
            canvas.FillRect(new(1, 1, 2, 2), _white);
            canvas.EndLayer();
        });
        AssertPixel(translated, 8, 1, 1, Vector4.Zero);
        AssertPixel(translated, 8, 5, 1, Vector4.One);
        AssertPixel(translated, 8, 6, 1, Vector4.Zero);

        Vector4[] flattenedClips = await RenderAsync(device, 4, 2, canvas =>
        {
            canvas.ClipRect(new(0.5f, 0, 3, 2));
            canvas.BeginLayer(new() { ContentBounds = new(0.5f, 0, 3, 2) });
            canvas.FillRect(new(0, 0, 4, 2), _white);
            canvas.EndLayer();
        });
        AssertPixel(flattenedClips, 4, 0, 0, new(0.25f));
        AssertPixel(flattenedClips, 4, 1, 0, Vector4.One);
    }

    private static async Task CheckEffectsAsync(IGraphicDevice device)
    {
        Vector4[] blur = await RenderAsync(device, 12, 6, canvas =>
        {
            canvas.BeginLayer(new() { ContentBounds = new(4, 1, 4, 4), Effects = graph => graph.AppendGaussianBlur(graph.Source, 1, 0) });
            canvas.FillRect(new(4, 1, 4, 4), _white);
            canvas.EndLayer();
        });

        // The normalized radius-three Gaussian has weights .39905028, .24203623, .05400558, .00443305.
        AssertPixel(blur, 12, 3, 2, new(0.30047486f), 0.003f);
        AssertPixel(blur, 12, 2, 2, new(0.05843863f), 0.003f);
        float mass = Enumerable.Range(0, 12).Sum(x => blur[(2 * 12) + x].W);
        Assert.InRange(mass, 3.98f, 4.02f);
        AssertPixel(blur, 12, 5, 0, Vector4.Zero);

        var hdrMatrix = new ColorMatrix4x5(
        [
            0, 0, 0, 0, 2,
            0, 0, 0, 0, -0.25f,
            0, 0, 0, 0, 0,
            0, 0, 0, 0, 0.5f,
        ]);
        Vector4[] matrix = await RenderAsync(device, 4, 2, canvas =>
        {
            canvas.BeginLayer(new() { ContentBounds = new(1, 0, 2, 2), Effects = graph => graph.AppendColorMatrix(graph.Source, hdrMatrix) });
            canvas.FillRect(new(1, 0, 2, 2), _white);
            canvas.EndLayer();
        });
        AssertPixel(matrix, 4, 1, 0, new(1, -0.125f, 0, 0.5f));
        AssertPixel(matrix, 4, 0, 0, Vector4.Zero);

        Vector4[] plus = await RenderAsync(device, 2, 2, canvas =>
        {
            canvas.BeginLayer(new()
            {
                ContentBounds = new(0, 0, 2, 2),
                Effects = graph =>
                {
                    EffectImage part = graph.AppendOpacity(graph.Source, 0.75f);
                    return graph.AppendComposite(part, part, EffectCompositeMode.Plus);
                },
            });
            canvas.FillRect(new(0, 0, 2, 2), _white);
            canvas.EndLayer();
        });
        AssertPixel(plus, 2, 0, 0, new(1.5f, 1.5f, 1.5f, 1));
    }

    private static async Task CheckBackdropAsync(IGraphicDevice device)
    {
        Vector4[] translucent = await RenderAsync(device, 4, 2, canvas =>
        {
            canvas.FillRect(new(0, 0, 4, 2), new Color2D(1, 0, 0, 0.5f));
            canvas.DrawBackdropEffect(new() { OutputBounds = new(1.5f, 0, 2.5f, 2), Effects = graph => graph.AppendOpacity(graph.Source, 0.5f) });
            canvas.FillRect(new(3, 0, 1, 2), _blue);
        });
        AssertPixel(translucent, 4, 0, 0, new(0.5f, 0, 0, 0.5f));
        AssertPixel(translucent, 4, 1, 0, new(0.375f, 0, 0, 0.375f));
        AssertPixel(translucent, 4, 2, 0, new(0.25f, 0, 0, 0.25f));
        AssertPixel(translucent, 4, 3, 0, new(0, 0, 1, 1));

        Vector4[] blur = await RenderAsync(device, 5, 1, canvas =>
        {
            canvas.FillRect(new(0, 0, 1, 1), _white);
            canvas.DrawBackdropEffect(new() { OutputBounds = new(1, 0, 3, 1), Effects = graph => graph.AppendGaussianBlur(graph.Source, 1, 0) });
        });
        AssertPixel(blur, 5, 0, 0, Vector4.One);
        AssertPixel(blur, 5, 1, 0, new(0.24203623f), 0.003f);
        AssertPixel(blur, 5, 2, 0, new(0.05400558f), 0.003f);
        AssertPixel(blur, 5, 4, 0, Vector4.Zero);
    }

    private static async Task CheckComposedEffectsAsync(IGraphicDevice device)
    {
        Vector4[] layer = await RenderAsync(device, 8, 4, canvas =>
        {
            canvas.ClipRect(new(5, 0, 1, 4));
            canvas.BeginLayer(new() { ContentBounds = new(1, 1, 2, 2), Effects = graph => graph.AppendOpacity(graph.AppendEffect(new TranslateLayerEffect(4, 0), graph.Source), 0.5f) });
            canvas.FillRect(new(1, 1, 2, 2), _white);
            canvas.EndLayer();
        });
        AssertPixel(layer, 8, 1, 1, Vector4.Zero);
        AssertPixel(layer, 8, 5, 1, new(0.5f));
        AssertPixel(layer, 8, 6, 1, Vector4.Zero);

        Vector4[] backdrop = await RenderAsync(device, 5, 1, canvas =>
        {
            canvas.FillRect(new(0, 0, 5, 1), _blue);
            canvas.FillRect(new(0, 0, 1, 1), _red);
            canvas.DrawBackdropEffect(new() { OutputBounds = new(3, 0, 1, 1), Effects = graph => graph.AppendEffect(new TranslateLayerEffect(3, 0), graph.Source) });
        });
        AssertPixel(backdrop, 5, 0, 0, new(1, 0, 0, 1));
        AssertPixel(backdrop, 5, 2, 0, new(0, 0, 1, 1));
        AssertPixel(backdrop, 5, 3, 0, new(1, 0, 0, 1));
        AssertPixel(backdrop, 5, 4, 0, new(0, 0, 1, 1));
    }

    private static async Task<Vector4[]> RenderAsync(IGraphicDevice device, int width, int height, Action<RenderGraphCanvas2D> draw, IGraphicsTexture[]? inputs = null, float pixelScale = 1)
    {
        var programs = new ExternalDrawingPrograms();
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        draw(canvas);
        RenderGraphDrawList2D list = canvas.End();
        var graph = new Graph(device, "drawing-2d-readback");
        var bindings = new DrawingGraphBindings2D(graph);
        foreach (IGraphicsTexture input in inputs ?? [])
        {
            bindings.BindTexture(input, graph.ImportTexture(input, TextureState.Sampled, _sampleRead));
        }

        var description = new TextureDesc
        {
            Width = checked((uint)width),
            Height = checked((uint)height),
            Format = TextureFormat.Rgba16Float,
            Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled | TextureUsage.CopySource,
        };
        RenderGraphTexture target = graph.CreateTexture(description, "canvas");
        using var renderer = new RenderGraphRenderer2D(DrawingCommandRenderers2D.CreateDefault(), list, new() { Texture = target, Description = description, LoadOp = AttachmentLoadOp.Clear, PixelScale = pixelScale }, bindings, programs);
        PreparedDrawingPasses2D drawing = await graph.Append2D(renderer);
        TextureCopyLayout layout = device.GetTextureCopyLayout(description.Format);
        uint rowBytes = checked((uint)width * layout.BytesPerTexel);
        uint pitch = checked(((rowBytes + layout.BytesPerRowAlignment - 1) / layout.BytesPerRowAlignment) * layout.BytesPerRowAlignment);
        uint byteCount = checked(pitch * (uint)height);
        RenderGraphBuffer<byte> result = graph.CreateBuffer<byte>(new() { Count = byteCount, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback }, "readback");
        graph.AddPass("readback", context => context.Commands.CopyTextureToBuffer(
            new() { Texture = context.GetTexture(target), Width = checked((uint)width), Height = checked((uint)height) },
            new() { Buffer = context.GetBuffer(result).Slice(0, byteCount), BytesPerRow = pitch, RowsPerImage = checked((uint)height) }))
            .UseTexture(target, TextureState.CopySource, _copyRead).UseBuffer(result, _copyWrite);
        graph.ExportBuffer(result, new(PipelineStage.Host, ResourceAccess.HostRead));
        CompiledRenderGraph? plan = null;
        IGraphicsCommandBuffer? commands = null;
        IGraphicsSubmission? submission = null;
        try
        {
            plan = graph.Compile();
            commands = device.CreateCommandBuffer(new());
            plan.Record(commands);
            commands.Finish();
            submission = device.Queue.Submit([commands]);
            await submission.WaitAsync();
            IGraphicsBuffer<byte> readback = plan.GetBuffer(result);
            await readback.MapAsync();
            byte[] bytes = new byte[byteCount];
            readback.CopyTo(bytes);
            readback.Unmap();
            var pixels = new Vector4[checked(width * height)];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int offset = checked((int)((y * pitch) + (x * 8)));
                    pixels[(y * width) + x] = new(ReadHalf(bytes, offset), ReadHalf(bytes, offset + 2), ReadHalf(bytes, offset + 4), ReadHalf(bytes, offset + 6));
                }
            }

            return pixels;
        }
        finally
        {
            commands?.Dispose();
            plan?.Dispose();
            Assert.True(programs.Acquired > 0);
            Assert.Equal(0, programs.Returned);
            drawing.Dispose();
            Assert.Equal(programs.Acquired, programs.Returned);
            submission?.Dispose();
        }
    }

    private static async Task UploadAsync(IGraphicDevice device, IGraphicsTexture texture, byte[] texels)
    {
        TextureCopyLayout layout = device.GetTextureCopyLayout(texture.Format);
        uint rowBytes = checked(texture.Width * layout.BytesPerTexel);
        uint pitch = checked(((rowBytes + layout.BytesPerRowAlignment - 1) / layout.BytesPerRowAlignment) * layout.BytesPerRowAlignment);
        byte[] bytes = new byte[checked(pitch * texture.Height)];
        Assert.Equal(checked((int)(rowBytes * texture.Height)), texels.Length);
        for (uint row = 0; row < texture.Height; row++)
        {
            texels.AsSpan(checked((int)(row * rowBytes)), checked((int)rowBytes)).CopyTo(bytes.AsSpan(checked((int)(row * pitch))));
        }

        using IGraphicsBuffer<byte> upload = device.CreateBuffer<byte>(new() { Count = (ulong)bytes.Length, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        await upload.MapAsync();
        upload.CopyFrom(bytes);
        upload.Unmap();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = upload.Slice(0, upload.Count), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = _copyRead });
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.CopyDestination, Before = default, After = _copyWrite });
        commands.CopyBufferToTexture(new() { Buffer = upload.Slice(0, upload.Count), BytesPerRow = pitch, RowsPerImage = texture.Height }, new() { Texture = texture, Width = texture.Width, Height = texture.Height });
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.CopyDestination, AfterState = TextureState.Sampled, Before = _copyWrite, After = _sampleRead });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        commands.Dispose();
    }

    private static Path2D Rectangle(float x, float y, float width, float height)
    {
        var builder = new PathBuilder2D();
        AddRectangle(builder, x, y, width, height);
        return builder.Build();
    }

    private static void AddRectangle(PathBuilder2D builder, float x, float y, float width, float height, bool reverse = false)
    {
        builder.MoveTo(new(x, y));
        if (reverse)
        {
            builder.LineTo(new(x, y + height));
            builder.LineTo(new(x + width, y + height));
            builder.LineTo(new(x + width, y));
        }
        else
        {
            builder.LineTo(new(x + width, y));
            builder.LineTo(new(x + width, y + height));
            builder.LineTo(new(x, y + height));
        }

        builder.Close();
    }

    private static float ReadHalf(byte[] bytes, int offset) => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2)));

    private static void AssertPixel(Vector4[] pixels, int width, int x, int y, Vector4 expected, float tolerance = 0.002f)
    {
        Vector4 actual = pixels[(y * width) + x];
        var difference = Vector4.Abs(actual - expected);
        Assert.True(difference.X <= tolerance && difference.Y <= tolerance && difference.Z <= tolerance && difference.W <= tolerance, $"Pixel ({x}, {y}): expected {expected}, actual {actual}, tolerance {tolerance}.");
    }

    private sealed class ExternalDrawingPrograms : IGraphicsProgramProvider
    {
        private readonly ShaderArtifact _vertex = new(ShaderArtifact.LoadEmbedded(typeof(DrawingPrograms2D).Assembly, "Lumyte.Drawing2D.drawing-vertex.lshader").GetBinary());
        private readonly ShaderArtifact _fragment = new(ShaderArtifact.LoadEmbedded(typeof(DrawingPrograms2D).Assembly, "Lumyte.Drawing2D.drawing-fragment.lshader").GetBinary());

        internal int Acquired { get; private set; }

        internal int Returned { get; private set; }

        public GraphicsProgramLease Acquire(IGraphicDevice device, string programId)
        {
            GraphicsProgramLease native = programId switch
            {
                DrawingPrograms2D.ProgramId => GraphicsProgramLease.Create(device, _vertex, _fragment),
                _ => throw new NotSupportedException("Unknown drawing program ABI."),
            };
            Acquired++;
            return new(native.Resources, () =>
            {
                Returned++;
                native.Dispose();
            });
        }
    }

    private sealed record TranslateLayerEffect(int X, int Y) : ImageEffectDefinition
    {
        /// <inheritdoc/>
        public override int InputCount => 1;

        /// <inheritdoc/>
        public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs) => inputs[0].Offset(X, Y);

        /// <inheritdoc/>
        public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output) => output.OffsetInverse(X, Y);

        /// <inheritdoc/>
        public override EffectImage AppendPasses(EffectPreparationContext context, Graph plan, ReadOnlySpan<EffectImage> inputs) => inputs[0] with { Bounds = inputs[0].Bounds.Offset(X, Y) };
    }
}
