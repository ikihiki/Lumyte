using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks scratch capacities against independent pixel expectations.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Checks the small/large boundary and maximum program size on wgpu.</summary>
    /// <returns>The asynchronous pixel verification.</returns>
    [GpuFact]
    public async Task WgpuProgramCapacitiesAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckProgramCapacitiesAsync(device);
    }

    /// <summary>Checks the small/large boundary and maximum program size on Vulkan.</summary>
    /// <returns>The asynchronous pixel verification.</returns>
    [GpuFact]
    public async Task VulkanProgramCapacitiesAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckProgramCapacitiesAsync(device);
    }

    private static async Task CheckProgramCapacitiesAsync(IGraphicDevice device)
    {
        // Shape + initial transparent layer + two nodes per layer + final
        // multiply = 2 * layers + 3. Opacity adds one node per wrapper.
        foreach ((int layers, int opacity) in new[] { (2, 1), (3, 0), (6, 1), (6, 2), (126, 1) })
        {
            var paint = PaintGraph2D.Layers(Enumerable.Range(0, layers).Select(_ => PaintGraph2D.Solid(new Color2D(1, 0, 0, 1))).ToArray());
            for (int i = 0; i < opacity; i++)
            {
                paint = PaintGraph2D.Opacity(1, paint);
            }

            Vector4[] pixels = await RenderAsync(device, 2, 1, canvas => canvas.FillRect(new(0, 0, 1, 1), paint));
            AssertPixel(pixels, 2, 0, 0, new(1, 0, 0, 1));
            AssertPixel(pixels, 2, 1, 0, Vector4.Zero);
        }
    }
}
