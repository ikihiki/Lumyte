using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Verifies the exact integer-scissor subset against winding and expected pixels.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Vulkan integer scissors preserve exact public-API pixels.</summary>
    /// <returns>The asynchronous checks.</returns>
    [GpuFact]
    public async Task VulkanIntegerScissorPixelsAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckIntegerScissorAsync(device);
    }

    /// <summary>Wgpu integer scissors preserve exact public-API pixels.</summary>
    /// <returns>The asynchronous checks.</returns>
    [GpuFact]
    public async Task WgpuIntegerScissorPixelsAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckIntegerScissorAsync(device);
    }

    private static async Task CheckIntegerScissorAsync(IGraphicDevice device)
    {
        foreach (FillRule2D rule in new[] { FillRule2D.NonZero, FillRule2D.EvenOdd })
        {
            foreach (bool empty in new[] { false, true })
            {
                var canonical = new PathBuilder2D();
                canonical.MoveTo(new(2, 3));
                canonical.LineTo(new(22, 3));
                canonical.LineTo(new(22, 23));
                canonical.LineTo(new(2, 23));
                canonical.Close();
                var winding = new PathBuilder2D();
                winding.MoveTo(new(2, 3));
                winding.LineTo(new(12, 3));
                winding.LineTo(new(22, 3));
                winding.LineTo(new(22, 23));
                winding.LineTo(new(2, 23));
                winding.Close();

                async Task<Vector4[]> Draw(Path2D path) => await RenderAsync(device, 32, 32, canvas =>
                {
                    canvas.ClipPath(path, rule);
                    canvas.ClipRect(empty ? new(25, 25, 2, 2) : new(5, 1, 10, 10));

                    // Circle keeps this test on the interpreter route, not rectangle instancing.
                    canvas.FillCircle(new(16, 16), 32, _red);
                });

                Vector4[] actual = await Draw(canonical.Build());
                Assert.Equal(await Draw(winding.Build()), actual);
                for (int y = 0; y < 32; y++)
                {
                    for (int x = 0; x < 32; x++)
                    {
                        bool covered = !empty && x >= 5 && x < 15 && y >= 3 && y < 11;
                        Assert.Equal(covered ? new Vector4(1, 0, 0, 1) : Vector4.Zero, actual[(y * 32) + x]);
                    }
                }
            }
        }
    }
}
