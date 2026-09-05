using System.Numerics;

using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.TwoD.Tests;

public abstract class BackendConformanceTests
{
    private const uint Width = 64;
    private const uint Height = 64;
    private const ulong RowPitch = Width * 4;
    private const ulong ByteCount = RowPitch * Height;

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void PrimitiveAndPolygonRoutesRenderWithoutGeometryBindings()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.FillRectangle(new(4, 4, 24, 24), Brush.Solid(new(1, 0, 0)));
        encoder.FillRoundedRectangle(new(36, 4, 24, 24), new(8), Brush.Solid(new(0, 1, 0)));
        encoder.DrawLine(new(8, 32), new(56, 32), 4, Brush.Solid(Color.White));
        encoder.FillEllipse(new(4, 36, 24, 24), Brush.Solid(new(0, 0, 1)));
        encoder.DrawGeometry(
            PolygonGeometry.FromConvexPolygon([
                new(36, 60),
                new(60, 60),
                new(48, 36),
            ]),
            Matrix3x2.Identity,
            Brush.Solid(new(1, 1, 0)));
        DisplayList displayList = encoder.Finish();
        using PreparedDisplayList prepared = renderer.Prepare(displayList, target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "two-d",
            renderer,
            prepared,
            new RenderTarget(
                target.Handle,
                target.Description,
                GpuAttachmentLoadOperation.Clear,
                ClearColor: new(0, 0, 0, 0)));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        AssertPixelNear(pixels, 16, 16, 255, 0, 0, 255);
        AssertPixelNear(pixels, 48, 16, 0, 255, 0, 255);
        AssertPixelNear(pixels, 32, 32, 255, 255, 255, 255);
        AssertPixelNear(pixels, 16, 48, 0, 0, 255, 255);
        AssertPixelNear(pixels, 48, 48, 255, 255, 0, 255);
        AssertPixelNear(pixels, 1, 1, 0, 0, 0, 0);
        AssertPixelNear(pixels, 36, 4, 0, 0, 0, 0);
        AssertPixelNear(pixels, 4, 36, 0, 0, 0, 0);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void ClipPreservesPainterOrderWithinItsBounds()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.FillRectangle(new(0, 0, Width, Height), Brush.Solid(new(1, 0, 0)));
        encoder.Save();
        encoder.Clip(new(0, 0, 32, Height));
        encoder.FillRectangle(new(0, 0, Width, Height), Brush.Solid(new(0, 0, 1)));
        encoder.Restore();
        DisplayList displayList = encoder.Finish();
        using PreparedDisplayList prepared = renderer.Prepare(displayList, target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "two-d",
            renderer,
            prepared,
            new RenderTarget(target.Handle, target.Description, GpuAttachmentLoadOperation.Clear));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        AssertPixelNear(pixels, 16, 16, 0, 0, 255, 255);
        AssertPixelNear(pixels, 48, 16, 255, 0, 0, 255);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void IsolatedLayerCompositesOpacityAcrossTheBackend()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.PushLayer(new() { Opacity = 0.5f });
        encoder.FillRectangle(new(0, 0, Width, Height), Brush.Solid(new(1, 0, 0)));
        encoder.PopLayer();
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "two-d-layer",
            renderer,
            prepared,
            new RenderTarget(
                target.Handle,
                target.Description,
                GpuAttachmentLoadOperation.Clear,
                ClearColor: new(0, 0, 1, 1)));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        AssertPixelNear(pixels, 32, 32, 128, 0, 127, 255, tolerance: 3);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void LayerMaskControlsCompositeCoverageAcrossTheBackend()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        var maskDescription = new GpuTextureDescription(
            2,
            2,
            GpuFormat.Rgba8Unorm,
            GpuTextureUsage.Sampled | GpuTextureUsage.CopyDestination);
        using var mask = BackendTexture.Create(backend, maskDescription);
        UploadTexture(backend, mask.Handle, mask.Description, [
            0, 0, 0, 128, 0, 0, 0, 128,
            0, 0, 0, 128, 0, 0, 0, 128,
        ]);
        SamplerId sampler = backend.CreateSampler(new(
            GpuSamplerFilter.Linear,
            GpuSamplerFilter.Linear));
        try
        {
            using var renderer = new Renderer(backend);
            ImageId maskId = renderer.RegisterImage(mask.Handle, mask.Description, sampler);
            using CommandEncoder encoder = renderer.CreateCommandEncoder();
            encoder.PushLayer(new() { Mask = maskId });
            encoder.FillRectangle(new(0, 0, Width, Height), Brush.Solid(new(1, 0, 0)));
            encoder.PopLayer();
            using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
            var graph = new GpuRenderGraph();
            graph.AddTwoD(
                "two-d-mask",
                renderer,
                prepared,
                new RenderTarget(
                    target.Handle,
                    target.Description,
                    GpuAttachmentLoadOperation.Clear,
                    ClearColor: new(0, 0, 1, 1)));

            using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
            byte[] pixels = ReadPixels(backend, target.Handle);

            AssertPixelNear(pixels, 32, 32, 128, 0, 127, 255, tolerance: 3);
        }
        finally
        {
            backend.DestroySampler(sampler);
        }
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void LayerShadowUsesTheBlurAndOffsetAcrossTheBackend()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.PushLayer(new()
        {
            Shadow = new(new(24, 0), new(1, 0, 0), 1),
        });
        encoder.FillRectangle(new(8, 8, 16, 16), Brush.Solid(Color.White));
        encoder.PopLayer();
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "two-d-shadow",
            renderer,
            prepared,
            new RenderTarget(
                target.Handle,
                target.Description,
                GpuAttachmentLoadOperation.Clear,
                ClearColor: new(0, 0, 0, 0)));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        AssertPixelNear(pixels, 16, 16, 255, 255, 255, 255, tolerance: 3);
        AssertPixelNear(pixels, 40, 16, 255, 0, 0, 255, tolerance: 4);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void AdditiveLayerUsesItsBlendModeAcrossTheBackend()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.PushLayer(new() { BlendMode = BlendMode.Additive });
        encoder.FillRectangle(new(0, 0, Width, Height), Brush.Solid(new(1, 0, 0, 0.5f)));
        encoder.PopLayer();
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "two-d-additive",
            renderer,
            prepared,
            new RenderTarget(
                target.Handle,
                target.Description,
                GpuAttachmentLoadOperation.Clear,
                ClearColor: new(0, 0, 1, 1)));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        AssertPixelNear(pixels, 32, 32, 128, 0, 255, 255, tolerance: 3);
    }

    [Theory]
    [InlineData(BlendMode.Multiply, 51, 51, 38)]
    [InlineData(BlendMode.Screen, 217, 179, 204)]
    [Trait("Category", "TwoDConformance")]
    public void LayerUsesSeparableBlendModesAcrossTheBackend(
        BlendMode blendMode,
        byte red,
        byte green,
        byte blue)
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.PushLayer(new() { BlendMode = blendMode });
        encoder.FillRectangle(new(0, 0, Width, Height), Brush.Solid(new(0.8f, 0.4f, 0.2f)));
        encoder.PopLayer();
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "two-d-blend",
            renderer,
            prepared,
            new RenderTarget(
                target.Handle,
                target.Description,
                GpuAttachmentLoadOperation.Clear,
                ClearColor: new(0.25f, 0.5f, 0.75f, 1)));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        AssertPixelNear(pixels, 32, 32, red, green, blue, 255, tolerance: 3);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void ImageRouteUsesPremultipliedSourceOverBlending()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        var imageDescription = new GpuTextureDescription(
            2,
            2,
            GpuFormat.Rgba8Unorm,
            GpuTextureUsage.Sampled | GpuTextureUsage.CopyDestination);
        using var image = BackendTexture.Create(backend, imageDescription);
        byte[] texels =
        [
            255, 0, 0, 128, 255, 0, 0, 128,
            255, 0, 0, 128, 255, 0, 0, 128,
        ];
        UploadTexture(backend, image.Handle, image.Description, texels);
        SamplerId sampler = backend.CreateSampler(default);
        try
        {
            using var renderer = new Renderer(backend);
            ImageId registered = renderer.RegisterImage(image.Handle, image.Description, sampler);
            using CommandEncoder encoder = renderer.CreateCommandEncoder();
            encoder.DrawImage(registered, new(0, 0, Width, Height));
            DisplayList displayList = encoder.Finish();
            using PreparedDisplayList prepared = renderer.Prepare(displayList, target.Description);
            var graph = new GpuRenderGraph();
            graph.AddTwoD(
                "two-d",
                renderer,
                prepared,
                new RenderTarget(
                    target.Handle,
                    target.Description,
                    GpuAttachmentLoadOperation.Clear,
                    ClearColor: new(0, 0, 1, 1)));

            using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
            byte[] pixels = ReadPixels(backend, target.Handle);

            AssertPixelNear(pixels, 32, 32, 128, 0, 127, 255, tolerance: 2);
        }
        finally
        {
            backend.DestroySampler(sampler);
        }
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void SolidBrushUsesPremultipliedSourceOverBlending()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.FillRectangle(
            new(0, 0, Width, Height),
            Brush.Solid(new(1, 0, 0, 0.5f)));
        DisplayList displayList = encoder.Finish();
        using PreparedDisplayList prepared = renderer.Prepare(displayList, target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "two-d",
            renderer,
            prepared,
            new RenderTarget(
                target.Handle,
                target.Description,
                GpuAttachmentLoadOperation.Clear,
                ClearColor: new(0, 0, 1, 1)));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        AssertPixelNear(pixels, 32, 32, 128, 0, 127, 255, tolerance: 2);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void PartialBufferWritesPreserveUnaffectedBytes()
    {
        using IGpuBackend backend = CreateBackend();
        byte[] initial = Enumerable.Range(0, 16).Select(static value => (byte)value).ToArray();
        byte[] replacement = [40, 41, 42, 43];
        byte[] expected = [0, 1, 2, 3, 40, 41, 42, 43, 8, 9, 10, 11, 12, 13, 14, 15];

        byte[] actual = WriteAndReadBuffer(backend, initial, replacement, 4);

        Assert.Equal(expected, actual);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void RetainedSceneUploadsOnlyChangedNodeAndRendersLatestState()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        var scene = new Scene();
        NodeId left = scene.CreateNode();
        scene.SetContent(left, SceneContent.Rectangle(new(0, 0, 32, Height), Brush.Solid(new(1, 0, 0))));
        NodeId right = scene.CreateNode();
        scene.SetContent(right, SceneContent.Rectangle(new(32, 0, 32, Height), Brush.Solid(new(0, 0, 1))));
        using SceneSnapshot snapshot = renderer.Prepare(scene, target.Description);
        SceneUpdateStatistics initial = snapshot.LastUpdate;

        scene.SetContent(right, SceneContent.Rectangle(new(32, 0, 32, Height), Brush.Solid(new(0, 1, 0))));
        SceneUpdateStatistics changed = snapshot.Update();
        SceneUpdateStatistics unchanged = snapshot.Update();
        scene.SetOrder(left, 10);
        SceneUpdateStatistics reordered = snapshot.Update();
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "retained",
            renderer,
            snapshot,
            new RenderTarget(target.Handle, target.Description, GpuAttachmentLoadOperation.Clear));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        Assert.Equal(new(2, 256, true), initial);
        Assert.Equal(new(1, 128, false), changed);
        Assert.Equal(default, unchanged);
        Assert.Equal(default, reordered);
        AssertPixelNear(pixels, 16, 32, 255, 0, 0, 255);
        AssertPixelNear(pixels, 48, 32, 0, 255, 0, 255);
    }

    [Theory]
    [InlineData(DistanceFieldEncoding.Coverage)]
    [InlineData(DistanceFieldEncoding.SignedDistance)]
    [InlineData(DistanceFieldEncoding.MultiChannelSignedDistance)]
    [Trait("Category", "TwoDConformance")]
    public void GpuRasterizedDistanceFieldRendersThroughExplicitRoute(DistanceFieldEncoding encoding)
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        GpuFormat atlasFormat = encoding == DistanceFieldEncoding.MultiChannelSignedDistance
            ? GpuFormat.Rgba8Unorm
            : GpuFormat.R8Unorm;
        using var atlas = new DistanceFieldAtlas(backend, 64, 64, atlasFormat);
        using var rasterizer = new DistanceFieldRasterizer(backend, atlas);
        PathGeometry path = new PathBuilder()
            .MoveTo(new(0, 0))
            .LineTo(new(1, 0))
            .LineTo(new(1, 1))
            .LineTo(new(0, 1))
            .Close()
            .Build();
        DistanceField field = rasterizer.Rasterize(
            path,
            32,
            32,
            new() { Encoding = encoding });
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.DrawDistanceField(field, new(8, 8, 48, 48), Brush.Solid(new(1, 0.5f, 0)));
        DisplayList displayList = encoder.Finish();
        using PreparedDisplayList prepared = renderer.Prepare(displayList, target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "distance-field",
            renderer,
            prepared,
            new RenderTarget(target.Handle, target.Description, GpuAttachmentLoadOperation.Clear));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        AssertPixelNear(pixels, 32, 32, 255, 128, 0, 255, tolerance: 3);
        AssertPixelNear(pixels, 9, 9, 0, 0, 0, 0, tolerance: 3);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void AtlasWaitsForFenceBeforeCollectingReleasedRegion()
    {
        using IGpuBackend backend = CreateBackend();
        using var atlas = new DistanceFieldAtlas(backend, 32, 32);
        using var rasterizer = new DistanceFieldRasterizer(backend, atlas);
        PathGeometry path = new PathBuilder()
            .MoveTo(new(0, 0))
            .LineTo(new(1, 0))
            .LineTo(new(0, 1))
            .Close()
            .Build();
        DistanceField field = rasterizer.Rasterize(path, 16, 16);

        atlas.Release(field, new(5));
        int early = atlas.Collect(new(4));
        int completed = atlas.Collect(new(5));

        Assert.Equal(0, early);
        Assert.Equal(1, completed);
        Assert.Equal(0, atlas.PendingRetirementCount);
        Assert.False(atlas.IsAlive(field));
        Assert.Throws<ArgumentException>(() => encoderUse(field));

        void encoderUse(DistanceField stale)
        {
            using var renderer = new Renderer(backend);
            using CommandEncoder encoder = renderer.CreateCommandEncoder();
            encoder.DrawDistanceField(stale, new(0, 0, 8, 8), Brush.Solid(Color.White));
        }
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void VectorPathRendersLinearGradientThroughTiledCoverage()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        PathGeometry path = RectanglePath(4, 4, 56, 56);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.DrawPath(
            path,
            Matrix3x2.Identity,
            Brush.LinearGradient(new(4, 4), new(60, 4), new(1, 0, 0), new(0, 0, 1)));
        DisplayList displayList = encoder.Finish();
        using PreparedDisplayList prepared = renderer.Prepare(displayList, target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "path",
            renderer,
            prepared,
            new RenderTarget(target.Handle, target.Description, GpuAttachmentLoadOperation.Clear));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        AssertPixelNear(pixels, 11, 32, 223, 0, 32, 255, tolerance: 5);
        AssertPixelNear(pixels, 53, 32, 32, 0, 223, 255, tolerance: 5);
        AssertPixelNear(pixels, 1, 1, 0, 0, 0, 0);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void CurvedPathIsExpandedByComputeBeforeRasterization()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        PathGeometry path = new PathBuilder()
            .MoveTo(new(8, 32))
            .CubicTo(new(8, 8), new(56, 8), new(56, 32))
            .QuadraticTo(new(32, 58), new(8, 32))
            .Build();
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.DrawPath(path, Matrix3x2.Identity, Brush.Solid(new(0, 1, 0)));
        DisplayList displayList = encoder.Finish();
        using PreparedDisplayList prepared = renderer.Prepare(displayList, target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "curved-path",
            renderer,
            prepared,
            new RenderTarget(target.Handle, target.Description, GpuAttachmentLoadOperation.Clear));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        AssertPixelNear(pixels, 32, 32, 0, 255, 0, 255, tolerance: 4);
        AssertPixelNear(pixels, 4, 4, 0, 0, 0, 0);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void PathClipRestrictsStrokeAndFillCoverage()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        PathGeometry path = RectanglePath(4, 4, 56, 56);
        PathGeometry clip = RectanglePath(16, 16, 32, 32);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.DrawPath(
            path,
            Matrix3x2.Identity,
            Brush.Solid(new(0, 1, 0)),
            clip: new(clip, Matrix3x2.Identity));
        PathGeometry line = new PathBuilder().MoveTo(new(8, 52)).LineTo(new(56, 52)).Build();
        encoder.StrokePath(
            line,
            Matrix3x2.Identity,
            new StrokeStyle(4),
            Brush.Solid(Color.White));
        DisplayList displayList = encoder.Finish();
        using PreparedDisplayList prepared = renderer.Prepare(displayList, target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "clipped-path",
            renderer,
            prepared,
            new RenderTarget(target.Handle, target.Description, GpuAttachmentLoadOperation.Clear));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        AssertPixelNear(pixels, 32, 32, 0, 255, 0, 255);
        AssertPixelNear(pixels, 8, 8, 0, 0, 0, 0);
        AssertPixelNear(pixels, 32, 52, 255, 255, 255, 255);
        AssertPixelNear(pixels, 32, 48, 0, 0, 0, 0);
    }

    protected abstract IGpuBackend CreateBackend();

    private static PathGeometry RectanglePath(float x, float y, float width, float height)
        => new PathBuilder()
            .MoveTo(new(x, y))
            .LineTo(new(x + width, y))
            .LineTo(new(x + width, y + height))
            .LineTo(new(x, y + height))
            .Close()
            .Build();

    private static GpuTextureDescription TargetDescription() => new(
        Width,
        Height,
        GpuFormat.Rgba8Unorm,
        GpuTextureUsage.ColorAttachment | GpuTextureUsage.CopySource);

    private static void UploadTexture(
        IGpuBackend backend,
        GpuTextureHandle texture,
        GpuTextureDescription description,
        ReadOnlySpan<byte> texels)
    {
        var footprint = new GpuTextureCopyFootprint(
            description.Width,
            description.Height,
            4,
            description.Width * 4);
        if ((backend.Capabilities & GpuBackendCapabilities.DeviceOwnedResources) != 0)
        {
            backend.WriteTexture(texture, texels, footprint);
            return;
        }

        var bufferDescription = new GpuBufferDescription(
            checked((ulong)texels.Length),
            GpuBufferUsage.CopySource);
        GpuBufferMemoryRequirements requirements = backend.GetBufferMemoryRequirements(bufferDescription);
        GpuMemoryAllocation allocation = backend.AllocateMemory(
            requirements.Size,
            requirements.Alignment,
            GpuMemoryKind.HostMapped,
            requirements.Compatibility);
        GpuBufferHandle upload = default;
        try
        {
            upload = backend.CreatePlacedBuffer(bufferDescription, allocation);
            backend.WriteBuffer(upload, texels);
            GpuCommandBuffer commands = backend.MainQueue.StartCommandRecording()
                .CopyMemoryToTexture(
                    backend.GetBufferMemoryAddress(upload, 0, checked((ulong)texels.Length)),
                    texture,
                    footprint)
                .Barrier(GpuStage.Copy, GpuStage.PixelShader);
            Submit(backend, commands);
        }
        finally
        {
            if (!upload.IsNull) { backend.DestroyBuffer(upload); }
            backend.FreeMemory(allocation);
        }
    }

    private static byte[] ReadPixels(IGpuBackend backend, GpuTextureHandle texture)
    {
        if ((backend.Capabilities & GpuBackendCapabilities.DeviceOwnedResources) != 0)
        {
            return backend.ReadTexture(texture, new(Width, Height, 4, RowPitch));
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
                    new(Width, Height, 4, RowPitch));
            Submit(backend, commands);
            return allocation.MappedBytes()[..checked((int)ByteCount)].ToArray();
        }
        finally
        {
            if (!readback.IsNull) { backend.DestroyBuffer(readback); }
            backend.FreeMemory(allocation);
        }
    }

    private static byte[] WriteAndReadBuffer(
        IGpuBackend backend,
        ReadOnlySpan<byte> initial,
        ReadOnlySpan<byte> replacement,
        ulong destinationOffset)
    {
        var description = new GpuBufferDescription(
            checked((ulong)initial.Length),
            GpuBufferUsage.ShaderData | GpuBufferUsage.CopySource | GpuBufferUsage.CopyDestination);
        if ((backend.Capabilities & GpuBackendCapabilities.DeviceOwnedResources) != 0)
        {
            GpuBufferHandle buffer = backend.CreateBuffer(description);
            try
            {
                backend.WriteBuffer(buffer, initial);
                backend.WriteBuffer(buffer, destinationOffset, replacement);
                return backend.ReadBuffer(buffer);
            }
            finally
            {
                backend.DestroyBuffer(buffer);
            }
        }

        GpuBufferMemoryRequirements requirements = backend.GetBufferMemoryRequirements(description);
        GpuMemoryAllocation allocation = backend.AllocateMemory(
            requirements.Size,
            requirements.Alignment,
            GpuMemoryKind.HostMapped,
            requirements.Compatibility);
        GpuBufferHandle placedBuffer = default;
        try
        {
            placedBuffer = backend.CreatePlacedBuffer(description, allocation);
            backend.WriteBuffer(placedBuffer, initial);
            backend.WriteBuffer(placedBuffer, destinationOffset, replacement);
            return allocation.MappedBytes()[..initial.Length].ToArray();
        }
        finally
        {
            if (!placedBuffer.IsNull) { backend.DestroyBuffer(placedBuffer); }
            backend.FreeMemory(allocation);
        }
    }

    private static void Submit(IGpuBackend backend, GpuCommandBuffer commands)
    {
        using GpuSemaphore completion = backend.MainQueue.CreateSemaphore();
        backend.MainQueue.Submit([commands], completion, 1);
        backend.MainQueue.Wait(completion, 1);
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
        int offset = checked((y * (int)Width + x) * 4);
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

        public GpuTextureHandle Handle { get; }
        public GpuTextureDescription Description { get; }

        public static BackendTexture Create(
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
