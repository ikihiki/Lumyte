using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Lumyte.Mathematics.Colors;

namespace Lumyte.Graphics.Tests;

/// <summary>Full-surface mixed-kind verification through the ordinary public renderer.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Checks every output pixel for mixed sparse Vulkan commands.</summary>
    /// <returns>The completed comparison.</returns>
    [GpuFact]
    public async Task VulkanSparseMixedElevenKindsFullPixelsAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckSparseMixedElevenKindsFullPixelsAsync(device);
    }

    /// <summary>Checks every output pixel for mixed sparse native wgpu commands.</summary>
    /// <returns>The completed comparison.</returns>
    [GpuFact]
    public async Task WgpuSparseMixedElevenKindsFullPixelsAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckSparseMixedElevenKindsFullPixelsAsync(device);
    }

    private static async Task CheckSparseMixedElevenKindsFullPixelsAsync(IGraphicDevice device)
    {
        using IGraphicsTexture image = device.CreateTexture(new() { Width = 2, Height = 1, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination });
        using IGraphicsTextureView imageView = image.CreateView();
        using IGraphicsTexture coverage = device.CreateTexture(new() { Width = 2, Height = 1, Format = TextureFormat.R8Unorm, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination });
        using IGraphicsTextureView coverageView = coverage.CreateView();
        using IGraphicsSampler sampler = device.CreateSampler(new() { MinFilter = FilterMode.Nearest, MagFilter = FilterMode.Nearest });
        await UploadAsync(device, image, [255, 0, 0, 255, 0, 0, 255, 128]);
        await UploadAsync(device, coverage, [64, 255]);
        var canvas = new Canvas2D();
        var paint = PaintGraph2D.LinearGradient(new(0, 0), new(32, 0), [new(0, new SRgba<float>(1, 0, 0, 0.5f)), new(1, new SRgba<float>(0, 0, 1, 1))]);
        canvas.Begin();
        canvas.ClipRect(new(0.375f, 0.125f, 30, 15));
        canvas.FillRect(new(0.125f, 0.375f, 10, 5), paint);
        canvas.FillRoundedRect(new(10, 0, 8, 6), 2, paint);
        canvas.FillCircle(new(22, 3), 3, paint);
        canvas.DrawLine(new(0, 7), new(8, 7), paint, new() { Width = 2 });
        var builder = new PathBuilder2D();
        builder.MoveTo(new(10, 7));
        builder.LineTo(new(16, 7));
        builder.LineTo(new(10, 12));
        builder.Close();
        Path2D path = builder.Build();
        canvas.FillPath(path, paint);
        canvas.StrokePath(path, paint, new() { Width = 1, Join = LineJoin2D.Bevel });
        canvas.DrawImage(new(imageView, sampler), new(0, 0, 2, 1), new(18, 7, 6, 3));
        canvas.DrawCoverageMask(new(coverageView, sampler), new(0, 0, 2, 1), new(24, 7, 6, 3), paint);
        canvas.DrawDistanceField(new(coverageView, sampler, DistanceFieldEncoding2D.Sdf, 4), new(0, 0, 2, 1), new(18, 11, 6, 3), paint);
        canvas.DrawMesh(new([new(new(0, 10), 0.25f), new(new(8, 10), 1), new(new(0, 15), 0.5f)], [0, 1, 2]), paint);
        canvas.DrawPaintGraph(PaintGraph2D.Opacity(0.5f, PaintGraph2D.Solid(new SRgba<float>(0, 1, 0, 0.1f))));
        DrawList2D list = canvas.End();
        using IRenderer2D legacy = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using IRenderer2D sparse = CreateSparseOracleRenderer(device);
        Vector4[]? expected = null;
        foreach (IRenderer2D renderer in new[] { legacy, sparse })
        {
            using IGraphicsTexture target = device.CreateTexture(new() { Width = 32, Height = 16, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
            using IGraphicsTextureView view = target.CreateView();
            await InitializeOracleBackgroundAsync(device, target, view);
            using PreparedDraw2D prepared = await renderer.PrepareAsync(list, new() { View = view, LoadOp = AttachmentLoadOp.Load });
            Vector4[] pixels = await ReadOracleDirectAsync(device, target, commands =>
            {
                var observed = new PartitionCommands(commands);
                prepared.Record(observed);
                Xunit.Assert.Equal(0, observed.ComputePasses);
            });
            if (expected is null)
            {
                expected = pixels;
                continue;
            }

            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    AssertPixel(pixels, 32, x, y, expected[(y * 32) + x]);
                }
            }
        }
    }
}
