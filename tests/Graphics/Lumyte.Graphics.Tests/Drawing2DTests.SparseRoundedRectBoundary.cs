using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.ImageEffects;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks inclusive rounded bounds against native shader coverage and half-open rectangles.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Reads actual Vulkan direct-target and graph pixels for every radius category.</summary>
    /// <returns>The completed readbacks.</returns>
    [GpuFact]
    public async Task VulkanSparseRoundedRectBoundaryAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckSparseRoundedRectBoundaryAsync(device);
    }

    /// <summary>Reads actual wgpu direct-target and graph pixels for every radius category.</summary>
    /// <returns>The completed readbacks.</returns>
    [GpuFact]
    public async Task WgpuSparseRoundedRectBoundaryAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckSparseRoundedRectBoundaryAsync(device);
    }

    private static async Task CheckSparseRoundedRectBoundaryAsync(IGraphicDevice device)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        float[] radii = [0, 0.0625f, 0.125f, 0.25f, 1];
        for (int i = 0; i < radii.Length; i++)
        {
            canvas.FillRoundedRect(new(i + 0.125f, 0.125f, 0.5f, 0.5f), radii[i], _white);
        }

        canvas.FillRect(new(5.125f, 0.125f, 0.5f, 0.5f), _white);
        canvas.FillCircle(new(6.375f, 0.375f), 0.25f, _white);
        DrawList2D list = canvas.End();
        using IRenderer2D standard = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using IRenderer2D sparse = CreateSparseOracleRenderer(device);
        foreach (IRenderer2D renderer in new[] { standard, sparse })
        {
            using IGraphicsTexture target = device.CreateTexture(new() { Width = 8, Height = 2, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
            using IGraphicsTextureView view = target.CreateView();
            await InitializeOracleBackgroundAsync(device, target, view);
            using PreparedDraw2D prepared = await renderer.PrepareAsync(list, new() { View = view, LoadOp = AttachmentLoadOp.Clear, ClearColor = default });
            Vector4[] direct = await ReadOracleDirectAsync(device, target, prepared.Record);
            AssertRoundedBoundaryPixels(direct);
            using var graph = new Graph(device, "rounded-boundary-oracle");
            (PreparedDrawingPasses2D preparation, EffectImage image) = await AppendSparseOracleImageAsync(graph, renderer, list, 8, 2);
            using PreparedDrawingPasses2D owner = preparation;
            Vector4[] graphed = await ReadOracleGraphAsync(device, graph, image);
            AssertRoundedBoundaryPixels(graphed);
        }
    }

    private static void AssertRoundedBoundaryPixels(Vector4[] pixels)
    {
        // Exact 4x4 lattice counts: inclusive zero-radius rounded rect = 9;
        // positive rounded corners = 5; half-open rect = 4; strict circle = 1.
        int[] counts = [9, 5, 5, 5, 5, 4, 1, 0];
        for (int x = 0; x < counts.Length; x++)
        {
            AssertPixel(pixels, 8, x, 0, new Vector4(counts[x] / 16f));
            AssertPixel(pixels, 8, x, 1, Vector4.Zero);
        }
    }
}
