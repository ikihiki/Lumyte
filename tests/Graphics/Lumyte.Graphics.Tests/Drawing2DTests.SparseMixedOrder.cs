using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Analytic ordered SrcOver across actual sparse/default/sparse groups.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Checks mixed algorithm ordering and fractional clip on Vulkan.</summary>
    /// <returns>The completed full-surface pixel verification.</returns>
    [GpuFact]
    public async Task VulkanSparseDefaultSparsePainterOrderAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckSparseDefaultSparsePainterOrderAsync(device);
    }

    /// <summary>Checks mixed algorithm ordering and fractional clip on native wgpu.</summary>
    /// <returns>The completed full-surface pixel verification.</returns>
    [GpuFact]
    public async Task WgpuSparseDefaultSparsePainterOrderAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckSparseDefaultSparsePainterOrderAsync(device);
    }

    private static async Task CheckSparseDefaultSparsePainterOrderAsync(IGraphicDevice device)
    {
        using IRenderer2D renderer = CreateSparseOracleRenderer(device, legacyCircle: true);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.ClipRect(new(0.5f, 0, 7.5f, 8));
        canvas.FillRect(new(0, 0, 8, 8), new SRgba<float>(1, 0, 0, 0.5f));
        canvas.FillCircle(new(4), 16, new SRgba<float>(0, 1, 0, 0.5f));
        canvas.FillRoundedRect(new(0, 0, 8, 8), 0, new SRgba<float>(0, 0, 1, 0.5f));
        using IGraphicsTexture target = device.CreateTexture(new() { Width = 8, Height = 8, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView view = target.CreateView();
        await InitializeOracleBackgroundAsync(device, target, view);
        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view, LoadOp = AttachmentLoadOp.Load });
        Vector4[] pixels = await ReadOracleDirectAsync(device, target, commands =>
        {
            var observed = new PartitionCommands(commands);
            prepared.Record(observed);
            Assert.Equal(0, observed.ComputePasses);
        });
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                // At x=0 each command has eight of sixteen clip samples, hence
                // premultiplied source alpha1/4. Else alpha1/2. These exact binary
                // fractions follow red then green then blue over opaque blue.
                Vector4 expected = x == 0 ? new(0.140625f, 0.1875f, 0.671875f, 1) : new(0.125f, 0.25f, 0.625f, 1);
                AssertPixel(pixels, 8, x, y, expected);
            }
        }
    }
}
