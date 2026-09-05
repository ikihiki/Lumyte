using System.Numerics;

using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Text.Tests;

public abstract class TextBackendConformanceTests
{
    private const uint Width = 96;
    private const uint Height = 96;
    private const int RowPitch = checked((int)Width * 4);
    private const ulong ByteCount = (ulong)RowPitch * Height;

    private static readonly TextRenderingPolicy BoundaryPolicy = new()
    {
        CoverageMaximumSize = 14,
        SignedDistanceMaximumSize = 28,
        MultiChannelSignedDistanceMaximumSize = 42,
        PolygonMaximumSize = 56,
    };

    [Theory]
    [InlineData(14, TextRenderingMode.Coverage)]
    [InlineData(28, TextRenderingMode.SignedDistance)]
    [InlineData(42, TextRenderingMode.MultiChannelSignedDistance)]
    [InlineData(56, TextRenderingMode.Polygon)]
    [InlineData(57, TextRenderingMode.VectorPath)]
    [Trait("Category", "TextConformance")]
    public void AutoRenderingDrawsEveryPolicyRoute(float fontSize, TextRenderingMode expectedMode)
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using var textRenderer = new TextRenderer(renderer, BoundaryPolicy, 64, 64);
        using var font = new FontFace(TestFontData.Create());
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        TextDrawResult result = encoder.DrawText(
            textRenderer,
            font,
            "A",
            new Vector2(16, 80),
            fontSize,
            Brush.Solid(new(0, 1, 0)),
            new() { RenderingMode = TextRenderingMode.Auto });
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "text",
            renderer,
            prepared,
            new RenderTarget(
                target.Handle,
                target.Description,
                GpuAttachmentLoadOperation.Clear,
                ClearColor: new(0, 0, 0, 0)));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        Assert.Equal(expectedMode, result.RenderingMode);
        Assert.Equal(0, result.FallbackGlyphCount);
        AssertGreenPixelNear(
            pixels,
            (int)MathF.Round(16 + fontSize * 0.3f),
            (int)MathF.Round(80 - fontSize * 0.2f));
        AssertPixelNear(pixels, 2, 2, 0, 0, 0, 0);
    }

    [Theory]
    [InlineData(TextRenderingMode.Coverage, 0, 0, 0, 255)]
    [InlineData(TextRenderingMode.SignedDistance, 0, 0, 0, 255)]
    [InlineData(TextRenderingMode.MultiChannelSignedDistance, 0, 0, 0, 255)]
    [InlineData(TextRenderingMode.Polygon, 0, 0, 0, 255)]
    [InlineData(TextRenderingMode.VectorPath, 0, 0, 0, 255)]
    [InlineData(TextRenderingMode.VectorPath, 1, 0, 255, 0)]
    [Trait("Category", "TextConformance")]
    public void ColorGlyphDrawsEveryRouteWithSelectedPaletteAndForeground(
        TextRenderingMode renderingMode,
        uint paletteIndex,
        byte paletteRed,
        byte paletteGreen,
        byte paletteBlue)
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using var textRenderer = new TextRenderer(renderer, BoundaryPolicy, 128, 128);
        using var font = new FontFace(TestFontData.CreateColor());
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        TextDrawResult result = encoder.DrawText(
            textRenderer,
            font,
            "\U0001F600",
            new Vector2(16, 80),
            64,
            Brush.Solid(new(1, 0, 0)),
            new()
            {
                RenderingMode = renderingMode,
                ColorGlyphMode = ColorGlyphMode.Auto,
                ColorPaletteIndex = paletteIndex,
            });
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "color text",
            renderer,
            prepared,
            new RenderTarget(
                target.Handle,
                target.Description,
                GpuAttachmentLoadOperation.Clear,
                ClearColor: new(0, 0, 0, 0)));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        Assert.Equal(renderingMode, result.RenderingMode);
        Assert.Equal(1, result.ColorGlyphCount);
        Assert.Equal(0, result.FallbackGlyphCount);
        AssertOpaqueColorNear(pixels, 23, 60, paletteRed, paletteGreen, paletteBlue);
        AssertOpaqueColorNear(pixels, 35, 60, 255, 0, 0);
        AssertPixelNear(pixels, 2, 2, 0, 0, 0, 0);
    }

    [Fact]
    [Trait("Category", "TextConformance")]
    public void ColrVersionOnePaintRendersThroughTheGpuPath()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using var textRenderer = new TextRenderer(renderer, BoundaryPolicy, 64, 64);
        using var font = new FontFace(TestFontData.CreateColorV1());
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        TextDrawResult result = encoder.DrawText(
            textRenderer,
            font,
            "\U0001F600",
            new Vector2(16, 80),
            64,
            Brush.Solid(new(1, 0, 0)),
            new()
            {
                RenderingMode = TextRenderingMode.VectorPath,
                ColorGlyphMode = ColorGlyphMode.Auto,
                ColorPaletteIndex = 0,
            });
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "COLRv1 text",
            renderer,
            prepared,
            new RenderTarget(
                target.Handle,
                target.Description,
                GpuAttachmentLoadOperation.Clear,
                ClearColor: new(0, 0, 0, 0)));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        Assert.Equal(1, result.ColorGlyphCount);
        Assert.Equal(0, result.BitmapGlyphCount);
        Assert.Equal(0, result.FallbackGlyphCount);
        AssertOpaqueColorNear(pixels, 24, 60, 0, 0, 255);
        AssertPixelNear(pixels, 2, 2, 0, 0, 0, 0);
    }

    [Fact]
    [Trait("Category", "TextConformance")]
    public void ColrVersionOneNestedTransformsUseHarfBuzzCompositionOrder()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using var textRenderer = new TextRenderer(renderer, BoundaryPolicy, 64, 64);
        using var font = new FontFace(TestFontData.CreateColorV1WithNestedTransforms());
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        TextDrawResult result = encoder.DrawText(
            textRenderer,
            font,
            "\U0001F600",
            new Vector2(16, 80),
            64,
            Brush.Solid(Color.White),
            new()
            {
                RenderingMode = TextRenderingMode.VectorPath,
                ColorGlyphMode = ColorGlyphMode.Auto,
                ColorPaletteIndex = 0,
            });
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "COLRv1 nested transforms",
            renderer,
            prepared,
            new RenderTarget(
                target.Handle,
                target.Description,
                GpuAttachmentLoadOperation.Clear,
                ClearColor: new(0, 0, 0, 0)));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        Assert.Equal(1, result.ColorGlyphCount);
        Assert.Equal(0, result.FallbackGlyphCount);
        AssertOpaqueColorNear(pixels, 58, 60, 0, 0, 255);
        AssertPixelNear(pixels, 34, 60, 0, 0, 0, 0);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    [Trait("Category", "TextConformance")]
    public void ColrLayersAndColrGlyphReuseRenderBothLayers(string text)
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using var textRenderer = new TextRenderer(renderer, BoundaryPolicy, 64, 64);
        using var font = new FontFace(TestFontData.CreateColorV1TableCoverage());
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        TextDrawResult result = encoder.DrawText(
            textRenderer,
            font,
            text,
            new Vector2(16, 80),
            64,
            Brush.Solid(new(1, 0, 0)),
            new()
            {
                RenderingMode = TextRenderingMode.VectorPath,
                ColorGlyphMode = ColorGlyphMode.Auto,
                ColorPaletteIndex = 0,
            });
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "COLRv1 shared layers",
            renderer,
            prepared,
            new RenderTarget(
                target.Handle,
                target.Description,
                GpuAttachmentLoadOperation.Clear,
                ClearColor: new(0, 0, 0, 0)));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        Assert.Equal(1, result.ColorGlyphCount);
        Assert.Equal(0, result.FallbackGlyphCount);
        AssertOpaqueColorNear(pixels, 24, 60, 0, 0, 255);
        AssertOpaqueColorNear(pixels, 35, 60, 255, 0, 0);
        AssertPixelNear(pixels, 2, 2, 0, 0, 0, 0);
    }

    [Fact]
    [Trait("Category", "TextConformance")]
    public void ColrClipListConstrainsTheGpuPaintGraph()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using var textRenderer = new TextRenderer(renderer, BoundaryPolicy, 64, 64);
        using var font = new FontFace(TestFontData.CreateColorV1TableCoverage());
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        TextDrawResult result = encoder.DrawText(
            textRenderer,
            font,
            "C",
            new Vector2(16, 80),
            64,
            Brush.Solid(Color.White),
            new()
            {
                RenderingMode = TextRenderingMode.VectorPath,
                ColorGlyphMode = ColorGlyphMode.Auto,
                ColorPaletteIndex = 0,
            });
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "COLRv1 clip list",
            renderer,
            prepared,
            new RenderTarget(
                target.Handle,
                target.Description,
                GpuAttachmentLoadOperation.Clear,
                ClearColor: new(0, 0, 0, 0)));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        Assert.Equal(1, result.ColorGlyphCount);
        Assert.Equal(0, result.FallbackGlyphCount);
        AssertPixelNear(pixels, 24, 60, 0, 0, 0, 0);
        AssertOpaqueColorNear(pixels, 35, 60, 0, 0, 255);
        AssertPixelNear(pixels, 2, 2, 0, 0, 0, 0);
    }

    [Fact]
    [Trait("Category", "TextConformance")]
    public void ColrVariableTranslateUsesTheSelectedAxisOnTheGpu()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using var textRenderer = new TextRenderer(renderer, BoundaryPolicy, 64, 64);
        using var font = new FontFace(
            TestFontData.CreateColorV1TableCoverage(),
            variations: [new FontVariation("wght", 1)]);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        TextDrawResult result = encoder.DrawText(
            textRenderer,
            font,
            "D",
            new Vector2(16, 80),
            64,
            Brush.Solid(Color.White),
            new()
            {
                RenderingMode = TextRenderingMode.VectorPath,
                ColorGlyphMode = ColorGlyphMode.Auto,
                ColorPaletteIndex = 0,
            });
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "COLRv1 variable translate",
            renderer,
            prepared,
            new RenderTarget(
                target.Handle,
                target.Description,
                GpuAttachmentLoadOperation.Clear,
                ClearColor: new(0, 0, 0, 0)));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        Assert.Equal(1, result.ColorGlyphCount);
        Assert.Equal(0, result.FallbackGlyphCount);
        AssertPixelNear(pixels, 35, 60, 0, 0, 0, 0);
        AssertOpaqueColorNear(pixels, 55, 60, 0, 0, 255);
        AssertPixelNear(pixels, 2, 2, 0, 0, 0, 0);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    [InlineData("C")]
    [InlineData("D")]
    [InlineData("E")]
    [Trait("Category", "TextConformance")]
    public void ColrVersionOneAdvancedPaintsRenderWithoutFallback(string text)
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using var textRenderer = new TextRenderer(renderer, BoundaryPolicy, 64, 64);
        using var font = new FontFace(TestFontData.CreateColorV1Features());
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        TextDrawResult result = encoder.DrawText(
            textRenderer,
            font,
            text,
            new Vector2(16, 80),
            64,
            Brush.Solid(new(1, 1, 1)),
            new()
            {
                RenderingMode = TextRenderingMode.VectorPath,
                ColorGlyphMode = ColorGlyphMode.Auto,
                ColorPaletteIndex = 0,
            });
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "advanced COLRv1 text",
            renderer,
            prepared,
            new RenderTarget(
                target.Handle,
                target.Description,
                GpuAttachmentLoadOperation.Clear,
                ClearColor: new(0, 0, 0, 0)));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        Assert.Equal(1, result.ColorGlyphCount);
        Assert.Equal(0, result.BitmapGlyphCount);
        Assert.Equal(0, result.FallbackGlyphCount);
        AssertRenderedArea(pixels);
        AssertPixelNear(pixels, 2, 2, 0, 0, 0, 0);
    }

    [Fact]
    [Trait("Category", "TextConformance")]
    public void BitmapColorGlyphDrawsIndexedCbdtPng()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using var textRenderer = new TextRenderer(renderer, BoundaryPolicy, 64, 64);
        using var font = new FontFace(TestFontData.CreateColorBitmap());
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        TextDrawResult result = encoder.DrawText(
            textRenderer,
            font,
            "\U0001F600",
            new Vector2(20, 50),
            50,
            Brush.Solid(new(1, 0, 1)),
            new() { ColorGlyphMode = ColorGlyphMode.Auto });
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "CBDT color text",
            renderer,
            prepared,
            new RenderTarget(
                target.Handle,
                target.Description,
                GpuAttachmentLoadOperation.Clear,
                ClearColor: new(0, 0, 0, 0)));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        Assert.Equal(1, result.ColorGlyphCount);
        Assert.Equal(1, result.BitmapGlyphCount);
        Assert.Equal(0, result.FallbackGlyphCount);
        AssertOpaqueColorNear(pixels, 27, 42, 255, 0, 0);
        AssertOpaqueColorNear(pixels, 33, 42, 0, 255, 0);
        AssertOpaqueColorNear(pixels, 27, 48, 0, 0, 255);
        AssertPixelNear(pixels, 33, 48, 0, 0, 0, 0, tolerance: 4);
        AssertPixelNear(pixels, 2, 2, 0, 0, 0, 0);
    }

    [Fact]
    [Trait("Category", "TextConformance")]
    public void InvalidColorLayerFallsBackWithoutPartialColor()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using var textRenderer = new TextRenderer(renderer, BoundaryPolicy, 64, 64);
        using var font = new FontFace(TestFontData.CreateColorWithInvalidLayer());
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        TextDrawResult result = encoder.DrawText(
            textRenderer,
            font,
            "\U0001F600",
            new Vector2(16, 80),
            64,
            Brush.Solid(new(1, 0, 0)),
            new()
            {
                RenderingMode = TextRenderingMode.VectorPath,
                ColorGlyphMode = ColorGlyphMode.Auto,
            });
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "invalid color layer",
            renderer,
            prepared,
            new RenderTarget(
                target.Handle,
                target.Description,
                GpuAttachmentLoadOperation.Clear,
                ClearColor: new(0, 0, 0, 0)));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        Assert.Equal(0, result.ColorGlyphCount);
        AssertPixelNear(pixels, 23, 60, 0, 0, 0, 0);
        AssertOpaqueColorNear(pixels, 35, 60, 255, 0, 0);
        AssertPixelNear(pixels, 2, 2, 0, 0, 0, 0);
    }

    [Fact]
    [Trait("Category", "TextConformance")]
    public void MonochromeModeDrawsColorGlyphWithForegroundOnly()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using var textRenderer = new TextRenderer(renderer, BoundaryPolicy, 64, 64);
        using var font = new FontFace(TestFontData.CreateColor());
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        TextDrawResult result = encoder.DrawText(
            textRenderer,
            font,
            "\U0001F600",
            new Vector2(16, 80),
            64,
            Brush.Solid(new(1, 0, 0)),
            new()
            {
                RenderingMode = TextRenderingMode.VectorPath,
                ColorGlyphMode = ColorGlyphMode.Monochrome,
            });
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "monochrome text",
            renderer,
            prepared,
            new RenderTarget(
                target.Handle,
                target.Description,
                GpuAttachmentLoadOperation.Clear,
                ClearColor: new(0, 0, 0, 0)));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        Assert.Equal(0, result.ColorGlyphCount);
        AssertOpaqueColorNear(pixels, 23, 60, 255, 0, 0);
        AssertOpaqueColorNear(pixels, 35, 60, 255, 0, 0);
        AssertPixelNear(pixels, 2, 2, 0, 0, 0, 0);
    }

    protected abstract IGpuBackend CreateBackend();

    private static GpuTextureDescription TargetDescription() => new(
        Width,
        Height,
        GpuFormat.Rgba8Unorm,
        GpuTextureUsage.ColorAttachment | GpuTextureUsage.CopySource);

    private static byte[] ReadPixels(IGpuBackend backend, GpuTextureHandle texture)
    {
        var footprint = new GpuTextureCopyFootprint(Width, Height, 4, RowPitch);
        if ((backend.Capabilities & GpuBackendCapabilities.DeviceOwnedResources) != 0)
        {
            return backend.ReadTexture(texture, footprint);
        }

        var description = new GpuBufferDescription(ByteCount, GpuBufferUsage.CopyDestination);
        GpuBufferMemoryRequirements requirements = backend.GetBufferMemoryRequirements(description);
        GpuMemoryAllocation allocation = backend.AllocateMemory(
            requirements.Size,
            requirements.Alignment,
            GpuMemoryKind.HostCached,
            requirements.Compatibility);
        GpuBufferHandle readback = default;
        try
        {
            readback = backend.CreatePlacedBuffer(description, allocation);
            GpuCommandBuffer commands = backend.MainQueue.StartCommandRecording()
                .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
                .CopyTextureToMemory(
                    texture,
                    backend.GetBufferMemoryAddress(readback, 0, ByteCount),
                    footprint);
            Submit(backend, commands);
            return allocation.MappedBytes()[..checked((int)ByteCount)].ToArray();
        }
        finally
        {
            if (!readback.IsNull)
            {
                backend.DestroyBuffer(readback);
            }
            backend.FreeMemory(allocation);
        }
    }

    private static void Submit(IGpuBackend backend, GpuCommandBuffer commands)
    {
        using GpuSemaphore completion = backend.MainQueue.CreateSemaphore();
        backend.MainQueue.Submit([commands], completion, 1);
        backend.MainQueue.Wait(completion, 1);
    }

    private static void AssertGreenPixelNear(ReadOnlySpan<byte> pixels, int expectedX, int expectedY)
    {
        byte maximumGreen = 0;
        byte maximumAlpha = 0;
        bool found = false;
        for (int y = Math.Max(0, expectedY - 1); y <= Math.Min((int)Height - 1, expectedY + 1); y++)
        {
            for (int x = Math.Max(0, expectedX - 1); x <= Math.Min((int)Width - 1, expectedX + 1); x++)
            {
                int offset = checked((int)(y * RowPitch + x * 4));
                byte green = pixels[offset + 1];
                byte alpha = pixels[offset + 3];
                maximumGreen = Math.Max(maximumGreen, green);
                maximumAlpha = Math.Max(maximumAlpha, alpha);
                if (green >= 16 && alpha >= 16)
                {
                    found = true;
                }
            }
        }

        Assert.True(
            found,
            $"Expected a rendered green pixel near ({expectedX}, {expectedY}), "
                + $"but maximum green/alpha were {maximumGreen}/{maximumAlpha}.");
    }

    private static void AssertRenderedArea(ReadOnlySpan<byte> pixels)
    {
        int renderedPixelCount = 0;
        for (int offset = 3; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset] >= 16)
            {
                renderedPixelCount++;
            }
        }

        Assert.True(
            renderedPixelCount >= 64,
            $"Expected at least 64 rendered pixels, but found {renderedPixelCount}.");
    }

    private static void AssertOpaqueColorNear(
        ReadOnlySpan<byte> pixels,
        int expectedX,
        int expectedY,
        byte expectedRed,
        byte expectedGreen,
        byte expectedBlue)
    {
        byte[] expected = [expectedRed, expectedGreen, expectedBlue, 255];
        byte[] closest = new byte[4];
        int closestDistance = int.MaxValue;
        bool found = false;
        for (int y = Math.Max(0, expectedY - 1); y <= Math.Min((int)Height - 1, expectedY + 1); y++)
        {
            for (int x = Math.Max(0, expectedX - 1); x <= Math.Min((int)Width - 1, expectedX + 1); x++)
            {
                int offset = checked((int)(y * RowPitch + x * 4));
                ReadOnlySpan<byte> actual = pixels.Slice(offset, 4);
                int distance = 0;
                for (int channel = 0; channel < 4; channel++)
                {
                    distance += Math.Abs(actual[channel] - expected[channel]);
                }
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    actual.CopyTo(closest);
                }
                if (distance <= 16)
                {
                    found = true;
                }
            }
        }

        Assert.True(
            found,
            $"Expected an opaque [{string.Join(", ", expected)}] pixel near ({expectedX}, {expectedY}), "
                + $"but the closest was [{string.Join(", ", closest)}].");
    }

    private static void AssertPixelNear(
        ReadOnlySpan<byte> pixels,
        int x,
        int y,
        byte red,
        byte green,
        byte blue,
        byte alpha,
        int tolerance = 1)
    {
        int offset = checked((int)(y * RowPitch + x * 4));
        byte[] actual = pixels.Slice(offset, 4).ToArray();
        byte[] expected = [red, green, blue, alpha];
        bool matches = actual.Zip(expected).All(pair => Math.Abs(pair.First - pair.Second) <= tolerance);
        Assert.True(
            matches,
            $"Pixel ({x}, {y}) expected [{string.Join(", ", expected)}] "
                + $"within {tolerance}, but was [{string.Join(", ", actual)}].");
    }

    private sealed class BackendTexture : IDisposable
    {
        private readonly IGpuBackend backend;
        private GpuMemoryAllocation allocation;

        private BackendTexture(
            IGpuBackend backend,
            GpuTextureHandle handle,
            GpuTextureDescription description,
            GpuMemoryAllocation allocation)
        {
            this.backend = backend;
            Handle = handle;
            Description = description;
            this.allocation = allocation;
        }

        internal GpuTextureHandle Handle { get; }
        internal GpuTextureDescription Description { get; }

        internal static BackendTexture Create(
            IGpuBackend backend,
            GpuTextureDescription description)
        {
            if ((backend.Capabilities & GpuBackendCapabilities.DeviceOwnedResources) != 0)
            {
                return new(backend, backend.CreateTexture(description), description, default);
            }

            GpuTextureMemoryRequirements requirements = backend.GetTextureMemoryRequirements(description);
            GpuMemoryAllocation allocation = backend.AllocateMemory(
                requirements.Size,
                requirements.Alignment,
                GpuMemoryKind.DeviceLocal,
                requirements.Compatibility);
            try
            {
                return new(
                    backend,
                    backend.CreatePlacedTexture(description, allocation),
                    description,
                    allocation);
            }
            catch
            {
                backend.FreeMemory(allocation);
                throw;
            }
        }

        public void Dispose()
        {
            backend.DestroyTexture(Handle);
            if (!allocation.MemoryAddress.IsNull)
            {
                backend.FreeMemory(allocation);
                allocation = default;
            }
        }
    }
}
