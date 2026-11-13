using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Compares canonical rectangular paths to equivalent unspecialized winding contours.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Checks exact pixels for transformed fractional clips and both fill rules on Vulkan.</summary>
    /// <returns>The asynchronous pixel checks.</returns>
    [GpuFact]
    public async Task VulkanRectanglePathPixelsAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckRectanglePathsAsync(device);
    }

    /// <summary>Checks exact pixels for transformed fractional clips and both fill rules on wgpu.</summary>
    /// <returns>The asynchronous pixel checks.</returns>
    [GpuFact]
    public async Task WgpuRectanglePathPixelsAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckRectanglePathsAsync(device);
    }

    /// <summary>Non-invertible captured clips preserve transparent Vulkan pixels.</summary>
    /// <returns>The asynchronous pixel checks.</returns>
    [GpuFact]
    public async Task VulkanNonInvertibleRectangleClipPixelsAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckNonInvertibleRectangleClipAsync(device);
    }

    /// <summary>Non-invertible captured clips preserve transparent wgpu pixels.</summary>
    /// <returns>The asynchronous pixel checks.</returns>
    [GpuFact]
    public async Task WgpuNonInvertibleRectangleClipPixelsAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckNonInvertibleRectangleClipAsync(device);
    }

    private static async Task CheckNonInvertibleRectangleClipAsync(IGraphicDevice device)
    {
        foreach (FillRule2D rule in new[] { FillRule2D.NonZero, FillRule2D.EvenOdd })
        {
            void Draw(RenderGraphCanvas2D canvas, bool split)
            {
                canvas.Scale(new Vector2(1e-24f));
                canvas.ClipPath(RectanglePath(split), rule);
                canvas.Scale(new Vector2(1e24f));
                canvas.ClipRect(new(0, 0, 8, 8));
                canvas.FillCircle(new(1, 1), 4, new Rgba<float>(1, 0, 0, 1));
            }

            Vector4[] winding = await RenderAsync(device, 8, 8, canvas => Draw(canvas, true));
            Vector4[] rectangle = await RenderAsync(device, 8, 8, canvas => Draw(canvas, false));
            Assert.All(winding, pixel => Assert.Equal(Vector4.Zero, pixel));
            Assert.Equal(winding, rectangle);
        }
    }

    private static async Task CheckRectanglePathsAsync(IGraphicDevice device)
    {
        foreach (float scale in new[] { 1f, 1.5f, 2f })
        {
            foreach (FillRule2D rule in new[] { FillRule2D.NonZero, FillRule2D.EvenOdd })
            {
                void Draw(RenderGraphCanvas2D canvas, bool split)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        canvas.Save();
                        canvas.Translate(new(4 + (i * 3.125f), 4 + (i * 2.25f)));
                        canvas.Rotate((i - 3) * 0.07f);
                        canvas.Scale(i % 2 == 0 ? new Vector2(1.125f, 0.875f) : new Vector2(-1, 1));
                        Path2D path = RectanglePath(split);
                        canvas.ClipPath(path, rule);
                        canvas.Translate(new(0.125f, -0.25f));
                        canvas.ClipPath(path, rule);
                        canvas.FillCircle(new Vector2(8, 7), 12, new Rgba<float>(1, 0.25f, 0.5f, 0.5f));
                        canvas.FillPath(path, new Rgba<float>(0, 0.5f, 1, 0.5f), rule);
                        canvas.Restore();
                    }
                }

                Vector4[] winding = await RenderAsync(device, 64, 48, canvas => Draw(canvas, true), pixelScale: scale);
                Vector4[] rectangle = await RenderAsync(device, 64, 48, canvas => Draw(canvas, false), pixelScale: scale);
                Assert.Equal(winding, rectangle);
            }
        }
    }

    private static Path2D RectanglePath(bool split)
    {
        var path = new PathBuilder2D();
        path.MoveTo(new(1.125f, 2.25f));
        if (split)
        {
            // An extra collinear edge forces the original winding evaluator.
            path.LineTo(new(8.125f, 2.25f));
        }

        path.LineTo(new(18.625f, 2.25f));
        path.LineTo(new(18.625f, 15.75f));
        path.LineTo(new(1.125f, 15.75f));
        path.Close();
        return path.Build();
    }
}
