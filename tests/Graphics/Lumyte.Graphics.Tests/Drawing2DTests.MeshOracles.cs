using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.Vulkan;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Independent analytic sample and resource oracles for actual Mesh coverage dispatches.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>The independent derivative oracle remains 0x0FF0 under a nonuniform stroked quadratic.</summary>
    /// <returns>The native full-pixel legacy comparison plus independent exact sample checks.</returns>
    [MeshGpuFact]
    public async Task VulkanMeshIndependentArchAndCorrelatedSamplesAsync()
    {
        using var device = VulkanDevice.Create();
        Assert.Equal(0x0FF0U, IndependentMeshArchMask(32, 8));
        void Arch(RenderGraphCanvas2D canvas)
        {
            canvas.ConcatTransform(Matrix3x2.CreateScale(16, 0.25f) * Matrix3x2.CreateTranslation(64, 8));
            var path = new PathBuilder2D();
            path.MoveTo(new(-1, 0));
            path.QuadraticTo(new(0, 1), new(1, 0));
            canvas.StrokePath(path.Build(), _white, new() { Width = 6, Cap = LineCap2D.Butt, Join = LineJoin2D.Bevel });
        }

        Vector4[] reference = await RenderAsync(device, 128, 16, Arch);
        PartitionCommands? observed = null;
        Vector4[] actual = await RenderAsync(device, 128, 16, Arch, meshCoverage: true, observe: native => observed = new(native));
        Assert.Equal(reference, actual);
        AssertPixel(actual, 128, 32, 8, new(0.5f));
        Assert.NotNull(observed);
        Assert.True(observed.MeshDispatches > 0);
        Console.WriteLine($"meshArch mask=0x0FF0 dispatches={observed.MeshDispatches} pixels={actual.Length}");
        await CheckNormalizedClipSamplesAsync(device, meshCoverage: true);
        await CheckSingularFoldedClipPaintAsync(device, meshCoverage: true);

        float a = MathF.BitIncrement(1f);
        float c = MathF.BitIncrement(a);
        Vector2 start = new(-0.875f, 0.125f - a);
        Vector2[] points = [start, start + new Vector2(a, c), start + new Vector2(0, 2)];
        Assert.Equal(1U, IndependentMeshFmaMask(points));
        var boundary = new PathBuilder2D();
        boundary.MoveTo(points[0]);
        boundary.LineTo(points[1]);
        boundary.LineTo(points[2]);
        boundary.Close();
        Vector4[] fma = await RenderAsync(
            device,
            1,
            1,
            canvas =>
        {
            canvas.ClipPath(boundary.Build());
            canvas.FillRect(new(-100, -100, 200, 200), _white);
        },
            meshCoverage: true);

        // Double FMA winding admits the first lattice point; rounded float cross products do not.
        Assert.Equal(new Vector4(1f / 16), Assert.Single(fma));
        Console.WriteLine("meshFmaBoundary expectedMask=0x0001 independentOrderedFma=true");
    }

    /// <summary>Checks atlas crop, mask/SDF/MSDF/MTSDF, gradients and explicit legacy mesh centers.</summary>
    /// <returns>The full-pixel and independent center-color checks.</returns>
    [MeshGpuFact]
    public async Task VulkanMeshIndependentImagesGradientsAndLegacyCentersAsync()
    {
        using var device = VulkanDevice.Create();
        using IGraphicsTexture atlas = device.CreateTexture(new() { Width = 4, Height = 1, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination });
        using IGraphicsTextureView atlasView = atlas.CreateView();
        using IGraphicsTexture coverage = device.CreateTexture(new() { Width = 3, Height = 1, Format = TextureFormat.R8Unorm, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination });
        using IGraphicsTextureView coverageView = coverage.CreateView();
        using IGraphicsTexture multichannel = device.CreateTexture(new() { Width = 2, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination });
        using IGraphicsTextureView multichannelView = multichannel.CreateView();
        using IGraphicsSampler sampler = device.CreateSampler(new() { MinFilter = FilterMode.Nearest, MagFilter = FilterMode.Nearest, MipmapFilter = FilterMode.Nearest, AddressU = AddressMode.ClampToEdge, AddressV = AddressMode.ClampToEdge });
        await UploadAsync(device, atlas, [0, 255, 0, 255, 255, 0, 0, 255, 0, 0, 255, 128, 0, 255, 0, 255]);
        await UploadAsync(device, coverage, [0, 128, 255]);
        await UploadAsync(device, multichannel, [0, 255, 255, 0, 255, 0, 0, 255]);
        void Images(RenderGraphCanvas2D canvas)
        {
            canvas.DrawImage(new(atlasView, sampler), new(1, 0, 2, 1), new(2, 2, 8, 4));
            canvas.DrawCoverageMask(new(coverageView, sampler), new(0, 0, 3, 1), new(2, 8, 12, 4), PaintGraph2D.Solid(_red));
            canvas.DrawDistanceField(new(coverageView, sampler, DistanceFieldEncoding2D.Sdf, 4), new(0, 0, 3, 1), new(2, 14, 12, 4), PaintGraph2D.Solid(_white));
            canvas.DrawDistanceField(new(multichannelView, sampler, DistanceFieldEncoding2D.Msdf, 4), new(0, 0, 2, 1), new(2, 20, 8, 4), PaintGraph2D.Solid(_white));
            canvas.DrawDistanceField(new(multichannelView, sampler, DistanceFieldEncoding2D.Mtsdf, 4), new(0, 0, 2, 1), new(2, 26, 8, 4), PaintGraph2D.Solid(_white));
        }

        Vector4[] expected = await RenderAsync(device, 24, 32, Images, [atlas, coverage, multichannel]);
        Vector4[] pixels = await RenderAsync(device, 24, 32, Images, [atlas, coverage, multichannel], meshCoverage: true);
        Assert.Equal(expected, pixels);
        AssertPixel(pixels, 24, 3, 3, new(1, 0, 0, 1));
        AssertPixel(pixels, 24, 8, 3, new(0, 0, 128f / 255, 128f / 255));
        AssertPixel(pixels, 24, 7, 9, new(128f / 255, 0, 0, 128f / 255));
        AssertPixel(pixels, 24, 11, 15, Vector4.One);
        foreach (int y in new[] { 21, 27 })
        {
            AssertPixel(pixels, 24, 3, y, Vector4.One);
            AssertPixel(pixels, 24, 8, y, Vector4.Zero);
        }

        ColorStop2D[] stops = [new(0, new(0, 0, 1, 0)), new(1, _red)];
        void Gradients(RenderGraphCanvas2D canvas)
        {
            canvas.FillRect(new(2, 2, 16, 4), PaintGraph2D.LinearGradient(new(2, 0), new(18, 0), stops));
            canvas.DrawMesh(new([new(new(2, 16), 0.25f), new(new(18, 16), 0.75f), new(new(2, 24), 0.25f)], [0, 1, 2]), PaintGraph2D.Solid(_white));
            canvas.FillCircle(new(24, 8), 4, new SRgba<float>(0, 1, 0, 0.5f));
        }

        expected = await RenderAsync(device, 32, 24, Gradients);
        pixels = await RenderAsync(device, 32, 24, Gradients, meshCoverage: true);
        Assert.Equal(expected, pixels);
        AssertPixel(pixels, 32, 5, 3, new(0.21875f, 0, 0, 0.21875f));
        AssertPixel(pixels, 32, 13, 3, new(0.71875f, 0, 0, 0.71875f));
        AssertPixel(pixels, 32, 5, 18, new(0.359375f));
        AssertPixel(pixels, 32, 9, 17, new(0.484375f));
    }

    /// <summary>A previously submitted background survives Load with Standard indexed geometry between Mesh batches.</summary>
    /// <returns>The exact legacy full-frame comparison and actual dispatch evidence.</returns>
    [MeshGpuFact]
    public async Task VulkanMeshLoadBackgroundMixedStandardOrderAsync()
    {
        using var device = VulkanDevice.Create();
        using var slot = new SharedArgumentFrame(device);
        using (IGraphicsCommandBuffer clearCommands = device.CreateCommandBuffer(new()))
        {
            clearCommands.Barrier(new TextureBarrierDesc { Texture = slot.Texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite) });
            IRenderEncoder clear = clearCommands.BeginRenderPass(new() { ColorAttachments = [new() { View = slot.View, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearValue = new(0, 0, 1, 1) }] });
            clear.End();
            clearCommands.Finish();
            using IGraphicsSubmission background = device.Queue.Submit([clearCommands]);
            await background.WaitAsync();
        }

        var mesh = new Mesh2D([new(new(2, 2), 0.5f), new(new(7, 2), 0.5f), new(new(2, 7), 0.5f)], [0, 1, 2]);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(1, 1, 6, 4), new SRgba<float>(1, 0, 0, 0.5f));
        canvas.DrawMesh(mesh, PaintGraph2D.Solid(_white));
        canvas.FillRoundedRect(new(3, 3, 4, 4), 1, new SRgba<float>(0, 1, 0, 0.25f));
        DrawList2D content = canvas.End();
        using Renderer2D renderer = CreateMeshResidentRenderer(device);
        using PreparedDraw2D prepared = await renderer.PrepareAsync(content, new() { View = slot.View, LoadOp = AttachmentLoadOp.Load, ClearColor = _red });
        renderer.Dispose();
        var observed = new PartitionCommands(slot.Commands);
        prepared.Record(observed);
        ResidentTargetReadback(observed, slot);
        observed.Finish();
        using IGraphicsSubmission submitted = device.Queue.Submit([slot.Commands]);
        await submitted.WaitAsync();
        slot.Commands.Dispose();
        Vector4[] actual = await DecodeOraclePixelsAsync(slot.Readback, 8, 8, slot.Pitch);
        var referenceCanvas = new Canvas2D();
        referenceCanvas.Begin();
        referenceCanvas.FillRect(new(0, 0, 8, 8), _blue);
        referenceCanvas.Replay(content);
        Vector4[] expected = await ReadResidentDirectAsync(device, referenceCanvas.End());
        Assert.Equal(expected, actual);
        AssertPixel(actual, 8, 0, 0, new(0, 0, 1, 1));
        Assert.True(observed.MeshDispatches >= 2);
        Console.WriteLine($"meshLoadBackground actualMeshDispatches={observed.MeshDispatches} standardIndexedBetween=true pixels={actual.Length}");
    }

    private static uint IndependentMeshFmaMask(Vector2[] points)
    {
        uint mask = 0;
        for (int bit = 0; bit < 16; bit++)
        {
            double px = ((bit % 4) + 0.5) / 4;
            double py = ((bit / 4) + 0.5) / 4;
            int winding = 0;
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[(i + 1) % points.Length];
                double side = Math.FusedMultiplyAdd((double)b.X - a.X, py - a.Y, -Math.FusedMultiplyAdd((double)b.Y - a.Y, px - a.X, 0));
                if (a.Y <= py && b.Y > py && side > 0)
                {
                    winding++;
                }

                if (b.Y <= py && a.Y > py && side < 0)
                {
                    winding--;
                }
            }

            if (winding != 0)
            {
                mask |= 1U << bit;
            }
        }

        return mask;
    }

    private static uint IndependentMeshArchMask(int pixelX, int pixelY)
    {
        uint mask = 0;
        for (int j = 0; j < 4; j++)
        {
            for (int i = 0; i < 4; i++)
            {
                double x = (pixelX + ((i + 0.5) / 4) - 64) / 16;
                double y = (pixelY + ((j + 0.5) / 4) - 8) / 0.25;

                // Analytic distance derivative, independent of product flattening.
                for (int interval = 0; interval < 64; interval++)
                {
                    double lo = interval / 64.0;
                    double hi = (interval + 1) / 64.0;
                    if (Derivative(lo) >= 0 || Derivative(hi) <= 0)
                    {
                        continue;
                    }

                    for (int iteration = 0; iteration < 60; iteration++)
                    {
                        double middle = (lo + hi) * 0.5;
                        if (Derivative(middle) < 0)
                        {
                            lo = middle;
                        }
                        else
                        {
                            hi = middle;
                        }
                    }

                    double t = (lo + hi) * 0.5;
                    double dx = (2 * t) - 1 - x;
                    double dy = (2 * t * (1 - t)) - y;
                    if ((dx * dx) + (dy * dy) < 9)
                    {
                        mask |= 1U << ((j * 4) + i);
                    }
                }

                double Derivative(double t) => (((2 * t) - 1 - x) * 2) + (((2 * t * (1 - t)) - y) * (2 - (4 * t)));
            }
        }

        return mask;
    }
}
