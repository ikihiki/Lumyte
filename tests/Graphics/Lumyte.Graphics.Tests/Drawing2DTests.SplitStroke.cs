using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Separately averaged draws are not a sample-wise union of adjacent roads.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Checks handmade split strokes with both current renderers on Radeon/Vulkan.</summary>
    /// <returns>The completed readbacks.</returns>
    [GpuFact]
    public async Task VulkanSplitStrokeContractAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckSplitStrokeContractAsync(device);
    }

    /// <summary>Checks the same contract through native wgpu.</summary>
    /// <returns>The completed readbacks.</returns>
    [GpuFact]
    public async Task WgpuSplitStrokeContractAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckSplitStrokeContractAsync(device);
    }

    private static async Task CheckSplitStrokeContractAsync(IGraphicDevice device)
    {
        // Invented coordinates: a horizontal stroke split halfway through pixel (2, 1).
        // Each butt command covers 8/16 samples, their union covers all 16.
        // SrcOver of the two averaged alphas is 1/2 + (1 - 1/2) * 1/2 = 3/4.
        uint leftMask = 0;
        uint rightMask = 0;
        for (int sample = 0; sample < 16; sample++)
        {
            if ((sample % 4) < 2)
            {
                leftMask |= 1u << sample;
            }
            else
            {
                rightMask |= 1u << sample;
            }
        }

        Assert.Equal(0x3333u, leftMask);
        Assert.Equal(0xccccu, rightMask);
        Assert.Equal(0xffffu, leftMask | rightMask);
        foreach (bool standard in new[] { false, true })
        {
            using IRenderer2D renderer = standard ? Renderer2D.CreateDefault(device) : new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());

            // A right-angle miter fills the outer corner, independently of the
            // two segment bodies. A bevel deliberately leaves that corner empty.
            foreach (bool miter in new[] { false, true })
            {
                var corner = new Canvas2D();
                corner.Begin();
                corner.StrokePath(SplitStrokePath(new(0, 1), new(2, 1), new(2, 3)), PaintGraph2D.Clip(Rectangle(0, 0, 4, 4), PaintGraph2D.Solid(_white)), new() { Width = 2, Join = miter ? LineJoin2D.Miter : LineJoin2D.Bevel });
                using IGraphicsTexture cornerTarget = device.CreateTexture(new() { Width = 4, Height = 4, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
                using IGraphicsTextureView cornerView = cornerTarget.CreateView();
                await InitializeOracleBackgroundAsync(device, cornerTarget, cornerView);
                using PreparedDraw2D cornerPrepared = await renderer.PrepareAsync(corner.End(), new() { View = cornerView, LoadOp = AttachmentLoadOp.Clear, ClearColor = new(0, 0, 0, 0) });
                Vector4[] cornerPixels = await ReadOracleDirectAsync(device, cornerTarget, cornerPrepared.Record);
                Assert.Equal(new Vector4(miter ? 1 : 0.625f), cornerPixels[2]);
            }

            foreach (bool continuous in new[] { false, true })
            {
                foreach (bool reverse in new[] { false, true })
                {
                    foreach (bool square in new[] { false, true })
                    {
                        foreach (bool clip in new[] { false, true })
                        {
                            var canvas = new Canvas2D();
                            canvas.Begin();
                            if (clip)
                            {
                                canvas.ClipRect(new(2.5f, 0, 2, 3));
                            }

                            var style = new StrokeStyle2D { Width = 2, Cap = square ? LineCap2D.Square : LineCap2D.Butt };
                            if (continuous)
                            {
                                canvas.StrokePath(SplitStrokePath(new(0, 1.5f), new(2.5f, 1.5f), new(5, 1.5f)), _white, style);
                            }
                            else
                            {
                                Path2D left = SplitStrokePath(new(0, 1.5f), new(2.5f, 1.5f));
                                Path2D right = SplitStrokePath(new(2.5f, 1.5f), new(5, 1.5f));
                                canvas.StrokePath(reverse ? right : left, _red, style);
                                canvas.StrokePath(reverse ? left : right, _blue, style);
                            }

                            using IGraphicsTexture target = device.CreateTexture(new() { Width = 5, Height = 3, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
                            using IGraphicsTextureView view = target.CreateView();
                            await InitializeOracleBackgroundAsync(device, target, view);
                            DrawList2D list = canvas.End();
                            using PreparedDraw2D prepared = await renderer.PrepareAsync(list, new() { View = view, LoadOp = AttachmentLoadOp.Clear, ClearColor = new(0, 0, 0, 0) });
                            Vector4[] pixels = await ReadOracleDirectAsync(device, target, prepared.Record);
                            Vector4 expected;
                            if (continuous)
                            {
                                expected = new(clip ? 0.5f : 1);
                            }
                            else if (square)
                            {
                                expected = clip ? new(0.25f, 0, 0.5f, 0.75f) : new(0, 0, 1, 1);
                            }
                            else if (clip)
                            {
                                expected = reverse ? new(0.5f, 0, 0, 0.5f) : new(0, 0, 0.5f, 0.5f);
                            }
                            else
                            {
                                expected = new(0.25f, 0, 0.5f, 0.75f);
                            }

                            Assert.Equal(expected, pixels[7]);
                        }
                    }
                }
            }
        }
    }

    private static Path2D SplitStrokePath(params Vector2[] points)
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(points[0]);
        foreach (Vector2 point in points.Skip(1))
        {
            builder.LineTo(point);
        }

        return builder.Build();
    }
}
