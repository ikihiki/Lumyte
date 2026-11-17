using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>All eleven actual command kinds, mixed declaration choices, load background and painter order.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Compares every pixel and independent interior values on Vulkan.</summary>
    /// <returns>The completed native proof.</returns>
    [GpuFact]
    public async Task VulkanMembershipAllElevenKindsAndMixedOrderAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckMembershipAllKindsAsync(device);
    }

    /// <summary>Compares every pixel and independent interior values on wgpu.</summary>
    /// <returns>The completed native proof.</returns>
    [GpuFact]
    public async Task WgpuMembershipAllElevenKindsAndMixedOrderAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckMembershipAllKindsAsync(device);
    }

    private static async Task CheckMembershipAllKindsAsync(IGraphicDevice device)
    {
        using IGraphicsTexture image = device.CreateTexture(new() { Width = 2, Height = 1, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination });
        using IGraphicsTextureView imageView = image.CreateView();
        using IGraphicsTexture mask = device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.R8Unorm, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination });
        using IGraphicsTextureView maskView = mask.CreateView();
        using IGraphicsSampler sampler = device.CreateSampler(new() { MinFilter = FilterMode.Nearest, MagFilter = FilterMode.Nearest, MipmapFilter = FilterMode.Nearest });
        await UploadAsync(device, image, [255, 0, 0, 255, 255, 255, 255, 255]);
        await UploadAsync(device, mask, [128]);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 8, 8), _red);
        canvas.FillRoundedRect(new(14, 0, 8, 8), 2, new SRgba<float>(0, 1, 0, 1));
        canvas.FillCircle(new(32, 4), 4, _blue);
        canvas.DrawLine(new(44, 2), new(50, 6), PaintGraph2D.Solid(_white), new() { Width = 2, Cap = LineCap2D.Round });
        canvas.FillPath(KindTriangle(56, 0, 8), new SRgba<float>(1, 0, 1, 1));
        canvas.StrokePath(KindTriangle(70, 0, 8), PaintGraph2D.Solid(new SRgba<float>(0, 1, 1, 1)), new() { Width = 1 });
        canvas.DrawImage(new(imageView, sampler), new(0, 0, 2, 1), new(84, 0, 8, 8));
        canvas.DrawCoverageMask(new(maskView, sampler), new(0, 0, 1, 1), new(98, 0, 8, 8), PaintGraph2D.Solid(_red));
        canvas.DrawDistanceField(new(maskView, sampler, DistanceFieldEncoding2D.Sdf, 4), new(0, 0, 1, 1), new(112, 0, 8, 8), PaintGraph2D.Solid(_white));
        canvas.DrawMesh(new([new(new(126, 0), 0.25f), new(new(134, 0), 0.75f), new(new(126, 8), 0.25f)], [0, 1, 2]), PaintGraph2D.Solid(_white));
        canvas.Save();
        canvas.ClipRect(new(140, 0, 8, 8));
        canvas.DrawPaintGraph(PaintGraph2D.Solid(new SRgba<float>(1, 1, 0, 1)));
        canvas.Restore();
        canvas.FillRect(new(140, 0, 8, 8), new SRgba<float>(0, 0, 1, 0.5f));
        canvas.FillPath(KindTriangle(146, 0, 2), _red);
        DrawList2D list = canvas.End();
        Vector4[]? control = null;
        for (int mode = 0; mode < 3; mode++)
        {
            using IRenderer2D renderer = mode == 0 ? new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault()) : CreateMembershipOracleRenderer(device, mixed: mode == 2);
            using IGraphicsTexture target = device.CreateTexture(new() { Width = 160, Height = 16, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
            using IGraphicsTextureView view = target.CreateView();
            await InitializeOracleBackgroundAsync(device, target, view);
            using PreparedDraw2D prepared = await renderer.PrepareAsync(list, new() { View = view, LoadOp = AttachmentLoadOp.Load });
            Vector4[] pixels = await ReadOracleDirectAsync(device, target, commands =>
            {
                var observed = new PartitionCommands(commands);
                prepared.Record(observed);
                Assert.Equal(mode == 0 ? 0 : 4, observed.ComputePasses);
                Assert.Equal(mode == 0 ? 0 : 4, observed.ComputeDispatches);
            });
            if (control is not null)
            {
                for (int y = 0; y < 16; y++)
                {
                    for (int x = 0; x < 160; x++)
                    {
                        AssertPixel(pixels, 160, x, y, control[(y * 160) + x]);
                    }
                }
            }
            else
            {
                control = pixels;
            }

            AssertPixel(pixels, 160, 2, 2, new(1, 0, 0, 1));
            AssertPixel(pixels, 160, 18, 4, new(0, 1, 0, 1));
            AssertPixel(pixels, 160, 32, 4, new(0, 0, 1, 1));
            AssertPixel(pixels, 160, 58, 2, new(1, 0, 1, 1));
            AssertPixel(pixels, 160, 86, 2, new(1, 0, 0, 1));
            AssertPixel(pixels, 160, 102, 2, new(128f / 255, 0, 1 - (128f / 255), 1));
            AssertPixel(pixels, 160, 128, 2, new(0.40625f, 0.40625f, 1, 1));
            AssertPixel(pixels, 160, 142, 2, new(0.5f, 0.5f, 0.5f, 1));
            AssertPixel(pixels, 160, 146, 0, new(1, 0, 0, 1));
            AssertPixel(pixels, 160, 155, 11, new(0, 0, 1, 1));
        }
    }

    private static Path2D KindTriangle(float x, float y, float size)
    {
        var path = new PathBuilder2D();
        path.MoveTo(new(x, y));
        path.LineTo(new(x + size, y));
        path.LineTo(new(x, y + size));
        path.Close();
        return path.Build();
    }
}
