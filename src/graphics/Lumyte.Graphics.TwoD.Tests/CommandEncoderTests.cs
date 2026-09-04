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
    public void EncoderRecordsEveryPhaseOneRoute()
    {
        using var backend = new BufferBackend();
        using var renderer = new Renderer(backend);
        ImageId image = renderer.RegisterImage(
            new(71),
            new(2, 2, GpuFormat.Rgba8Unorm, GpuTextureUsage.Sampled),
            new(72));
        using CommandEncoder encoder = renderer.CreateCommandEncoder();

        encoder.FillRectangle(new(0, 0, 16, 16), Brush.Solid(new(1, 0, 0)));
        encoder.FillRoundedRectangle(new(16, 0, 16, 16), new(4), Brush.Solid(new(0, 1, 0)));
        encoder.FillEllipse(new(32, 0, 16, 16), Brush.Solid(new(0, 0, 1)));
        encoder.DrawLine(new(0, 24), new(32, 24), 2, Brush.Solid(Color.White));
        encoder.DrawImage(image, new(48, 0, 16, 16));
        encoder.DrawGeometry(
            PolygonGeometry.FromConvexPolygon([
                new(0, 32),
                new(16, 32),
                new(8, 48),
            ]),
            Matrix3x2.Identity,
            Brush.Solid(Color.White));
        DisplayList displayList = encoder.Finish();

        using PreparedDisplayList prepared = renderer.Prepare(displayList, TargetDescription);

        Assert.Equal(6, displayList.Count);
        Assert.Equal(6, prepared.CommandCount);
        Assert.False(prepared.IsEmpty);
    }

    [Fact]
    public void ClipExcludesInvisibleCommandsDuringPreparation()
    {
        using var backend = new BufferBackend();
        using var renderer = new Renderer(backend);
        using CommandEncoder encoder = renderer.CreateCommandEncoder();
        encoder.Save();
        encoder.Clip(new(0, 0, 8, 8));
        encoder.FillRectangle(new(32, 32, 8, 8), Brush.Solid(Color.White));
        encoder.Restore();
        encoder.FillRectangle(new(4, 4, 8, 8), Brush.Solid(Color.White));
        DisplayList displayList = encoder.Finish();

        using PreparedDisplayList prepared = renderer.Prepare(displayList, TargetDescription);

        Assert.Equal(1, prepared.CommandCount);
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

    private sealed class BufferBackend : IGpuBackend
    {
        private readonly Dictionary<ulong, byte[]> buffers = [];
        private ulong nextBuffer = 1;

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
