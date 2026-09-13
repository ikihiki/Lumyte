using System.Buffers.Binary;

namespace Lumyte.Graphics.Portable.Tests.Device;

public sealed partial class ExternalPortableGpuBackendTests
{
    [Fact]
    public void ConsumerCreatesAndDestroysALogicalRasterPipeline()
    {
        List<object> observed = [];
        using IPortableGpuBackend backend = new ExternalBackend(observed.Add);
        var module = backend.CreateShaderModule("raster source owned by the caller");
        var description = new GpuRasterPipelineDescription([new(GpuFormat.Rgba8Unorm)]);
        var shaders = new GpuShaderProgramDescription([
            new(module, GpuShaderStage.Vertex, "vertex"), new(module, GpuShaderStage.Pixel, "fragment")], [], 8);
        observed.Clear();

        var pipeline = backend.CreateRasterPipeline(description, shaders);
        backend.DestroyRasterPipeline(pipeline);

        Assert.Collection(observed,
            value => Assert.Equal(new RasterPipelineCreation(description, shaders), value),
            value => Assert.Same(pipeline, value));
        backend.DestroyShaderModule(module);
    }

    [Fact]
    public void TypedRasterRootForwardsItsExactBytesToAnExternalRecording()
    {
        List<object> observed = [];
        using GpuCommandBuffer commands = new ExternalCommands(observed.Add);
        var root = new ComputeRoot(17, 2.5f);

        commands.SetRootData(in root);
        root = default;

        byte[] bytes = Assert.IsType<RasterRootCall>(Assert.Single(observed)).Bytes;
        Assert.Equal(8, bytes.Length);
        Assert.Equal((17u, 2.5f), (BinaryPrimitives.ReadUInt32LittleEndian(bytes), BitConverter.ToSingle(bytes, 4)));
    }

    [Fact]
    public void ExternalRecordingConsumesColorAttachmentsBeforeSourceReuse()
    {
        List<object> observed = [];
        using IPortableGpuBackend backend = new ExternalBackend(observed.Add);
        var texture = backend.CreateTexture(new(GpuTextureDimension.Texture2D, 4, 4, 1, 1, 1, 4,
            GpuFormat.Rgba8Unorm, GpuTextureUsage.ColorAttachment));
        var expected = new GpuColorAttachment(new(texture), GpuAttachmentLoadOperation.Clear,
            ClearColor: new(0.25, 0.5, 0.75, 1), ResolveTarget: new(texture), DepthSlice: 2);
        GpuColorAttachment[] colors = [expected];
        using GpuCommandBuffer commands = new ExternalCommands(observed.Add);
        observed.Clear();

        commands.BeginRendering(colors);
        colors[0] = default;

        var call = Assert.IsType<BeginRenderingCall>(Assert.Single(observed));
        Assert.Equal(expected, Assert.Single(call.Colors));
        Assert.Null(call.DepthStencil);
        backend.DestroyTexture(texture);
    }

    [Fact]
    public void ExternalRecordingConsumesRasterDynamicOffsetsBeforeSourceReuse()
    {
        List<object> observed = [];
        using IPortableGpuBackend backend = new ExternalBackend(observed.Add);
        var layout = backend.CreateBindingLayout([]);
        var bindings = backend.CreateBindings(layout, []);
        using GpuCommandBuffer commands = new ExternalCommands(observed.Add);
        uint[] offsets = [256, 512];
        observed.Clear();

        commands.SetBindings(2, bindings, offsets);
        Array.Fill(offsets, 0u);

        var call = Assert.IsType<RasterBindingsCall>(Assert.Single(observed));
        Assert.Equal((2u, bindings), (call.Group, call.Bindings));
        Assert.Equal(new uint[] { 256, 512 }, call.Offsets);
        backend.DestroyBindings(bindings);
        backend.DestroyBindingLayout(layout);
    }

    [Theory]
    [InlineData(GpuIndexFormat.Uint16)]
    [InlineData(GpuIndexFormat.Uint32)]
    public void ExternalIndexedDrawPreservesRangeAndSignedBaseVertex(GpuIndexFormat format)
    {
        List<object> observed = [];
        using IPortableGpuBackend backend = new ExternalBackend(observed.Add);
        var buffer = backend.CreateBuffer(new(1ul << 42, GpuBufferUsage.Index));
        var range = new GpuBufferRange(buffer, (1ul << 40) + 4, 60);
        using GpuCommandBuffer commands = new ExternalCommands(observed.Add);
        observed.Clear();

        commands.DrawIndexed(range, format, 6, 2, 3, -4, 7);

        Assert.Equal(new DrawIndexedCall(range, format, 6, 2, 3, -4, 7), Assert.Single(observed));
        backend.DestroyBuffer(buffer);
    }

    [Fact]
    public void ExternalRecordingPreservesRenderStateAndWorkOrder()
    {
        List<object> observed = [];
        using GpuCommandBuffer commands = new ExternalCommands(observed.Add);
        var viewport = new GpuViewport(1, 2, 16, 8, 0.2f, 0.8f);
        var scissor = new GpuScissorRect(3, 4, 5, 6);
        var color = new GpuClearColor(0.1, 0.2, 0.3, 0.4);

        commands.BeginRendering([]);
        commands.SetViewportAndScissor(viewport, scissor);
        commands.SetStencilReference(42);
        commands.SetBlendConstant(color);
        commands.Draw(3, 2, 6, 4);
        commands.EndRendering();

        Assert.Collection(observed,
            value => Assert.IsType<BeginRenderingCall>(value),
            value => Assert.Equal(new ViewportCall(viewport, scissor), value),
            value => Assert.Equal(new StencilCall(42), value),
            value => Assert.Equal(new BlendConstantCall(color), value),
            value => Assert.Equal(new DrawCall(3, 2, 6, 4), value),
            value => Assert.IsType<EndRenderingCall>(value));
    }

    [Fact]
    public void ExternalIndirectDrawPreservesBothBufferRanges()
    {
        List<object> observed = [];
        using IPortableGpuBackend backend = new ExternalBackend(observed.Add);
        var buffer = backend.CreateBuffer(new(1ul << 42, GpuBufferUsage.Index | GpuBufferUsage.IndirectArguments));
        var indices = new GpuBufferRange(buffer, 1ul << 40, 64);
        var arguments = new GpuBufferRange(buffer, (1ul << 41) + 16, 20);
        using GpuCommandBuffer commands = new ExternalCommands(observed.Add);
        observed.Clear();

        commands.DrawIndirect(arguments);
        commands.DrawIndexedIndirect(indices, GpuIndexFormat.Uint32, arguments);

        Assert.Collection(observed,
            value => Assert.Equal(new DrawIndirectCall(arguments), value),
            value => Assert.Equal(new DrawIndexedIndirectCall(indices, GpuIndexFormat.Uint32, arguments), value));
        backend.DestroyBuffer(buffer);
    }

    private sealed record RasterPipelineCreation(GpuRasterPipelineDescription Description, GpuShaderProgramDescription Shaders);
    private sealed record BeginRenderingCall(GpuColorAttachment[] Colors, GpuDepthStencilAttachment? DepthStencil);
    private sealed record EndRenderingCall;
    private sealed record RasterPipelineCall(GpuRasterPipelineHandle Pipeline);
    private sealed record ViewportCall(GpuViewport Viewport, GpuScissorRect Scissor);
    private sealed record StencilCall(uint Reference);
    private sealed record BlendConstantCall(GpuClearColor Color);
    private sealed record RasterBindingsCall(uint Group, GpuBindingsHandle Bindings, uint[] Offsets);
    private sealed record RasterRootCall(byte[] Bytes);
    private sealed record DrawCall(uint VertexCount, uint InstanceCount, uint FirstVertex, uint FirstInstance);
    private sealed record DrawIndexedCall(GpuBufferRange Indices, GpuIndexFormat Format, uint IndexCount,
        uint InstanceCount, uint FirstIndex, int BaseVertex, uint FirstInstance);
    private sealed record DrawIndirectCall(GpuBufferRange Arguments);
    private sealed record DrawIndexedIndirectCall(GpuBufferRange Indices, GpuIndexFormat Format, GpuBufferRange Arguments);
    private sealed record BufferToTextureCall(GpuBufferRange Source, GpuTextureHandle Texture, GpuTextureCopyFootprint Footprint);
    private sealed record TextureToBufferCall(GpuTextureHandle Texture, GpuTextureCopyFootprint Footprint, GpuBufferRange Destination);
    private sealed record TextureCopyCall(GpuTextureHandle Source, GpuTextureCopyFootprint SourceFootprint,
        GpuTextureHandle Destination, GpuTextureCopyFootprint DestinationFootprint);

    private sealed partial class ExternalBackend
    {
        public GpuRasterPipelineHandle CreateRasterPipeline(GpuRasterPipelineDescription description, GpuShaderProgramDescription shaders)
        { observe(new RasterPipelineCreation(description, shaders)); return new RasterPipeline(); }
        public void DestroyRasterPipeline(GpuRasterPipelineHandle pipeline) => observe(pipeline);
        private sealed class RasterPipeline : GpuRasterPipelineHandle;
    }

    private sealed partial class ExternalCommands
    {
        public override void BeginRendering(ReadOnlySpan<GpuColorAttachment> colors, GpuDepthStencilAttachment? depthStencil = null)
            => observe(new BeginRenderingCall(colors.ToArray(), depthStencil));
        public override void EndRendering() => observe(new EndRenderingCall());
        public override void SetPipeline(GpuRasterPipelineHandle pipeline) => observe(new RasterPipelineCall(pipeline));
        public override void SetViewportAndScissor(GpuViewport viewport, GpuScissorRect scissor) => observe(new ViewportCall(viewport, scissor));
        public override void SetStencilReference(uint reference) => observe(new StencilCall(reference));
        public override void SetBlendConstant(GpuClearColor color) => observe(new BlendConstantCall(color));
        public override void SetBindings(uint group, GpuBindingsHandle bindings, ReadOnlySpan<uint> dynamicOffsets = default)
            => observe(new RasterBindingsCall(group, bindings, dynamicOffsets.ToArray()));
        public override void SetRootData(ReadOnlySpan<byte> bytes) => observe(new RasterRootCall(bytes.ToArray()));
        public override void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
            => observe(new DrawCall(vertexCount, instanceCount, firstVertex, firstInstance));
        public override void DrawIndexed(GpuBufferRange indices, GpuIndexFormat format, uint indexCount,
            uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
            => observe(new DrawIndexedCall(indices, format, indexCount, instanceCount, firstIndex, baseVertex, firstInstance));
        public override void DrawIndirect(GpuBufferRange arguments) => observe(new DrawIndirectCall(arguments));
        public override void DrawIndexedIndirect(GpuBufferRange indices, GpuIndexFormat format, GpuBufferRange arguments)
            => observe(new DrawIndexedIndirectCall(indices, format, arguments));
        public override void CopyBufferToTexture(GpuBufferRange source, GpuTextureHandle texture, GpuTextureCopyFootprint footprint)
            => observe(new BufferToTextureCall(source, texture, footprint));
        public override void CopyTextureToBuffer(GpuTextureHandle texture, GpuTextureCopyFootprint footprint, GpuBufferRange destination)
            => observe(new TextureToBufferCall(texture, footprint, destination));
        public override void CopyTexture(GpuTextureHandle source, GpuTextureCopyFootprint sourceFootprint,
            GpuTextureHandle destination, GpuTextureCopyFootprint destinationFootprint)
            => observe(new TextureCopyCall(source, sourceFootprint, destination, destinationFootprint));
    }
}
