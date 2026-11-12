using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks normalized mask fetches against independent sample-grid geometry.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Vulkan reads every packed mask lane and preserves correlated shape/paint samples.</summary>
    /// <returns>The asynchronous checks.</returns>
    [GpuFact]
    public async Task VulkanNormalizedClipSamplesAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckNormalizedClipSamplesAsync(device);
    }

    /// <summary>Wgpu reads every packed mask lane and preserves correlated shape/paint samples.</summary>
    /// <returns>The asynchronous checks.</returns>
    [GpuFact]
    public async Task WgpuNormalizedClipSamplesAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckNormalizedClipSamplesAsync(device);
    }

    private static async Task CheckNormalizedClipSamplesAsync(IGraphicDevice device)
    {
        static Path2D Triangle(float size)
        {
            var path = new PathBuilder2D();
            path.MoveTo(Vector2.Zero);
            path.LineTo(new(size, 0));
            path.LineTo(new(0, size));
            path.Close();
            return path.Build();
        }

        foreach (float edge in new[] { 0.125f, MathF.BitIncrement(0.125f) })
        {
            Vector4[] boundary = await RenderAsync(device, 1, 1, canvas =>
            {
                canvas.ClipRect(new(edge, 0, 1, 1));
                canvas.FillCircle(new(0.5f), 2, _white);
            });
            Assert.Equal(new Vector4(edge == 0.125f ? 1 : 0.75f), Assert.Single(boundary));
        }

        Path2D triangle = Triangle(4);
        foreach (bool instanced in new[] { false, true })
        {
            for (int sy = 0; sy < 4; sy++)
            {
                for (int sx = 0; sx < 4; sx++)
                {
                    Vector4[] pixels = await RenderAsync(
                        device,
                        4,
                        4,
                        canvas =>
                    {
                        canvas.ClipPath(triangle);
                        for (int row = 0; row < 4; row++)
                        {
                            for (int column = 0; column < 4; column++)
                            {
                                canvas.FillRect(new(column + (sx * 0.25f), row + (sy * 0.25f), 0.25f, 0.25f), _white);
                            }
                        }
                    },
                        useDefaultResources: instanced);
                    for (int row = 0; row < 4; row++)
                    {
                        for (int column = 0; column < 4; column++)
                        {
                            // Independent rational triangle oracle; no product rasterizer is called here.
                            bool covered = (column + row + ((sx + sy + 1) * 0.25f)) < 4;
                            Assert.Equal(covered ? new Vector4(0.0625f) : Vector4.Zero, pixels[(row * 4) + column]);
                        }
                    }
                }
            }
        }

        var gradient = PaintGraph2D.LinearGradient(Vector2.Zero, Vector2.UnitX, [new(0, new(0, 0, 0, 1)), new(1, new(1, 0, 0, 1))]);
        Vector4[] correlated = await RenderAsync(device, 1, 1, canvas =>
        {
            canvas.ClipPath(Triangle(1));
            canvas.FillRect(new(0, 0, 0.5f, 1), gradient);
        });

        // Five correlated samples survive: three at x=1/8 and two at x=3/8.
        Assert.Equal(new Vector4(0.0703125f, 0, 0, 0.3125f), Assert.Single(correlated));

        var clipped = PaintGraph2D.Opacity(0.5f, PaintGraph2D.Clip(Triangle(1), PaintGraph2D.Solid(_red)));
        var composite = PaintGraph2D.Composite(clipped, PaintGraph2D.Solid(_blue), CompositeMode2D.SrcOver);
        Vector4[] scoped = await RenderAsync(device, 2, 1, canvas => canvas.FillRect(new(0, 0, 2, 1), composite));
        Assert.Equal(new Vector4(0.1875f, 0, 0.8125f, 1), scoped[0]);
        Assert.Equal(new Vector4(0, 0, 1, 1), scoped[1]);
    }
}
