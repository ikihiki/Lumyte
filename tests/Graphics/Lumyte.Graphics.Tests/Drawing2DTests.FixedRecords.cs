using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks fixed records through public rendering and independent pixel expectations.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Exercises every built-in command and compact paint family on the Vulkan backend.</summary>
    /// <returns>The completed hardware readbacks.</returns>
    [GpuFact]
    public async Task VulkanFixedRecordsAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckFixedRecordsAsync(device);
    }

    /// <summary>Exercises the identical format with ordered fallback draws on the WGSL backend.</summary>
    /// <returns>The completed hardware readbacks.</returns>
    [GpuFact]
    public async Task WgpuFixedRecordsAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckFixedRecordsAsync(device);
    }

    /// <summary>Changes only the public root snapshot and native instance count to select GPU-resident records.</summary>
    /// <returns>The independent first/middle/end/empty range readbacks.</returns>
    [GpuFact]
    public async Task VulkanFixedRecordSelectedInstancesAsync()
    {
        using var device = VulkanDevice.Create();
        foreach ((uint first, uint count) in new[] { (0u, 1u), (16u, 5u), (24u, 1u), (25u, 0u) })
        {
            Action<RenderGraphCanvas2D> draw = canvas =>
            {
                for (int i = 0; i < 25; i++)
                {
                    canvas.FillRect(new(i, 0, 1, 1), _white);
                }
            };
            Vector4[] pixels = await RenderAsync(device, 32, 1, draw, useDefaultResources: true, observe: native => new PartitionCommands(native, (first, count)));
            for (int x = 0; x < 32; x++)
            {
                AssertPixel(pixels, 32, x, 0, x >= first && (x - first) < count ? Vector4.One : Vector4.Zero);
            }
        }
    }

    private static async Task CheckFixedRecordsAsync(IGraphicDevice device)
    {
        Action<RenderGraphCanvas2D> boundaryDraw = canvas =>
        {
            for (int i = 0; i < 340; i++)
            {
                canvas.FillRect(new(0, 0, 8, 8), new Color2D(1, 0, 0, 0));
            }

            canvas.FillCircle(new(4, 4), 100, new Color2D(1, 0, 0, 0.5f));
            canvas.FillRect(new(0, 0, 8, 8), new Color2D(0, 0, 1, 0.5f));
        };
        Vector4[] boundary = await RenderAsync(device, 8, 8, boundaryDraw, useDefaultResources: true);
        Assert.All(boundary, pixel => Assert.InRange(Vector4.Distance(new(0.25f, 0, 0.5f, 0.75f), pixel), 0, 0.002f));

        using IGraphicsTexture image = device.CreateTexture(new() { Width = 2, Height = 2, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination });
        using IGraphicsTexture mask = device.CreateTexture(new() { Width = 2, Height = 2, Format = TextureFormat.R8Unorm, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination });
        await UploadAsync(device, image, [255, 64, 128, 255, 64, 255, 128, 255, 64, 128, 255, 255, 255, 255, 255, 255]);
        await UploadAsync(device, mask, [64, 128, 192, 255]);
        using IGraphicsTextureView imageView = image.CreateView();
        using IGraphicsTextureView maskView = mask.CreateView();
        using IGraphicsSampler sampler = device.CreateSampler(new() { MinFilter = FilterMode.Nearest, MagFilter = FilterMode.Nearest, MipmapFilter = FilterMode.Nearest, AddressU = AddressMode.ClampToEdge, AddressV = AddressMode.ClampToEdge, LodMaxClamp = 0 });
        Action<RenderGraphCanvas2D> invalidImageInverse = canvas =>
        {
            canvas.Scale(new Vector2(1e-10f));
            canvas.DrawPaintGraph(PaintGraph2D.ImagePattern(new() { View = imageView, Source = new(0, 0, 1, 1), PatternToLocal = Matrix3x2.CreateScale(1e-10f) }));
        };
        Vector4[] transparent = await RenderAsync(device, 2, 1, invalidImageInverse, [image], useDefaultResources: true);
        Assert.All(transparent, pixel => Assert.Equal(Vector4.Zero, pixel));
        Path2D triangle = RecordTriangle();
        var solid = PaintGraph2D.Solid(new(1, 0, 0, 0.5f));
        ColorStop2D[] stops = [new(0, new(1, 0, 0, 0.75f)), new(0.4f, new(0, 1, 0, 0.5f)), new(1, new(0, 0, 1, 1))];
        var linear = PaintGraph2D.LinearGradient(new(0, 0), new(12, 4), stops, ExtendMode2D.Reflect);
        List<PaintGraph2D> paints =
        [
            solid,
            linear,
            PaintGraph2D.RadialGradient(new(2, 2), 1, new(5, 4), 7, stops, ExtendMode2D.Repeat),
            PaintGraph2D.SweepGradient(new(4, 4), 0.2f, 5.5f, stops, ExtendMode2D.Pad),
            PaintGraph2D.ImagePattern(new() { View = imageView, Source = new(0, 0, 2, 2), PatternToLocal = Matrix3x2.CreateScale(3), TileX = ImageTileMode2D.Mirror, TileY = ImageTileMode2D.Repeat, Filter = ImageFilter2D.Linear, Alpha = AlphaRepresentation2D.Straight }),
            PaintGraph2D.DistanceField(new() { View = imageView, Source = new(0, 0, 2, 2), PatternToLocal = Matrix3x2.CreateScale(3) }, 4, linear),
            PaintGraph2D.DistanceField(new() { View = imageView, Source = new(0, 0, 2, 2), PatternToLocal = Matrix3x2.CreateScale(0.5f) }, 4, linear, DistanceFieldMode2D.Mtsdf),
            PaintGraph2D.ImagePattern(new() { View = imageView, Source = new(1, 0, 1, 2), PatternToLocal = Matrix3x2.CreateScale(3), TileX = ImageTileMode2D.Mirror }),
            PaintGraph2D.Opacity(0.5f, linear),
            PaintGraph2D.Composite(linear, solid, CompositeMode2D.SrcOver),
            PaintGraph2D.Clip(triangle, linear),
            PaintGraph2D.Transform(Matrix3x2.CreateRotation(0.2f), linear),
            PaintGraph2D.Layers([solid, PaintGraph2D.Opacity(0.5f, linear)]),
        ];
        paints.AddRange(Enum.GetValues<CompositeMode2D>().Select(mode => PaintGraph2D.Composite(linear, solid, mode)));
        foreach (PaintGraph2D paint in paints)
        {
            Action<RenderGraphCanvas2D> draw = canvas =>
            {
                canvas.ClipRect(new(0.25f, 0.375f, 15.25f, 11.25f));
                canvas.FillRect(new(1, 1, 5, 4), paint);
                canvas.FillRoundedRect(new(7, 1, 5, 4), 1.25f, paint);
                canvas.FillCircle(new(4, 7), 2.75f, paint);
                canvas.DrawLine(new(1, 1), new(12, 9), paint, new() { Width = 1.25f, Cap = LineCap2D.Round });
                canvas.FillPath(triangle, paint);
                canvas.StrokePath(triangle, paint, new() { Width = 0.75f });
                canvas.DrawImage(new(imageView, sampler), new(0.5f, 0.25f, 1, 1.5f), new(8, 5, 5, 5));
                canvas.DrawCoverageMask(new(maskView, sampler), new(0, 0, 2, 2), new(1, 1, 7, 7), paint);
                canvas.DrawDistanceField(new(maskView, sampler, DistanceFieldEncoding2D.Sdf, 4), new(0, 0, 2, 2), new(7, 4, 7, 6), paint);
                canvas.DrawMesh(new([new(new(1, 1), 0.25f), new(new(15, 2), 1), new(new(4, 11), 0.5f)], [0, 1, 2]), paint);
                canvas.DrawPaintGraph(PaintGraph2D.Opacity(0.1f, paint));
                canvas.DrawCoverageMask(new(maskView, sampler), new(0, 0, 0, 2), new(1, 1, 7, 7), paint);
                canvas.DrawDistanceField(new(maskView, sampler, DistanceFieldEncoding2D.Sdf, 4), new(0, 0, 0, 2), new(1, 1, 7, 7), paint);
                canvas.DrawCoverageMask(new(maskView, sampler), new(0, 0, 2, 2), new(1, 1, 0, 7), paint);
                canvas.DrawDistanceField(new(maskView, sampler, DistanceFieldEncoding2D.Sdf, 4), new(0, 0, 2, 2), new(1, 1, 0, 7), paint);
            };
            Vector4[] expected = await RenderAsync(device, 16, 12, draw, [image, mask]);
            Vector4[] actual = await RenderAsync(device, 16, 12, draw, [image, mask], useDefaultResources: true);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.InRange(Vector4.Distance(expected[i], actual[i]), 0, 0.002f);
            }
        }
    }

    private static Path2D RecordTriangle()
    {
        var path = new PathBuilder2D();
        path.MoveTo(new(1.25f, 1.375f));
        path.LineTo(new(13.5f, 2.25f));
        path.LineTo(new(5.25f, 10.75f));
        path.Close();
        return path.Build();
    }
}
