using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks fractional partitioning with real GPU source-over and shape AA intact.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Adjacent translucent buffered shapes own every device pixel once, at fractional DPR too.</summary>
    /// <returns>The asynchronous hardware readback checks.</returns>
    [GpuFact]
    public async Task VulkanOwnershipClipPixelsAsync()
    {
        using var device = VulkanDevice.Create();
        foreach (float scale in new[] { 1f, 1.5f, 2f })
        {
            foreach (bool reflect in new[] { false, true })
            {
                foreach (bool path in new[] { false, true })
                {
                    Vector4[] pixels = await RenderAsync(
                        device,
                        32,
                        16,
                        canvas =>
                        {
                            canvas.ClipRect(new(0.25f / scale, 0, 30 / scale, 16 / scale));
                            canvas.ConcatTransform(Matrix3x2.CreateScale(reflect ? -1 : 1, 1) * Matrix3x2.CreateTranslation(reflect ? 12.25f : 0.25f, 0));
                            foreach (Rect2D owner in new[] { new Rect2D(0, 0, 5.5f, 20), new Rect2D(5.5f, 0, 6.5f, 20) })
                            {
                                canvas.Save();
                                canvas.ClipOwnershipRect(owner);

                                // Both buffers intentionally overlap the seam by many pixels.
                                if (path)
                                {
                                    canvas.FillPath(Rectangle(-20, -20, 60, 60), new SRgba<float>(0, 0, 1, 0.5f));
                                }
                                else
                                {
                                    canvas.FillRect(new(-20, -20, 60, 60), new SRgba<float>(0, 0, 1, 0.5f));
                                }

                                canvas.Restore();
                            }
                        },
                        pixelScale: scale);
                    for (int x = 0; x < 32; x++)
                    {
                        bool owned = x + 0.5 >= 0.25 * scale && x + 0.5 < 12.25 * scale;
                        float coverage = x == 0 ? 0.75f : 1;
                        Assert.Equal(owned ? new Vector4(0, 0, 0.5f * coverage, 0.5f * coverage) : Vector4.Zero, pixels[32 + x]);
                    }
                }
            }
        }
    }
}
