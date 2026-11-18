using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Native readback regression for authoritative strips after large-coordinate cancellation.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>All four analytically full red pixels must survive the Vulkan sparse path.</summary>
    /// <returns>The completed readback verification.</returns>
    [GpuFact]
    public async Task VulkanSparseLargeCoordinateFourPixelsAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckSparseLargeCoordinateFourPixelsAsync(device);
    }

    /// <summary>All four analytically full red pixels must survive the native wgpu sparse path.</summary>
    /// <returns>The completed readback verification.</returns>
    [GpuFact]
    public async Task WgpuSparseLargeCoordinateFourPixelsAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckSparseLargeCoordinateFourPixelsAsync(device);
    }

    private static async Task CheckSparseLargeCoordinateFourPixelsAsync(IGraphicDevice device)
    {
        using IRenderer2D renderer = CreateSparseOracleRenderer(device);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.ConcatTransform(new(4, 0, 0, 1, -67108864, 0));
        canvas.FillRect(new(16777216, 0, 1, 1), SRgba<float>.Red);
        using IGraphicsTexture target = device.CreateTexture(new() { Width = 8, Height = 2, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView view = target.CreateView();
        await InitializeOracleBackgroundAsync(device, target, view);
        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view, LoadOp = AttachmentLoadOp.Load });
        Vector4[] pixels = await ReadOracleDirectAsync(device, target, commands =>
        {
            var observed = new PartitionCommands(commands);
            prepared.Record(observed);
            Assert.Equal(0, observed.ComputePasses);
        });
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                // Double arithmetic gives [2^24,2^24+1)*4-2^26=[0,4).
                // No production bounds/clip helper participates in this oracle.
                Vector4 expected = y == 0 && x < 4 ? new(1, 0, 0, 1) : new(0, 0, 1, 1);
                AssertPixel(pixels, 8, x, y, expected);
            }
        }
    }
}
