using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks generic batch allocation sharing without changing evaluator semantics.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Checks shared native-address ranges against separately allocated programs.</summary>
    /// <returns>The asynchronous readback checks.</returns>
    [GpuFact]
    public async Task VulkanSharedStorageAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckSharedStorageAsync(device);
    }

    /// <summary>Checks the portable separate-allocation fallback.</summary>
    /// <returns>The asynchronous readback checks.</returns>
    [GpuFact]
    public async Task WgpuSharedStorageFallbackAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckSharedStorageAsync(device);
    }

    private static async Task CheckSharedStorageAsync(IGraphicDevice device)
    {
        foreach (FillRule2D rule in new[] { FillRule2D.NonZero, FillRule2D.EvenOdd })
        {
            foreach (float scale in new[] { 1f, 1.5f, 2f })
            {
                void Draw(RenderGraphCanvas2D canvas)
                {
                    canvas.ClipRect(new(0.25f, 0.5f, 30.5f, 22.25f));
                    canvas.ClipRect(new(1.125f, 1.25f, 29.5f, 21.5f));
                    for (int i = 0; i < 24; i++)
                    {
                        canvas.Save();
                        canvas.Translate(new((i % 4) * 4.125f, (i % 3) * 3.25f));
                        canvas.Rotate((i - 12) * 0.025f);
                        canvas.Scale(i % 2 == 0 ? new Vector2(1.125f, 0.875f) : new Vector2(-1, 1));
                        var path = new PathBuilder2D();
                        path.MoveTo(new(-8, 2));
                        path.LineTo(new(12, 2));
                        if (i % 2 == 0)
                        {
                            path.QuadraticTo(new(14, 9), new(9, 14));
                        }
                        else
                        {
                            path.LineTo(new(9, 14));
                        }

                        path.LineTo(new(-8, 14));
                        path.Close();
                        canvas.FillPath(path.Build(), new Color2D(i % 2, 0.5f, (i % 3) / 2f, 0.25f), rule);
                        canvas.Restore();
                    }
                }

                Vector4[] independent = await RenderAsync(device, 32, 24, Draw, pixelScale: scale, individualPaths: true);
                Assert.Equal(independent, await RenderAsync(device, 32, 24, Draw, pixelScale: scale));
            }
        }

        void Solid(RenderGraphCanvas2D canvas)
        {
            var path = new PathBuilder2D();
            path.MoveTo(new(1, 1));
            path.LineTo(new(7, 1));
            path.LineTo(new(7, 7));
            path.LineTo(new(1, 7));
            path.Close();
            canvas.FillPath(path.Build(), new Color2D(1, 0, 0, 0.5f));
            canvas.FillPath(path.Build(), new Color2D(0, 0, 1, 0.5f));
        }

        using IGraphicsTexture red = device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination });
        using IGraphicsTexture blue = device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination });
        using IGraphicsTextureView redView = red.CreateView();
        using IGraphicsTextureView blueView = blue.CreateView();
        await UploadAsync(device, red, [255, 0, 0, 255]);
        await UploadAsync(device, blue, [0, 0, 255, 255]);
        void Sampled(RenderGraphCanvas2D canvas)
        {
            var redPaint = PaintGraph2D.ImagePattern(new() { View = redView, Source = new(0, 0, 1, 1), Filter = ImageFilter2D.Nearest });
            var bluePaint = PaintGraph2D.Opacity(0.5f, PaintGraph2D.ImagePattern(new() { View = blueView, Source = new(0, 0, 1, 1), Filter = ImageFilter2D.Nearest }));
            canvas.FillPath(Rectangle(0, 0, 4, 4), redPaint);
            canvas.FillPath(Rectangle(2, 0, 4, 4), bluePaint);
        }

        Vector4[] sampled = await RenderAsync(device, 8, 4, Sampled, [red, blue]);
        Assert.Equal(await RenderAsync(device, 8, 4, Sampled, [red, blue], individualPaths: true), sampled);
        AssertPixel(sampled, 8, 1, 1, new(1, 0, 0, 1));
        AssertPixel(sampled, 8, 3, 1, new(0.5f, 0, 0.5f, 1));
        AssertPixel(sampled, 8, 5, 1, new(0, 0, 0.5f, 0.5f));

        Vector4[] solid = await RenderAsync(device, 8, 8, Solid);
        AssertPixel(solid, 8, 4, 4, new(0.25f, 0, 0.5f, 0.75f));
        AssertPixel(solid, 8, 0, 0, Vector4.Zero);
    }

    private sealed class IndividualPathRenderer : IFillPathRenderer2D
    {
        public async ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillPathCommand2D> commands, CancellationToken cancellationToken)
        {
            IFillPathRenderer2D renderer = new DefaultFillPathRenderer2D();
            var batches = new List<IPreparedCommandBatch2D>();
            try
            {
                for (int i = 0; i < commands.Length; i++)
                {
                    batches.Add(await renderer.PrepareAsync(context, commands.Slice(i, 1), cancellationToken));
                }

                return new IndividualPathBatch(batches);
            }
            catch
            {
                foreach (IPreparedCommandBatch2D batch in batches)
                {
                    batch.Dispose();
                }

                throw;
            }
        }
    }

    private sealed class IndividualPathBatch(List<IPreparedCommandBatch2D> batches) : IPreparedCommandBatch2D
    {
        public void DeclareResources(IDrawingResourceDeclarations2D resources)
        {
            foreach (IPreparedCommandBatch2D batch in batches)
            {
                batch.DeclareResources(resources);
            }
        }

        public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target)
        {
            foreach (IPreparedCommandBatch2D batch in batches)
            {
                batch.Record(commands, target);
            }
        }

        public void Dispose()
        {
            foreach (IPreparedCommandBatch2D batch in batches)
            {
                batch.Dispose();
            }
        }
    }
}
