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
    public void CachedPolygonRendersWithoutGeometryBindings()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.DrawGeometry(
            PolygonGeometry.FromConvexPolygon([
                new(8, 56),
                new(56, 56),
                new(32, 8),
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

        AssertPixelNear(pixels, 32, 32, 255, 255, 0, 255);
        AssertPixelNear(pixels, 1, 1, 0, 0, 0, 0);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void RoundedRectangleRendersAcrossTheBackend()
    {
        byte[] pixels = Render(static encoder =>
            encoder.FillRoundedRectangle(
                new(8, 8, 48, 48),
                new(12),
                Brush.Solid(new(0, 1, 0))));

        AssertPixelNear(pixels, 32, 32, 0, 255, 0, 255);
        AssertPixelNear(pixels, 8, 8, 0, 0, 0, 0);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void EllipseRendersAcrossTheBackend()
    {
        byte[] pixels = Render(static encoder =>
            encoder.FillEllipse(new(8, 16, 48, 32), Brush.Solid(new(0, 0, 1))));

        AssertPixelNear(pixels, 32, 32, 0, 0, 255, 255);
        AssertPixelNear(pixels, 8, 16, 0, 0, 0, 0);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void LineRendersAcrossTheBackend()
    {
        byte[] pixels = Render(static encoder =>
            encoder.DrawLine(new(8, 32), new(56, 32), 4, Brush.Solid(Color.White)));

        AssertPixelNear(pixels, 32, 32, 255, 255, 255, 255);
        AssertPixelNear(pixels, 32, 24, 0, 0, 0, 0);
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
    public void LayerShadowUsesItsColorAndOffsetAcrossTheBackend()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.PushLayer(new()
        {
            Shadow = new(new(24, 0), new(1, 0, 0)),
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
    public void LayerBlurFiltersItsContentsAcrossTheBackend()
    {
        byte[] pixels = Render(static encoder =>
        {
            encoder.PushLayer(new() { BlurRadius = 2 });
            encoder.FillRectangle(new(8, 8, 16, 16), Brush.Solid(new(1, 0, 0)));
            encoder.PopLayer();
        });

        AssertPixelNear(pixels, 16, 16, 255, 0, 0, 255, tolerance: 4);
        AssertRedPixelInRange(pixels, 6, 16, 32, 160);
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

    [Theory]
    [InlineData(CompositeMode.Clear)]
    [InlineData(CompositeMode.Source)]
    [InlineData(CompositeMode.Destination)]
    [InlineData(CompositeMode.SourceOver)]
    [InlineData(CompositeMode.DestinationOver)]
    [InlineData(CompositeMode.SourceIn)]
    [InlineData(CompositeMode.DestinationIn)]
    [InlineData(CompositeMode.SourceOut)]
    [InlineData(CompositeMode.DestinationOut)]
    [InlineData(CompositeMode.SourceAtop)]
    [InlineData(CompositeMode.DestinationAtop)]
    [InlineData(CompositeMode.Xor)]
    [InlineData(CompositeMode.Plus)]
    [InlineData(CompositeMode.Screen)]
    [InlineData(CompositeMode.Overlay)]
    [InlineData(CompositeMode.Darken)]
    [InlineData(CompositeMode.Lighten)]
    [InlineData(CompositeMode.ColorDodge)]
    [InlineData(CompositeMode.ColorBurn)]
    [InlineData(CompositeMode.HardLight)]
    [InlineData(CompositeMode.SoftLight)]
    [InlineData(CompositeMode.Difference)]
    [InlineData(CompositeMode.Exclusion)]
    [InlineData(CompositeMode.Multiply)]
    [InlineData(CompositeMode.HslHue)]
    [InlineData(CompositeMode.HslSaturation)]
    [InlineData(CompositeMode.HslColor)]
    [InlineData(CompositeMode.HslLuminosity)]
    [Trait("Category", "TwoDConformance")]
    public void CompositeModesMatchPremultipliedReferenceAcrossTheBackend(CompositeMode mode)
    {
        var backdrop = new Color(0.2f, 0.6f, 0.8f, 0.7f);
        var source = new Color(0.9f, 0.3f, 0.1f, 0.6f);
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.FillRectangle(new(0, 0, Width, Height), Brush.Solid(backdrop));
        encoder.PushLayer(new() { CompositeMode = mode });
        encoder.FillRectangle(new(0, 0, Width, Height), Brush.Solid(source));
        encoder.PopLayer();
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "composite-mode",
            renderer,
            prepared,
            new RenderTarget(
                target.Handle,
                target.Description,
                GpuAttachmentLoadOperation.Clear));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);
        Vector4 expected = CompositeReference(mode, Premultiply(source), Premultiply(backdrop));

        AssertPixelNear(
            pixels,
            32,
            32,
            ToByte(expected.X),
            ToByte(expected.Y),
            ToByte(expected.Z),
            ToByte(expected.W),
            tolerance: 4);
    }

    [Theory]
    [InlineData(CompositeMode.Clear)]
    [InlineData(CompositeMode.Source)]
    [InlineData(CompositeMode.SourceIn)]
    [InlineData(CompositeMode.DestinationIn)]
    [InlineData(CompositeMode.DestinationAtop)]
    [Trait("Category", "TwoDConformance")]
    public void EmptyCompositeLayerPreservesItsTransparentSourceSemantics(CompositeMode mode)
    {
        byte[] pixels = Render(encoder =>
        {
            encoder.FillRectangle(new(0, 0, Width, Height), Brush.Solid(new(0, 1, 0)));
            encoder.PushLayer(new() { CompositeMode = mode });
            encoder.PopLayer();
        });

        AssertPixelNear(pixels, 32, 32, 0, 0, 0, 0);
    }

    [Theory]
    [InlineData(CompositeMode.Clear)]
    [InlineData(CompositeMode.Source)]
    [Trait("Category", "TwoDConformance")]
    public void EmptyCompositeLayerRemainsBetweenCompatibleDraws(CompositeMode mode)
    {
        byte[] pixels = Render(encoder =>
        {
            encoder.FillRectangle(new(0, 0, Width, Height), Brush.Solid(new(0, 1, 0)));
            encoder.PushLayer(new() { CompositeMode = mode });
            encoder.PopLayer();
            encoder.FillRectangle(new(0, 0, 16, 16), Brush.Solid(new(1, 0, 0)));
        });

        AssertPixelNear(pixels, 8, 8, 255, 0, 0, 255);
        AssertPixelNear(pixels, 32, 32, 0, 0, 0, 0);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void MultipleEmptyCompositeLayersRemainBeforeTheFollowingDraw()
    {
        byte[] pixels = Render(encoder =>
        {
            encoder.FillRectangle(new(0, 0, Width, Height), Brush.Solid(new(0, 1, 0)));
            encoder.PushLayer(new() { CompositeMode = CompositeMode.Clear });
            encoder.PopLayer();
            encoder.PushLayer(new() { CompositeMode = CompositeMode.Source });
            encoder.PopLayer();
            encoder.FillRectangle(new(0, 0, 16, 16), Brush.Solid(new(1, 0, 0)));
        });

        AssertPixelNear(pixels, 8, 8, 255, 0, 0, 255);
        AssertPixelNear(pixels, 32, 32, 0, 0, 0, 0);
    }

    [Theory]
    [InlineData(CompositeMode.Clear)]
    [InlineData(CompositeMode.Source)]
    [InlineData(CompositeMode.SourceIn)]
    [Trait("Category", "TwoDConformance")]
    public void EmptyUnboundedCompositeIsRestrictedByAPathClip(CompositeMode mode)
    {
        byte[] pixels = Render(encoder =>
        {
            encoder.FillRectangle(new(0, 0, Width, Height), Brush.Solid(new(0, 1, 0)));
            encoder.PushClip(RectanglePath(16, 16, 32, 32));
            encoder.PushLayer(new() { CompositeMode = mode });
            encoder.PopLayer();
            encoder.PopClip();
        });

        AssertPixelNear(pixels, 32, 32, 0, 0, 0, 0);
        AssertPixelNear(pixels, 8, 8, 0, 255, 0, 255);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void EmptyClearCompositeUsesExactRotatedRectangleClipCoverage()
    {
        byte[] pixels = Render(encoder =>
        {
            encoder.FillRectangle(new(0, 0, Width, Height), Brush.Solid(new(0, 1, 0)));
            encoder.SetTransform(Matrix3x2.CreateRotation(MathF.PI / 4, new(32, 32)));
            encoder.PushClip(new Rect(20, 20, 24, 24));
            encoder.SetTransform(Matrix3x2.Identity);
            encoder.PushLayer(new() { CompositeMode = CompositeMode.Clear });
            encoder.PopLayer();
            encoder.PopClip();
        });

        AssertPixelNear(pixels, 32, 32, 0, 0, 0, 0);
        AssertPixelNear(pixels, 20, 20, 0, 255, 0, 255, tolerance: 3);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void PathClippedClearCompositeAppliesEdgeCoverageOnce()
    {
        byte[] pixels = Render(encoder =>
        {
            encoder.FillRectangle(new(0, 0, Width, Height), Brush.Solid(new(0, 1, 0)));
            encoder.PushClip(RectanglePath(16.25f, 16.25f, 31.5f, 31.5f));
            encoder.PushLayer(new() { CompositeMode = CompositeMode.Clear });
            encoder.PopLayer();
            encoder.PopClip();
        });

        AssertPixelNear(pixels, 16, 32, 0, 64, 0, 64, tolerance: 10);
        AssertPixelNear(pixels, 15, 32, 0, 255, 0, 255, tolerance: 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "TwoDConformance")]
    public void PathClippedSourceOverAppliesEdgeCoverageOnce(bool nested)
    {
        byte[] pixels = Render(encoder =>
        {
            encoder.PushClip(RectanglePath(16.25f, 16.25f, 31.5f, 31.5f));
            encoder.PushLayer();
            if (nested) { encoder.PushLayer(); }
            encoder.DrawPath(
                RectanglePath(0, 0, Width, Height),
                Matrix3x2.Identity,
                Brush.Solid(Color.White));
            if (nested) { encoder.PopLayer(); }
            encoder.PopLayer();
            encoder.PopClip();
        });

        AssertPixelNear(pixels, 16, 32, 191, 191, 191, 191, tolerance: 10);
        AssertPixelNear(pixels, 15, 32, 0, 0, 0, 0, tolerance: 3);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void PathClippedSourceOverLoadsANonSampledBackdrop()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using CommandEncoder backgroundEncoder = renderer.CreateCommandEncoder();
        backgroundEncoder.FillRectangle(
            new(0, 0, Width, Height),
            Brush.Solid(new(0, 0, 1)));
        using PreparedDisplayList background = renderer.Prepare(
            backgroundEncoder.Finish(),
            target.Description);
        using CommandEncoder foregroundEncoder = renderer.CreateCommandEncoder();
        foregroundEncoder.PushClip(RectanglePath(16.25f, 16.25f, 31.5f, 31.5f));
        foregroundEncoder.PushLayer();
        foregroundEncoder.DrawPath(
            RectanglePath(0, 0, Width, Height),
            Matrix3x2.Identity,
            Brush.Solid(new(1, 0, 0)));
        foregroundEncoder.PopLayer();
        foregroundEncoder.PopClip();
        using PreparedDisplayList foreground = renderer.Prepare(
            foregroundEncoder.Finish(),
            target.Description);
        var graph = new GpuRenderGraph();
        GpuRenderGraphTexture graphTarget = graph.ImportTexture(
            "non-sampled-target",
            target.Handle,
            target.Description);
        graph.AddTwoD(
            "background",
            renderer,
            background,
            graphTarget,
            new(GpuAttachmentLoadOperation.Clear, GpuAttachmentStoreOperation.Store),
            markOutput: false);
        graph.AddTwoD(
            "path-clipped-source-over",
            renderer,
            foreground,
            graphTarget,
            new(GpuAttachmentLoadOperation.Load, GpuAttachmentStoreOperation.Store));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        AssertPixelNear(pixels, 32, 32, 255, 0, 0, 255, tolerance: 3);
        AssertPixelNear(pixels, 16, 32, 191, 0, 64, 255, tolerance: 10);
        AssertPixelNear(pixels, 15, 32, 0, 0, 255, 255, tolerance: 3);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void PathClippedLayerShadowUsesSourceOverComposition()
    {
        byte[] pixels = Render(encoder =>
        {
            encoder.PushClip(RectanglePath(4, 4, 56, 56));
            encoder.PushLayer(new()
            {
                Shadow = new(new(24, 0), new(1, 0, 0)),
            });
            encoder.DrawPath(
                RectanglePath(8, 8, 16, 16),
                Matrix3x2.Identity,
                Brush.Solid(Color.White));
            encoder.PopLayer();
            encoder.PopClip();
        });

        AssertPixelNear(pixels, 16, 16, 255, 255, 255, 255, tolerance: 3);
        AssertPixelNear(pixels, 40, 16, 255, 0, 0, 255, tolerance: 4);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "TwoDConformance")]
    public void ClippedOutLayerStillHonorsTheRequestedRootClear(bool nested)
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        if (nested) { encoder.PushLayer(); }
        encoder.PushClip(RectanglePath(80, 80, 16, 16));
        encoder.PushLayer(new() { CompositeMode = CompositeMode.Clear });
        encoder.PopLayer();
        encoder.PopClip();
        if (nested) { encoder.PopLayer(); }
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "clipped-out-layer",
            renderer,
            prepared,
            new RenderTarget(
                target.Handle,
                target.Description,
                GpuAttachmentLoadOperation.Clear,
                ClearColor: new(0, 0, 1, 1)));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        AssertPixelNear(pixels, 32, 32, 0, 0, 255, 255);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void ExplicitZeroOpacityLayerRemainsTransparent()
    {
        byte[] pixels = Render(encoder =>
        {
            encoder.FillRectangle(new(0, 0, Width, Height), Brush.Solid(new(0, 0, 1)));
            LayerOptions transparent = default;
            encoder.PushLayer(transparent);
            encoder.FillRectangle(new(0, 0, Width, Height), Brush.Solid(new(1, 0, 0)));
            encoder.PopLayer();
        });

        AssertPixelNear(pixels, 32, 32, 0, 0, 255, 255);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void PlusCompositeSaturatesPremultipliedColorAndAlpha()
    {
        byte[] pixels = Render(encoder =>
        {
            encoder.FillRectangle(
                new(0, 0, Width, Height),
                Brush.Solid(new(0.9f, 0.8f, 0.1f, 0.8f)));
            encoder.PushLayer(new() { CompositeMode = CompositeMode.Plus });
            encoder.FillRectangle(
                new(0, 0, Width, Height),
                Brush.Solid(new(0.8f, 0.7f, 0.95f, 0.75f)));
            encoder.PopLayer();
        });

        AssertPixelNear(pixels, 32, 32, 255, 255, 202, 255, tolerance: 3);
    }

    [Theory]
    [InlineData(CompositeMode.ColorDodge)]
    [InlineData(CompositeMode.ColorBurn)]
    [Trait("Category", "TwoDConformance")]
    public void ColorBlendBoundaryPairsFollowW3cBranchOrder(CompositeMode mode)
    {
        Color backdrop = mode == CompositeMode.ColorDodge
            ? new(0, 0, 0)
            : new(1, 1, 1);
        Color source = mode == CompositeMode.ColorDodge
            ? new(1, 1, 1)
            : new(0, 0, 0);
        byte expected = mode == CompositeMode.ColorDodge ? (byte)0 : (byte)255;

        byte[] pixels = Render(encoder =>
        {
            encoder.FillRectangle(new(0, 0, Width, Height), Brush.Solid(backdrop));
            encoder.PushLayer(new() { CompositeMode = mode });
            encoder.FillRectangle(new(0, 0, Width, Height), Brush.Solid(source));
            encoder.PopLayer();
        });

        AssertPixelNear(pixels, 32, 32, expected, expected, expected, 255, tolerance: 2);
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
    public void RectangleUsesPremultipliedSourceOverBlending()
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
    public void DisposedDistanceFieldAtlasIsUnregisteredBeforePreparing()
    {
        using IGpuBackend backend = CreateBackend();
        using var atlas = new DistanceFieldAtlas(backend, 32, 32);
        using var rasterizer = new DistanceFieldRasterizer(backend, atlas);
        DistanceField field = rasterizer.Rasterize(RectanglePath(0, 0, 1, 1), 16, 16);
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.DrawDistanceField(field, new(0, 0, 16, 16), Brush.Solid(Color.White));
        DisplayList displayList = encoder.Finish();

        atlas.Dispose();
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
        {
            using PreparedDisplayList _ = renderer.Prepare(displayList, TargetDescription());
        });

        Assert.Equal("image", exception.ParamName);
        Assert.Contains("not registered", exception.Message, StringComparison.OrdinalIgnoreCase);
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
    public void VectorPathRendersRadialGradientThroughTiledCoverage()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.DrawPath(
            RectanglePath(4, 4, 56, 56),
            Matrix3x2.Identity,
            Brush.RadialGradient(new(32, 32), 28, new(1, 0, 0), new(0, 0, 1)));
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "radial-gradient-path",
            renderer,
            prepared,
            new RenderTarget(target.Handle, target.Description, GpuAttachmentLoadOperation.Clear));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        AssertPixelNear(pixels, 32, 32, 255, 0, 0, 255, tolerance: 7);
        AssertPixelNear(pixels, 53, 32, 64, 0, 191, 255, tolerance: 7);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void VectorPathRepeatsAnArbitraryColorLine()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.DrawPath(
            RectanglePath(4, 4, 56, 56),
            Matrix3x2.Identity,
            Brush.LinearGradient(
                new(8, 8),
                new(24, 8),
                new(8, 24),
                [
                    new(0, new(1, 0, 0)),
                    new(0.5f, new(0, 1, 0)),
                    new(1, new(0, 0, 1)),
                ],
                GradientExtendMode.Repeat));
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "repeated-color-line",
            renderer,
            prepared,
            new RenderTarget(target.Handle, target.Description, GpuAttachmentLoadOperation.Clear));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        AssertPixelNear(pixels, 12, 32, 112, 143, 0, 255, tolerance: 5);
        AssertPixelNear(pixels, 28, 32, 112, 143, 0, 255, tolerance: 5);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void VectorPathReflectsATwoCircleRadialGradient()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.DrawPath(
            RectanglePath(0, 0, 64, 64),
            Matrix3x2.Identity,
            Brush.RadialGradient(
                new(32, 32),
                8,
                new(32, 32),
                24,
                [
                    new(0, new(1, 0, 0)),
                    new(0.5f, new(0, 1, 0)),
                    new(1, new(0, 0, 1)),
                ],
                GradientExtendMode.Reflect));
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "reflected-radial-gradient",
            renderer,
            prepared,
            new RenderTarget(target.Handle, target.Description, GpuAttachmentLoadOperation.Clear));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        AssertPixelNear(pixels, 40, 32, 247, 8, 0, 255, tolerance: 9);
        AssertPixelNear(pixels, 60, 32, 0, 144, 111, 255, tolerance: 9);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void VectorPathRendersASweepGradient()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.DrawPath(
            RectanglePath(4, 4, 56, 56),
            Matrix3x2.Identity,
            Brush.SweepGradient(
                new(32.5f, 32.5f),
                0,
                -MathF.Tau,
                [
                    new(0, new(1, 0, 0)),
                    new(0.5f, new(0, 1, 0)),
                    new(1, new(0, 0, 1)),
                ]));
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "sweep-gradient",
            renderer,
            prepared,
            new RenderTarget(target.Handle, target.Description, GpuAttachmentLoadOperation.Clear));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        AssertPixelNear(pixels, 52, 32, 253, 2, 0, 255, tolerance: 8);
        AssertPixelNear(pixels, 12, 32, 2, 253, 0, 255, tolerance: 8);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void SweepGradientSamplesAColrPartialRangeAfterYDownConversion()
    {
        Brush brush = Brush.SweepGradient(
            new(32.5f, 32.5f),
            Degrees(-110),
            Degrees(-230),
            [new(0, new(1, 0, 0)), new(1, new(0, 0, 1))]);

        byte[] pixels = Render(encoder => encoder.DrawPath(
            RectanglePath(0, 0, 64, 64),
            Matrix3x2.Identity,
            brush));

        AssertPixelNear(pixels, 52, 32, 255, 0, 0, 255, tolerance: 3);
        AssertPixelNear(pixels, 12, 32, 106, 0, 149, 255, tolerance: 4);
        AssertPixelNear(pixels, 32, 52, 0, 0, 255, 255, tolerance: 3);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void SweepGradientDoesNotLiftTheZeroDegreeRayIntoTheNextRevolution()
    {
        Brush brush = Brush.SweepGradient(
            new(32.5f, 32.5f),
            Degrees(-330),
            Degrees(-400),
            [new(0, new(1, 0, 0)), new(1, new(0, 0, 1))]);

        byte[] pixels = Render(encoder => encoder.DrawPath(
            RectanglePath(0, 0, 64, 64),
            Matrix3x2.Identity,
            brush));

        AssertPixelNear(pixels, 52, 32, 255, 0, 0, 255, tolerance: 3);
        AssertPixelNear(pixels, 52, 36, 187, 0, 68, 255, tolerance: 6);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void SweepGradientSupportsDescendingColrAnglesAfterYDownConversion()
    {
        Brush brush = Brush.SweepGradient(
            new(32.5f, 32.5f),
            Degrees(-210),
            Degrees(-110),
            [new(0, new(1, 0, 0)), new(1, new(0, 0, 1))]);

        byte[] pixels = Render(encoder => encoder.DrawPath(
            RectanglePath(0, 0, 64, 64),
            Matrix3x2.Identity,
            brush));

        AssertPixelNear(pixels, 32, 52, 255, 0, 0, 255, tolerance: 3);
        AssertPixelNear(pixels, 12, 32, 179, 0, 77, 255, tolerance: 4);
        AssertPixelNear(pixels, 32, 12, 0, 0, 255, 255, tolerance: 3);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void EqualAnglePadSweepKeepsItsSharpColorTransition()
    {
        Brush brush = Brush.SweepGradient(
            new(32.5f, 32.5f),
            -MathF.PI,
            -MathF.PI,
            [new(0, new(1, 0, 0)), new(1, new(0, 0, 1))]);

        byte[] pixels = Render(encoder => encoder.DrawPath(
            RectanglePath(0, 0, 64, 64),
            Matrix3x2.Identity,
            brush));

        AssertPixelNear(pixels, 52, 32, 255, 0, 0, 255, tolerance: 3);
        AssertPixelNear(pixels, 12, 32, 0, 0, 255, 255, tolerance: 3);
    }

    [Theory]
    [InlineData(GradientExtendMode.Repeat)]
    [InlineData(GradientExtendMode.Reflect)]
    [Trait("Category", "TwoDConformance")]
    public void EqualAnglePeriodicSweepDrawsNothing(GradientExtendMode extendMode)
    {
        Brush brush = Brush.SweepGradient(
            new(32.5f, 32.5f),
            -MathF.PI,
            -MathF.PI,
            [new(0, new(1, 0, 0)), new(1, new(0, 0, 1))],
            extendMode);

        byte[] pixels = Render(encoder => encoder.DrawPath(
            RectanglePath(0, 0, 64, 64),
            Matrix3x2.Identity,
            brush));

        AssertPixelNear(pixels, 52, 32, 0, 0, 0, 0);
        AssertPixelNear(pixels, 12, 32, 0, 0, 0, 0);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void NestedPathClipsIntersectOnTheGpu()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.PushClip(new Rect(8, 8, 48, 48));
        encoder.PushClip(
            RectanglePath(0, 0, 24, 24),
            Matrix3x2.CreateTranslation(24, 16));
        encoder.DrawPath(
            RectanglePath(0, 0, 64, 64),
            Matrix3x2.Identity,
            Brush.Solid(new(0, 1, 0)));
        encoder.PopClip();
        encoder.PopClip();
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "nested-clips",
            renderer,
            prepared,
            new RenderTarget(target.Handle, target.Description, GpuAttachmentLoadOperation.Clear));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        AssertPixelNear(pixels, 32, 32, 0, 255, 0, 255);
        AssertPixelNear(pixels, 16, 32, 0, 0, 0, 0);
        AssertPixelNear(pixels, 32, 8, 0, 0, 0, 0);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void TargetSpaceClipCurvesKeepSubpixelSubdivisionUnderASmallPathTransform()
    {
        PathGeometry curvedClip = new PathBuilder()
            .MoveTo(new(8, 56))
            .CubicTo(new(8, 8), new(56, 8), new(56, 56))
            .LineTo(new(8, 56))
            .Close()
            .Build();

        byte[] pixels = Render(encoder =>
        {
            encoder.PushClip(curvedClip, Matrix3x2.Identity);
            encoder.DrawPath(
                RectanglePath(0, 0, 64_000, 64_000),
                Matrix3x2.CreateScale(0.001f),
                Brush.Solid(new(0, 1, 0)));
            encoder.PopClip();
        });

        AssertPixelNear(pixels, 32, 40, 0, 255, 0, 255, tolerance: 4);
        AssertPixelNear(pixels, 32, 12, 0, 0, 0, 0);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void SourceInKeepsOnlyTheOverlapAcrossTheBackend()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.FillRectangle(new(8, 8, 32, 32), Brush.Solid(new(0, 1, 0)));
        encoder.PushLayer(new() { CompositeMode = CompositeMode.SourceIn });
        encoder.FillRectangle(new(24, 24, 32, 32), Brush.Solid(new(1, 0, 0)));
        encoder.PopLayer();
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "source-in",
            renderer,
            prepared,
            new RenderTarget(target.Handle, target.Description, GpuAttachmentLoadOperation.Clear));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        AssertPixelNear(pixels, 32, 32, 255, 0, 0, 255);
        AssertPixelNear(pixels, 16, 16, 0, 0, 0, 0);
        AssertPixelNear(pixels, 48, 48, 0, 0, 0, 0);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void CubicPathIsExpandedByComputeBeforeRasterization()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        PathGeometry path = new PathBuilder()
            .MoveTo(new(8, 32))
            .CubicTo(new(8, 8), new(56, 8), new(56, 32))
            .LineTo(new(56, 56))
            .LineTo(new(8, 56))
            .Close()
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

        AssertPixelNear(pixels, 32, 40, 0, 255, 0, 255, tolerance: 4);
        AssertPixelNear(pixels, 4, 4, 0, 0, 0, 0);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void QuadraticPathIsExpandedByComputeBeforeRasterization()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        PathGeometry path = new PathBuilder()
            .MoveTo(new(8, 32))
            .QuadraticTo(new(32, 8), new(56, 32))
            .LineTo(new(56, 56))
            .LineTo(new(8, 56))
            .Close()
            .Build();
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.DrawPath(path, Matrix3x2.Identity, Brush.Solid(new(0, 1, 0)));
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "quadratic-path",
            renderer,
            prepared,
            new RenderTarget(target.Handle, target.Description, GpuAttachmentLoadOperation.Clear));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        AssertPixelNear(pixels, 32, 40, 0, 255, 0, 255, tolerance: 4);
        AssertPixelNear(pixels, 4, 4, 0, 0, 0, 0);
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void PathClipRestrictsFillCoverage()
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
    }

    [Fact]
    [Trait("Category", "TwoDConformance")]
    public void StrokePathRendersAcrossTheBackend()
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        PathGeometry line = new PathBuilder()
            .MoveTo(new(8, 32))
            .LineTo(new(56, 32))
            .Build();
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.StrokePath(
            line,
            Matrix3x2.Identity,
            new StrokeStyle(4),
            Brush.Solid(Color.White));
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "stroked-path",
            renderer,
            prepared,
            new RenderTarget(target.Handle, target.Description, GpuAttachmentLoadOperation.Clear));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        byte[] pixels = ReadPixels(backend, target.Handle);

        AssertPixelNear(pixels, 32, 32, 255, 255, 255, 255);
        AssertPixelNear(pixels, 32, 26, 0, 0, 0, 0);
    }

    protected abstract IGpuBackend CreateBackend();

    private byte[] Render(Action<CommandEncoder> record)
    {
        using IGpuBackend backend = CreateBackend();
        using var target = BackendTexture.Create(backend, TargetDescription());
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        record(encoder);
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), target.Description);
        var graph = new GpuRenderGraph();
        graph.AddTwoD(
            "two-d-route",
            renderer,
            prepared,
            new RenderTarget(
                target.Handle,
                target.Description,
                GpuAttachmentLoadOperation.Clear,
                ClearColor: new(0, 0, 0, 0)));

        using GpuRenderGraphExecution execution = graph.Compile().Execute(backend);
        return ReadPixels(backend, target.Handle);
    }

    private static PathGeometry RectanglePath(float x, float y, float width, float height)
        => new PathBuilder()
            .MoveTo(new(x, y))
            .LineTo(new(x + width, y))
            .LineTo(new(x + width, y + height))
            .LineTo(new(x, y + height))
            .Close()
            .Build();

    private static float Degrees(float value) => value * MathF.PI / 180;

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

    private static Vector4 Premultiply(Color color)
        => new(
            color.Red * color.Alpha,
            color.Green * color.Alpha,
            color.Blue * color.Alpha,
            color.Alpha);

    private static Vector4 CompositeReference(
        CompositeMode mode,
        Vector4 source,
        Vector4 backdrop)
    {
        float sourceAlpha = source.W;
        float backdropAlpha = backdrop.W;
        return mode switch
        {
            CompositeMode.Clear => Vector4.Zero,
            CompositeMode.Source => source,
            CompositeMode.Destination => backdrop,
            CompositeMode.SourceOver => source + backdrop * (1 - sourceAlpha),
            CompositeMode.DestinationOver => source * (1 - backdropAlpha) + backdrop,
            CompositeMode.SourceIn => source * backdropAlpha,
            CompositeMode.DestinationIn => backdrop * sourceAlpha,
            CompositeMode.SourceOut => source * (1 - backdropAlpha),
            CompositeMode.DestinationOut => backdrop * (1 - sourceAlpha),
            CompositeMode.SourceAtop => source * backdropAlpha + backdrop * (1 - sourceAlpha),
            CompositeMode.DestinationAtop => source * (1 - backdropAlpha) + backdrop * sourceAlpha,
            CompositeMode.Xor => source * (1 - backdropAlpha) + backdrop * (1 - sourceAlpha),
            CompositeMode.Plus => Vector4.Min(Vector4.One, source + backdrop),
            _ => BlendReference(mode, source, backdrop),
        };
    }

    private static Vector4 BlendReference(
        CompositeMode mode,
        Vector4 source,
        Vector4 backdrop)
    {
        float sourceAlpha = source.W;
        float backdropAlpha = backdrop.W;
        Vector3 sourceColor = sourceAlpha > 0
            ? new Vector3(source.X, source.Y, source.Z) / sourceAlpha
            : default;
        Vector3 backdropColor = backdropAlpha > 0
            ? new Vector3(backdrop.X, backdrop.Y, backdrop.Z) / backdropAlpha
            : default;
        Vector3 blended = mode switch
        {
            CompositeMode.Screen => Vector3.One - (Vector3.One - backdropColor) * (Vector3.One - sourceColor),
            CompositeMode.Overlay => PerComponent(
                backdropColor,
                sourceColor,
                static (backdrop, source) => backdrop <= 0.5f
                    ? 2 * backdrop * source
                    : 1 - 2 * (1 - backdrop) * (1 - source)),
            CompositeMode.Darken => Vector3.Min(backdropColor, sourceColor),
            CompositeMode.Lighten => Vector3.Max(backdropColor, sourceColor),
            CompositeMode.ColorDodge => PerComponent(
                backdropColor,
                sourceColor,
                static (backdrop, source) => backdrop <= 0
                    ? 0
                    : source >= 1
                    ? 1
                    : MathF.Min(1, backdrop / (1 - source))),
            CompositeMode.ColorBurn => PerComponent(
                backdropColor,
                sourceColor,
                static (backdrop, source) => backdrop >= 1
                    ? 1
                    : source <= 0
                    ? 0
                    : 1 - MathF.Min(1, (1 - backdrop) / source)),
            CompositeMode.HardLight => PerComponent(
                backdropColor,
                sourceColor,
                static (backdrop, source) => source <= 0.5f
                    ? 2 * backdrop * source
                    : 1 - 2 * (1 - backdrop) * (1 - source)),
            CompositeMode.SoftLight => PerComponent(
                backdropColor,
                sourceColor,
                static (backdrop, source) => source <= 0.5f
                    ? backdrop - (1 - 2 * source) * backdrop * (1 - backdrop)
                    : backdrop + (2 * source - 1) * (SoftLightReference(backdrop) - backdrop)),
            CompositeMode.Difference => Vector3.Abs(backdropColor - sourceColor),
            CompositeMode.Exclusion => backdropColor + sourceColor - 2 * backdropColor * sourceColor,
            CompositeMode.Multiply => backdropColor * sourceColor,
            CompositeMode.HslHue => SetLuminosityReference(
                SetSaturationReference(sourceColor, SaturationReference(backdropColor)),
                LuminosityReference(backdropColor)),
            CompositeMode.HslSaturation => SetLuminosityReference(
                SetSaturationReference(backdropColor, SaturationReference(sourceColor)),
                LuminosityReference(backdropColor)),
            CompositeMode.HslColor => SetLuminosityReference(
                sourceColor,
                LuminosityReference(backdropColor)),
            CompositeMode.HslLuminosity => SetLuminosityReference(
                backdropColor,
                LuminosityReference(sourceColor)),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        Vector3 result = new Vector3(source.X, source.Y, source.Z) * (1 - backdropAlpha)
            + new Vector3(backdrop.X, backdrop.Y, backdrop.Z) * (1 - sourceAlpha)
            + blended * (sourceAlpha * backdropAlpha);
        float alpha = sourceAlpha + backdropAlpha - sourceAlpha * backdropAlpha;
        return new(result, alpha);
    }

    private static Vector3 PerComponent(
        Vector3 backdrop,
        Vector3 source,
        Func<float, float, float> blend)
        => new(
            blend(backdrop.X, source.X),
            blend(backdrop.Y, source.Y),
            blend(backdrop.Z, source.Z));

    private static float SoftLightReference(float backdrop)
        => backdrop <= 0.25f
            ? ((16 * backdrop - 12) * backdrop + 4) * backdrop
            : MathF.Sqrt(backdrop);

    private static float LuminosityReference(Vector3 color)
        => 0.3f * color.X + 0.59f * color.Y + 0.11f * color.Z;

    private static float SaturationReference(Vector3 color)
        => MathF.Max(color.X, MathF.Max(color.Y, color.Z))
            - MathF.Min(color.X, MathF.Min(color.Y, color.Z));

    private static Vector3 SetSaturationReference(Vector3 color, float saturation)
    {
        float minimum = MathF.Min(color.X, MathF.Min(color.Y, color.Z));
        float maximum = MathF.Max(color.X, MathF.Max(color.Y, color.Z));
        float range = maximum - minimum;
        return range > 0 ? (color - new Vector3(minimum)) * saturation / range : default;
    }

    private static Vector3 SetLuminosityReference(Vector3 color, float luminosity)
        => ClipColorReference(color + new Vector3(luminosity - LuminosityReference(color)));

    private static Vector3 ClipColorReference(Vector3 color)
    {
        float luminosity = LuminosityReference(color);
        float minimum = MathF.Min(color.X, MathF.Min(color.Y, color.Z));
        float maximum = MathF.Max(color.X, MathF.Max(color.Y, color.Z));
        if (minimum < 0)
        {
            color = new Vector3(luminosity)
                + (color - new Vector3(luminosity)) * luminosity / (luminosity - minimum);
        }
        if (maximum > 1)
        {
            color = new Vector3(luminosity)
                + (color - new Vector3(luminosity)) * (1 - luminosity) / (maximum - luminosity);
        }
        return color;
    }

    private static byte ToByte(float value)
        => checked((byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255));

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

    private static void AssertRedPixelInRange(
        ReadOnlySpan<byte> pixels,
        int x,
        int y,
        byte minimum,
        byte maximum)
    {
        int offset = checked((y * (int)Width + x) * 4);
        byte[] actual = pixels.Slice(offset, 4).ToArray();
        bool matches = actual[0] >= minimum
            && actual[0] <= maximum
            && actual[1] == 0
            && actual[2] == 0
            && Math.Abs(actual[0] - actual[3]) <= 2;
        Assert.True(
            matches,
            $"Pixel ({x}, {y}) expected premultiplied red between {minimum} and {maximum}, "
                + $"but was [{string.Join(", ", actual)}].");
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
