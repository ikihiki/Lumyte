using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Unit-coverage strokes must not acquire interpolated mesh alpha.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Checks handmade fractional zigzag strokes on Vulkan.</summary>
    /// <returns>The completed readbacks.</returns>
    [GpuFact]
    public async Task VulkanStrokeUnitCoverageAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckStrokeUnitCoverageAsync(device);
    }

    /// <summary>Checks the same binary-coverage invariant on native wgpu.</summary>
    /// <returns>The completed readbacks.</returns>
    [GpuFact]
    public async Task WgpuStrokeUnitCoverageAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckStrokeUnitCoverageAsync(device);
    }

    /// <summary>Checks a handmade exact endpoint witness on Vulkan.</summary>
    /// <returns>The completed membership comparisons.</returns>
    [GpuFact]
    public async Task VulkanStrokeStageMembershipAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckStrokeStageMembershipAsync(device);
    }

    /// <summary>Checks the same endpoint witness through native wgpu.</summary>
    /// <returns>The completed membership comparisons.</returns>
    [GpuFact]
    public async Task WgpuStrokeStageMembershipAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckStrokeStageMembershipAsync(device);
    }

    private static async Task CheckStrokeStageMembershipAsync(IGraphicDevice device)
    {
        var references = new List<Vector4[]>();
        foreach (bool standard in new[] { false, true })
        {
            using IRenderer2D renderer = standard ? Renderer2D.CreateDefault(device) : new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
            for (int witness = 0; witness < 64; witness++)
            {
                var canvas = new Canvas2D();
                canvas.Begin();
                var endpoint = new Vector2(11.125f - (((witness % 8) + 1) * 1.375f), 8.875f - (((witness / 8) + 1) * 1.625f));
                canvas.StrokePath(SplitStrokePath(new(11.125f, 8.875f), endpoint), _white, new() { Width = 2 });
                using IGraphicsTexture target = device.CreateTexture(new() { Width = 16, Height = 16, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
                using IGraphicsTextureView view = target.CreateView();
                await InitializeOracleBackgroundAsync(device, target, view);
                using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view, LoadOp = AttachmentLoadOp.Clear, ClearColor = default });
                Vector4[] pixels = await ReadOracleDirectAsync(device, target, prepared.Record);

                // Ignore interpolation deficits only for this membership comparison;
                // the separate unit-coverage test rejects every nonbinary sample sum.
                Vector4[] membership = pixels.Select(pixel => new Vector4(MathF.Round(pixel.X * 16) / 16)).ToArray();
                if (!standard)
                {
                    references.Add(membership);
                }
                else
                {
                    Assert.Equal(references[witness], membership);
                }
            }
        }
    }

    private static async Task CheckStrokeUnitCoverageAsync(IGraphicDevice device)
    {
        foreach (bool standard in new[] { false, true })
        {
            using IRenderer2D renderer = standard ? Renderer2D.CreateDefault(device) : new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
            foreach (float dpi in new[] { 1f, 1.5f, 2f })
            {
                var canvas = new Canvas2D();
                canvas.Begin();
                var zigzag = new PathBuilder2D();
                zigzag.MoveTo(new(2.17f, 2.125f));
                for (int row = 0; row < 12; row++)
                {
                    zigzag.LineTo(new(3.137f + ((row % 2) * 23.193f), 2.375f + (row * 2.137f)));
                }

                canvas.StrokePath(zigzag.Build(), _white, new() { Width = 2 });

                using IGraphicsTexture target = device.CreateTexture(new() { Width = 64, Height = 64, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
                using IGraphicsTextureView view = target.CreateView();
                await InitializeOracleBackgroundAsync(device, target, view);
                using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view, PixelScale = dpi, LoadOp = AttachmentLoadOp.Clear, ClearColor = default });
                Vector4[] pixels = await ReadOracleDirectAsync(device, target, prepared.Record);
                foreach (Vector4 pixel in pixels)
                {
                    // A unit-coverage stroke draw is a union of binary samples,
                    // including overlaps between its own triangles: exactly k/16.
                    Assert.Equal(MathF.Round(pixel.X * 16), pixel.X * 16);
                    Assert.Equal(new Vector4(pixel.X), pixel);
                }
            }
        }
    }
}
