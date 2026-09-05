using System.Numerics;

using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.TwoD.Tests;

public sealed class CommandEncoderTests
{
    private static readonly GpuTextureDescription TargetDescription = new(
        128,
        96,
        GpuFormat.Rgba8Unorm,
        GpuTextureUsage.ColorAttachment | GpuTextureUsage.CopySource);

    [Fact]
    public void ClipExcludesInvisibleCommandsDuringPreparation()
    {
        using var backend = new BufferBackend();
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        using (encoder.BeginClip(new Rect(0, 0, 8, 8)))
        {
            encoder.FillRectangle(new(32, 32, 8, 8), Brush.Solid(Color.White));
        }
        encoder.FillRectangle(new(4, 4, 8, 8), Brush.Solid(Color.White));
        DisplayList displayList = encoder.Finish();

        using PreparedDisplayList prepared = renderer.Prepare(displayList, TargetDescription);

        Assert.Equal(1, prepared.CommandCount);
    }

    [Fact]
    public void ScopeRestoresDrawingStateAfterException()
    {
        using var backend = new BufferBackend();
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();

        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using CommandEncoderScope scope = encoder.BeginState();
            encoder.Transform(Matrix3x2.CreateTranslation(64, 0));
            using CommandEncoderScope clip = encoder.BeginClip(new Rect(0, 0, 4, 4));
            throw new InvalidOperationException("drawing failed");
        }));
        encoder.FillRectangle(new(0, 0, 8, 8), Brush.Solid(Color.White));

        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), TargetDescription);

        Assert.Equal(1, prepared.CommandCount);
        Assert.Equal(0, encoder.ClipDepth);
    }

    [Fact]
    public void OutOfOrderScopeDisposalPreservesState()
    {
        using var backend = new BufferBackend();
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        CommandEncoderScope outer = encoder.BeginState();
        CommandEncoderScope inner = encoder.BeginClip(new Rect(0, 0, 8, 8));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(outer.Dispose);

        Assert.Contains("reverse order", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, encoder.ClipDepth);
        inner.Dispose();
        outer.Dispose();
        _ = encoder.Finish();
    }

    [Fact]
    public void DuplicateScopeDisposalCannotCloseParentScope()
    {
        using var backend = new BufferBackend();
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        CommandEncoderScope outer = encoder.BeginState();
        CommandEncoderScope inner = encoder.BeginClip(new Rect(0, 0, 8, 8));

        inner.Dispose();
        inner.Dispose();

        Assert.Throws<InvalidOperationException>(encoder.Finish);
        outer.Dispose();
        _ = encoder.Finish();
    }

    [Fact]
    public void FailedFinishLeavesEncoderRecording()
    {
        using var backend = new BufferBackend();
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        CommandEncoderScope scope = encoder.BeginLayer(new() { Opacity = 0.5f });

        _ = Assert.Throws<InvalidOperationException>(encoder.Finish);
        scope.Dispose();
        encoder.FillRectangle(new(0, 0, 8, 8), Brush.Solid(Color.White));

        Assert.Equal(1, encoder.Finish().Count);
        Assert.Throws<InvalidOperationException>(encoder.Finish);
    }

    [Fact]
    public void FinishRequiresBalancedSavedState()
    {
        using var backend = new BufferBackend();
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.Save();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(encoder.Finish);

        Assert.Contains("restored", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FinishRequiresBalancedLayers()
    {
        using var backend = new BufferBackend();
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.PushLayer(new() { Opacity = 0.5f });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(encoder.Finish);

        Assert.Contains("popped", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedRectangleAndPathClipsAreScopedIndependently()
    {
        using var backend = new BufferBackend();
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        PathGeometry path = new PathBuilder()
            .MoveTo(new(0, 0))
            .LineTo(new(16, 0))
            .LineTo(new(8, 16))
            .Close()
            .Build();

        encoder.PushClip(new Rect(0, 0, 32, 32));
        encoder.PushClip(path, Matrix3x2.CreateTranslation(4, 4), FillRule.EvenOdd);
        encoder.DrawPath(path, Matrix3x2.Identity, Brush.Solid(Color.White));
        Assert.Equal(2, encoder.ClipDepth);
        encoder.PopClip();
        encoder.PopClip();
        DisplayList displayList = encoder.Finish();

        Assert.Equal(1, displayList.Count);
        Assert.Equal(0, encoder.ClipDepth);
    }

    [Fact]
    public void FinishRequiresBalancedScopedClips()
    {
        using var backend = new BufferBackend();
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.PushClip(new Rect(0, 0, 8, 8));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(encoder.Finish);

        Assert.Contains("clip", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DisjointNestedClipBoundsDiscardCommands()
    {
        using var backend = new BufferBackend();
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.PushClip(new Rect(0, 0, 8, 8));
        encoder.PushClip(new Rect(16, 16, 8, 8));
        encoder.FillRectangle(new(0, 0, 32, 32), Brush.Solid(Color.White));
        encoder.PopClip();
        encoder.PopClip();

        DisplayList displayList = encoder.Finish();

        Assert.Equal(0, displayList.Count);
    }

    [Fact]
    public void NestedAxisAlignedRectangleClipsUseTheirIntersectionDuringPreparation()
    {
        using var backend = new BufferBackend();
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.PushClip(new Rect(0, 0, 16, 16));
        encoder.PushClip(new Rect(8, 8, 16, 16));
        encoder.FillRectangle(new(0, 0, 32, 32), Brush.Solid(Color.White));
        encoder.PopClip();
        encoder.PopClip();
        DisplayList displayList = encoder.Finish();

        using PreparedDisplayList prepared = renderer.Prepare(displayList, TargetDescription);

        Assert.Equal(1, prepared.CommandCount);
    }

    [Fact]
    public void PathClipIsRetainedForGpuPreparation()
    {
        using var backend = new BufferBackend();
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        PathGeometry path = new PathBuilder()
            .MoveTo(new(0, 0))
            .LineTo(new(16, 0))
            .LineTo(new(8, 16))
            .Close()
            .Build();
        encoder.PushClip(path);
        encoder.DrawPath(path, Matrix3x2.Identity, Brush.Solid(Color.White));
        encoder.PopClip();
        DisplayList displayList = encoder.Finish();

        Assert.Equal(1, displayList.Count);
    }

    [Fact]
    public void ExtendedGradientIsRetainedForGpuPreparation()
    {
        using var backend = new BufferBackend();
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        PathGeometry path = new PathBuilder()
            .MoveTo(new(0, 0))
            .LineTo(new(16, 0))
            .LineTo(new(8, 16))
            .Close()
            .Build();
        Brush brush = Brush.SweepGradient(
            new(8, 8),
            0,
            MathF.PI * 2,
            [new(0, Color.White), new(1, Color.Transparent)]);
        encoder.DrawPath(path, Matrix3x2.Identity, brush);
        DisplayList displayList = encoder.Finish();

        Assert.Equal(1, displayList.Count);
    }

    [Fact]
    public void PorterDuffCompositeCanBePrepared()
    {
        using var backend = new BufferBackend();
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.PushLayer(new() { CompositeMode = CompositeMode.SourceIn });
        encoder.FillRectangle(new(0, 0, 16, 16), Brush.Solid(Color.White));
        encoder.PopLayer();
        DisplayList displayList = encoder.Finish();

        using PreparedDisplayList prepared = renderer.Prepare(displayList, TargetDescription);

        Assert.Equal(1, prepared.CommandCount);
    }

    [Fact]
    public void LayerCreatesTransientRenderAndCompositePasses()
    {
        using var backend = new BufferBackend();
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.PushLayer(new() { Opacity = 0.5f });
        encoder.FillRectangle(new(0, 0, 16, 16), Brush.Solid(Color.White));
        encoder.PopLayer();
        using PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), TargetDescription);
        var graph = new GpuRenderGraph();

        graph.AddTwoD(
            "ui",
            renderer,
            prepared,
            new RenderTarget(new(91), TargetDescription, GpuAttachmentLoadOperation.Clear));
        GpuRenderGraphPlan plan = graph.Compile();

        Assert.Equal(2, plan.Passes.Count);
        Assert.Equal(2, plan.TextureCount);
        Assert.Contains(plan.Passes, pass => pass.Name.Contains("composite", StringComparison.Ordinal));
    }

    [Fact]
    public void ConvexPolygonExpandsToTriangleList()
    {
        PolygonGeometry geometry = PolygonGeometry.FromConvexPolygon([
            new(0, 0),
            new(1, 0),
            new(1, 1),
            new(0, 1),
        ]);

        Assert.Equal(2, geometry.TriangleCount);
        Assert.Equal(6, geometry.Vertices.Length);
    }

    [Fact]
    public void AddTwoDCreatesOneOrderedRasterPass()
    {
        using var backend = new BufferBackend();
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.FillRectangle(new(0, 0, 16, 16), Brush.Solid(Color.White));
        DisplayList displayList = encoder.Finish();
        using PreparedDisplayList prepared = renderer.Prepare(displayList, TargetDescription);
        var graph = new GpuRenderGraph();

        RenderPassResources resources = graph.AddTwoD(
            "ui",
            renderer,
            prepared,
            new RenderTarget(new(91), TargetDescription, GpuAttachmentLoadOperation.Clear));
        GpuRenderGraphPlan plan = graph.Compile();

        Assert.False(resources.Target.IsNull);
        Assert.Equal("ui", Assert.Single(plan.Passes).Name);
        Assert.Single(resources.Buffers);
        Assert.Equal(1, plan.TextureCount);
    }

    [Fact]
    public void PreparedDrawingLeaseDefersBufferDestruction()
    {
        using var backend = new BufferBackend();
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.FillRectangle(new(0, 0, 16, 16), Brush.Solid(Color.White));
        PreparedDisplayList prepared = renderer.Prepare(encoder.Finish(), TargetDescription);
        IDisposable lease = prepared.AcquireLease();
        int allocatedBufferCount = backend.BufferCount;

        prepared.Dispose();

        Assert.True(allocatedBufferCount > 0);
        Assert.Equal(allocatedBufferCount, backend.BufferCount);

        lease.Dispose();
        Assert.Equal(0, backend.BufferCount);
    }

    private sealed class BufferBackend : IGpuBackend
    {
        private readonly Dictionary<ulong, byte[]> buffers = [];
        private ulong nextBuffer = 1;

        public int BufferCount => buffers.Count;

        public GpuBackendCapabilities Capabilities =>
            GpuBackendCapabilities.DeviceOwnedResources | GpuBackendCapabilities.RasterPipeline;

        public GpuBufferHandle CreateBuffer(GpuBufferDescription description)
        {
            description.Validate();
            var handle = new GpuBufferHandle(nextBuffer++, description.Size);
            buffers.Add(handle.Value, new byte[checked((int)description.Size)]);
            return handle;
        }

        public void WriteBuffer(GpuBufferHandle buffer, ReadOnlySpan<byte> source)
            => WriteBuffer(buffer, 0, source);

        public void WriteBuffer(
            GpuBufferHandle buffer,
            ulong destinationOffset,
            ReadOnlySpan<byte> source)
        {
            byte[] destination = buffers[buffer.Value];
            source.CopyTo(destination.AsSpan(checked((int)destinationOffset)));
        }

        public void DestroyBuffer(GpuBufferHandle buffer)
        {
            if (!buffers.Remove(buffer.Value))
            {
                throw new ArgumentException("Unknown buffer.", nameof(buffer));
            }
        }
    }
}
