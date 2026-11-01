using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Compares instanced rectangles with the original per-command evaluator.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Checks ordering, coverage, clip and fallback boundaries on wgpu.</summary>
    /// <returns>The asynchronous pixel comparisons.</returns>
    [GpuFact]
    public async Task WgpuRectangleBatchPixelsAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckRectangleBatchAsync(device);
    }

    /// <summary>Checks ordering, coverage, clip and fallback boundaries on Vulkan.</summary>
    /// <returns>The asynchronous pixel comparisons.</returns>
    [GpuFact]
    public async Task VulkanRectangleBatchPixelsAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckRectangleBatchAsync(device);
    }

    private static async Task CheckRectangleBatchAsync(IGraphicDevice device)
    {
        foreach (float scale in new[] { 1f, 1.5f, 2f })
        {
            Vector4[] reference = await RenderAsync(device, 64, 48, DrawRectangles, pixelScale: scale);
            Vector4[] batched = await RenderAsync(device, 64, 48, DrawRectangles, pixelScale: scale, useDefaultResources: true);
            Assert.Equal(reference, batched);
        }

        // Cross the pool's 1,024-instance chunk boundary with overlapping alpha.
        void Many(RenderGraphCanvas2D canvas)
        {
            for (int i = 0; i < 1030; i++)
            {
                canvas.FillRect(new((i % 7) + 0.125f, (i % 5) + 0.25f, 2.5f, 2.75f), new Color2D(i % 2, 0, (i + 1) % 2, 0.5f));
            }
        }

        Assert.Equal(await RenderAsync(device, 12, 10, Many), await RenderAsync(device, 12, 10, Many, useDefaultResources: true));
    }

    private static void DrawRectangles(RenderGraphCanvas2D canvas)
    {
        var random = new Random(120);
        for (int i = 0; i < 60; i++)
        {
            canvas.Save();
            canvas.Translate(new((i % 4) * 0.125f, (i % 3) * 0.25f));
            if (i % 3 != 0)
            {
                canvas.ClipRect(new(2.125f, 1.25f, 27.5f, 20.75f));
            }

            canvas.Rotate(((i % 5) - 2) * 0.07f);
            if (i % 4 == 0)
            {
                canvas.ClipRect(new(1.5f, 2.25f, 22, 16.5f));
            }

            if (i % 7 == 0)
            {
                // An arbitrary clip must keep the original evaluator.
                var path = new PathBuilder2D();
                path.MoveTo(new(0, 0));
                path.LineTo(new(25, 5));
                path.LineTo(new(10, 25));
                path.Close();
                canvas.ClipPath(path.Build());
            }

            var rect = new Rect2D(random.Next(30) + 0.125f, random.Next(20) + 0.25f, 5.5f, 6.75f);
            canvas.FillRect(rect, new Color2D(i % 2, (i % 3) / 2f, (i % 5) / 4f, ((i % 4) + 1) / 4f));
            canvas.Restore();
        }
    }
}
