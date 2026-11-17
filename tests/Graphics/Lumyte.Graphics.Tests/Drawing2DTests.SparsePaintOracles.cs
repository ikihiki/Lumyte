using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.ImageEffects;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Tests;

/// <summary>Analytic paint and sampled-resource checks for both public alternative renderers.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Checks cropped images, coverage masks and all distance-field encodings on Vulkan.</summary>
    /// <returns>The completed native GPU verification.</returns>
    [GpuFact]
    public async Task VulkanAlternativeRendererImageFieldOraclesAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckAlternativeImageFieldOraclesAsync(device);
    }

    /// <summary>Checks cropped images, coverage masks and all distance-field encodings on wgpu.</summary>
    /// <returns>The completed native GPU verification.</returns>
    [GpuFact]
    public async Task WgpuAlternativeRendererImageFieldOraclesAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckAlternativeImageFieldOraclesAsync(device);
    }

    /// <summary>Checks barycentric mesh coverage and analytic gradients on Vulkan.</summary>
    /// <returns>The completed native GPU verification.</returns>
    [GpuFact]
    public async Task VulkanAlternativeRendererMeshGradientOraclesAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckAlternativeMeshGradientOraclesAsync(device);
    }

    /// <summary>Checks barycentric mesh coverage and analytic gradients on wgpu.</summary>
    /// <returns>The completed native GPU verification.</returns>
    [GpuFact]
    public async Task WgpuAlternativeRendererMeshGradientOraclesAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckAlternativeMeshGradientOraclesAsync(device);
    }

    private static async Task CheckAlternativeImageFieldOraclesAsync(IGraphicDevice device)
    {
        using IGraphicsTexture atlas = device.CreateTexture(new() { Width = 4, Height = 1, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination });
        using IGraphicsTextureView atlasView = atlas.CreateView();
        using IGraphicsTexture coverage = device.CreateTexture(new() { Width = 3, Height = 1, Format = TextureFormat.R8Unorm, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination });
        using IGraphicsTextureView coverageView = coverage.CreateView();
        using IGraphicsTexture multichannel = device.CreateTexture(new() { Width = 2, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination });
        using IGraphicsTextureView multichannelView = multichannel.CreateView();
        using IGraphicsSampler sampler = device.CreateSampler(new() { MinFilter = FilterMode.Nearest, MagFilter = FilterMode.Nearest, MipmapFilter = FilterMode.Nearest, AddressU = AddressMode.ClampToEdge, AddressV = AddressMode.ClampToEdge });

        // Green guards make an incorrect atlas source rectangle independently observable.
        await UploadAsync(device, atlas, [0, 255, 0, 255, 255, 0, 0, 255, 0, 0, 255, 128, 0, 255, 0, 255]);
        await UploadAsync(device, coverage, [0, 128, 255]);

        // RGB medians are respectively one and zero; alpha deliberately disagrees.
        await UploadAsync(device, multichannel, [0, 255, 255, 0, 255, 0, 0, 255]);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.DrawImage(new(atlasView, sampler), new(1, 0, 2, 1), new(2, 2, 8, 4));
        canvas.DrawCoverageMask(new(coverageView, sampler), new(0, 0, 3, 1), new(2, 8, 12, 4), PaintGraph2D.Solid(_red));
        canvas.DrawDistanceField(new(coverageView, sampler, DistanceFieldEncoding2D.Sdf, 4), new(0, 0, 3, 1), new(2, 14, 12, 4), PaintGraph2D.Solid(_white));
        canvas.DrawDistanceField(new(multichannelView, sampler, DistanceFieldEncoding2D.Msdf, 4), new(0, 0, 2, 1), new(2, 20, 8, 4), PaintGraph2D.Solid(_white));
        canvas.DrawDistanceField(new(multichannelView, sampler, DistanceFieldEncoding2D.Mtsdf, 4), new(0, 0, 2, 1), new(2, 26, 8, 4), PaintGraph2D.Solid(_white));
        DrawList2D list = canvas.End();
        {
            using IRenderer2D renderer = CreateSparseOracleRenderer(device);
            using var graph = new Graph(device, "alternative-image-field-oracle");
            (PreparedDrawingPasses2D imagePreparation, EffectImage image) = await AppendSparseOracleImageAsync(graph, renderer, list, 24, 32, atlas, coverage, multichannel);
            using PreparedDrawingPasses2D imageOwner = imagePreparation;
            Vector4[] pixels = await ReadOracleGraphAsync(device, graph, image);
            AssertPixel(pixels, 24, 3, 3, new(1, 0, 0, 1));
            AssertPixel(pixels, 24, 8, 3, new(0, 0, 128f / 255, 128f / 255));
            AssertPixel(pixels, 24, 0, 3, Vector4.Zero);
            AssertPixel(pixels, 24, 11, 3, Vector4.Zero);
            AssertPixel(pixels, 24, 3, 9, Vector4.Zero);
            AssertPixel(pixels, 24, 7, 9, new(128f / 255, 0, 0, 128f / 255));
            AssertPixel(pixels, 24, 11, 9, new(1, 0, 0, 1));
            AssertPixel(pixels, 24, 15, 9, Vector4.Zero);
            AssertPixel(pixels, 24, 3, 15, Vector4.Zero);
            AssertPixel(pixels, 24, 11, 15, Vector4.One);
            foreach (int y in new[] { 21, 27 })
            {
                AssertPixel(pixels, 24, 3, y, Vector4.One);
                AssertPixel(pixels, 24, 8, y, Vector4.Zero);
                AssertPixel(pixels, 24, 11, y, Vector4.Zero);
            }

            AssertPixel(pixels, 24, 23, 31, Vector4.Zero);
        }
    }

    private static async Task CheckAlternativeMeshGradientOraclesAsync(IGraphicDevice device)
    {
        ColorStop2D[] transparentBlueToRed = [new(0, new(0, 0, 1, 0)), new(1, _red)];
        ColorStop2D[] blackToWhite = [new(0, new(0, 0, 0, 1)), new(1, _white)];
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(2, 2, 16, 4), PaintGraph2D.LinearGradient(new(2, 0), new(18, 0), transparentBlueToRed));
        canvas.FillRect(new(2, 8, 16, 4), PaintGraph2D.LinearGradient(new(2, 0), new(10, 0), blackToWhite, ExtendMode2D.Reflect));
        canvas.FillRect(new(20, 2, 8, 8), PaintGraph2D.RadialGradient(new(20.5f, 2.5f), 8, blackToWhite));
        canvas.FillRect(new(20, 12, 8, 8), PaintGraph2D.SweepGradient(new(20.5f, 12.5f), 0, MathF.PI, blackToWhite, ExtendMode2D.Pad));
        canvas.DrawMesh(new([new(new(2, 16), 0.25f), new(new(18, 16), 0.75f), new(new(2, 24), 0.25f)], [0, 1, 2]), PaintGraph2D.Solid(_white));
        DrawList2D list = canvas.End();
        {
            using IRenderer2D renderer = CreateSparseOracleRenderer(device);
            using var graph = new Graph(device, "alternative-mesh-gradient-oracle");
            (PreparedDrawingPasses2D imagePreparation, EffectImage image) = await AppendSparseOracleImageAsync(graph, renderer, list, 32, 24);
            using PreparedDrawingPasses2D imageOwner = imagePreparation;
            Vector4[] pixels = await ReadOracleGraphAsync(device, graph, image);

            // Pixel centers give t=(x+0.5-2)/16; interpolation is premultiplied.
            AssertPixel(pixels, 32, 5, 3, new(0.21875f, 0, 0, 0.21875f));
            AssertPixel(pixels, 32, 13, 3, new(0.71875f, 0, 0, 0.71875f));
            AssertPixel(pixels, 32, 0, 3, Vector4.Zero);

            // Reflect maps 11.5/8 to 2-11.5/8 = 0.5625.
            AssertPixel(pixels, 32, 13, 9, new(0.5625f, 0.5625f, 0.5625f, 1));
            AssertPixel(pixels, 32, 24, 2, new(0.5f, 0.5f, 0.5f, 1));
            AssertPixel(pixels, 32, 20, 16, new(0.5f, 0.5f, 0.5f, 1));

            // Coverage = 0.25 + 0.5*(x-2)/16 inside this triangle.
            AssertPixel(pixels, 32, 5, 18, new(0.359375f));
            AssertPixel(pixels, 32, 9, 17, new(0.484375f));
            AssertPixel(pixels, 32, 16, 22, Vector4.Zero);
            AssertPixel(pixels, 32, 0, 18, Vector4.Zero);
            AssertPixel(pixels, 32, 31, 23, Vector4.Zero);
        }
    }
}
