namespace Lumyte.Graphics.Native.Tests.Device;

public sealed partial class ExternalNativeGpuBackendTests
{
    [Fact]
    public void ConsumerCreatesAndDestroysPrivateRasterPipelinesThroughPublicContracts()
    {
        List<object> observed = [];
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, observeRaster: observed.Add);
        var description = new NativeGpuRasterPipelineDescription
        {
            ColorTargets = [new(GpuFormat.Rgba8Unorm, GpuColorWriteMask.Red,
                new(Enabled: true, SourceColorFactor: NativeGpuBlendFactor.SourceAlpha,
                    DestinationColorFactor: NativeGpuBlendFactor.OneMinusSourceAlpha))],
            DepthStencilFormat = GpuFormat.Depth24PlusStencil8,
            Topology = NativeGpuPrimitiveTopology.TriangleStrip,
            CullMode = NativeGpuCullMode.Back,
            FrontFace = NativeGpuFrontFace.Clockwise,
            SampleCount = 4
        };
        byte[] code = [3, 5, 7, 11, 13, 17];
        var vertex = new NativeGpuShaderCode
        {
            Stage = GpuShaderStage.Vertex, Code = code.AsMemory(1, 4), EntryPoint = "drawVertex"
        };
        var program = new NativeGpuShaderProgram(vertex);

        NativeGpuRasterPipelineHandle pipeline = backend.CreateRasterPipeline(description, program);
        backend.DestroyRasterPipeline(pipeline);

        Assert.Collection(observed,
            value =>
            {
                RasterCreation creation = Assert.IsType<RasterCreation>(value);
                Assert.Same(description, creation.Description);
                Assert.Same(program, creation.Program);
                Assert.Same(vertex, creation.Program.Vertex);
            },
            value => Assert.Equal(new RasterDestruction(pipeline), Assert.IsType<RasterDestruction>(value)));
    }

    [Fact]
    public void ConsumerRecordsIndependentRasterStateInCallOrder()
    {
        List<object> observed = [];
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, observeCommands: observed.Add);
        var program = new NativeGpuShaderProgram(new NativeGpuShaderCode
        {
            Stage = GpuShaderStage.Vertex, Code = new byte[] { 1 }
        });
        NativeGpuRasterPipelineHandle pipeline = backend.CreateRasterPipeline(new(), program);
        var state = new NativeGpuDepthStencilState(DepthTest: true, DepthCompare: GpuCompareOp.Greater,
            StencilTest: true, StencilReadMask: 31, StencilWriteMask: 127,
            Front: new(PassOp: NativeGpuStencilOperation.IncrementWrap, Reference: 0x81234567),
            Back: new(PassOp: NativeGpuStencilOperation.DecrementClamp, Reference: 0xfedcba98));
        var viewport = new NativeGpuViewport(3.25f, 9.5f, 640.5f, 360.25f, 0.25f, 0.75f);
        var scissor = new NativeGpuScissorRect(-17, 19, uint.MaxValue - 2, 37);

        try
        {
            using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
            commands.SetPipeline(pipeline);
            commands.SetDepthStencilState(state);
            commands.SetViewport(viewport);
            commands.SetScissor(scissor);

            Assert.Collection(observed,
                value => Assert.Equal(new RasterSelection(pipeline), Assert.IsType<RasterSelection>(value)),
                value => Assert.Equal(new DepthStencilSelection(state), Assert.IsType<DepthStencilSelection>(value)),
                value => Assert.Equal(new ViewportSelection(viewport), Assert.IsType<ViewportSelection>(value)),
                value => Assert.Equal(new ScissorSelection(scissor), Assert.IsType<ScissorSelection>(value)));
        }
        finally { backend.DestroyRasterPipeline(pipeline); }
    }

    [Fact]
    public void ConsumerCanCopyAttachmentSpanValuesWithoutOwningTheirViews()
    {
        List<object> observed = [];
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, observeCommands: observed.Add);
        var colorView = new ConsumerRenderView(NativeGpuRenderViewFlags.None);
        var depthView = new ConsumerRenderView(NativeGpuRenderViewFlags.DepthReadOnly);
        var color = new NativeGpuColorAttachment(colorView, NativeGpuLoadOp.Clear,
            NativeGpuStoreOp.Store, new(0.25f, 0.5f, 0.75f, 1));
        NativeGpuColorAttachment[] attachments = [default, color, default];
        var depth = new NativeGpuDepthStencilAttachment(depthView,
            StencilLoadOp: NativeGpuLoadOp.Load, StencilStoreOp: NativeGpuStoreOp.Discard);
        using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();

        commands.BeginRendering(attachments.AsSpan(1, 1), depth);
        attachments[1] = default;
        commands.EndRendering();

        Assert.Collection(observed,
            value =>
            {
                RenderingBegin begin = Assert.IsType<RenderingBegin>(value);
                Assert.Equal(color, Assert.Single(begin.Colors));
                Assert.Equal(depth, begin.DepthStencil);
            },
            value => Assert.IsType<RenderingEnd>(value));
    }

    [Fact]
    public void ConsumerCanOmitDepthStencilAndUseDefaultDrawArguments()
    {
        List<object> observed = [];
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, observeCommands: observed.Add);
        var view = new ConsumerRenderView(NativeGpuRenderViewFlags.None);
        using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();

        commands.BeginRendering([new(view)]);
        commands.Draw([], 3);
        commands.EndRendering();

        Assert.Collection(observed,
            value =>
            {
                RenderingBegin begin = Assert.IsType<RenderingBegin>(value);
                Assert.Same(view, Assert.Single(begin.Colors).View);
                Assert.Null(begin.DepthStencil);
            },
            value =>
            {
                DirectDraw draw = Assert.IsType<DirectDraw>(value);
                Assert.Empty(draw.RootData);
                Assert.Equal((3u, 1u, 0u, 0u), (draw.VertexCount, draw.InstanceCount, draw.FirstVertex, draw.FirstInstance));
            },
            value => Assert.IsType<RenderingEnd>(value));
    }

    [Fact]
    public void ConsumerForwardsDirectDrawCountsAndRootSpanWithoutNarrowing()
    {
        List<object> observed = [];
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, observeCommands: observed.Add);
        byte[] input = [1, 3, 5, 7, 11, 13, 17, 19];
        using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();

        commands.Draw(input.AsSpan(2, 4), uint.MaxValue - 1, 23, 0x81234567, 0xfedcba98);
        input.AsSpan().Clear();

        DirectDraw draw = Assert.IsType<DirectDraw>(Assert.Single(observed));
        Assert.Equal(new byte[] { 5, 7, 11, 13 }, draw.RootData);
        Assert.Equal((uint.MaxValue - 1, 23u, 0x81234567u, 0xfedcba98u),
            (draw.VertexCount, draw.InstanceCount, draw.FirstVertex, draw.FirstInstance));
    }

    [Theory]
    [InlineData(NativeGpuIndexFormat.Uint16)]
    [InlineData(NativeGpuIndexFormat.Uint32)]
    public void ConsumerForwardsFullWidthIndexRangesAndSignedBaseVertex(NativeGpuIndexFormat format)
    {
        List<object> observed = [];
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, observeCommands: observed.Add);
        NativeGpuHeap heap = backend.CreateGpuHeap(1ul << 43, 256, NativeGpuMemoryKind.GpuOnly, []);
        NativeGpuLinearRegion region = backend.CreateLinearRegion(1ul << 41, heap, 1ul << 42);
        var indices = new NativeGpuRange(region, (1ul << 40) + 32, 128);

        try
        {
            using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
            commands.DrawIndexed([2, 3, 5, 7], indices, format, 0x81234567, 19, 23, int.MinValue + 7, 29);

            IndexedDraw draw = Assert.IsType<IndexedDraw>(Assert.Single(observed));
            Assert.Equal(new byte[] { 2, 3, 5, 7 }, draw.RootData);
            Assert.Equal((indices, format, 0x81234567u, 19u, 23u, int.MinValue + 7, 29u),
                (draw.Indices, draw.Format, draw.IndexCount, draw.InstanceCount,
                    draw.FirstIndex, draw.BaseVertex, draw.FirstInstance));
        }
        finally
        {
            backend.DestroyLinearRegion(region);
            backend.DestroyGpuHeap(heap);
        }
    }

    [Fact]
    public void ConsumerUsesDefaultIndexedDrawArguments()
    {
        List<object> observed = [];
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, observeCommands: observed.Add);
        NativeGpuHeap heap = backend.CreateGpuHeap(256, 256, NativeGpuMemoryKind.GpuOnly, []);
        NativeGpuLinearRegion region = backend.CreateLinearRegion(256, heap, 0);
        var indices = new NativeGpuRange(region, 16, 12);

        try
        {
            using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
            commands.DrawIndexed([], indices, NativeGpuIndexFormat.Uint16, 6);

            IndexedDraw draw = Assert.IsType<IndexedDraw>(Assert.Single(observed));
            Assert.Empty(draw.RootData);
            Assert.Equal((6u, 1u, 0u, 0, 0u),
                (draw.IndexCount, draw.InstanceCount, draw.FirstIndex, draw.BaseVertex, draw.FirstInstance));
        }
        finally
        {
            backend.DestroyLinearRegion(region);
            backend.DestroyGpuHeap(heap);
        }
    }

    [Fact]
    public void ConsumerForwardsIndirectArgumentsSeparatelyFromRootAndIndexData()
    {
        List<object> observed = [];
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, observeCommands: observed.Add);
        NativeGpuHeap heap = backend.CreateGpuHeap(1ul << 43, 256, NativeGpuMemoryKind.GpuOnly, []);
        NativeGpuLinearRegion region = backend.CreateLinearRegion(1ul << 41, heap, 1ul << 42);
        var indices = new NativeGpuRange(region, 64, 128);
        var arguments = new NativeGpuRange(region, (1ul << 40) + 32, 20);
        byte[] root = [13, 17, 19, 23, 29, 31, 37, 41];

        try
        {
            using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
            commands.DrawIndirect(root.AsSpan(0, 4), arguments.Slice(0, 16));
            commands.DrawIndexedIndirect(root.AsSpan(4, 4), indices, NativeGpuIndexFormat.Uint32, arguments);
            root.AsSpan().Clear();

            Assert.Collection(observed,
                value =>
                {
                    IndirectDraw draw = Assert.IsType<IndirectDraw>(value);
                    Assert.Equal(new byte[] { 13, 17, 19, 23 }, draw.RootData);
                    Assert.Equal(arguments.Slice(0, 16), draw.Arguments);
                },
                value =>
                {
                    IndirectIndexedDraw draw = Assert.IsType<IndirectIndexedDraw>(value);
                    Assert.Equal(new byte[] { 29, 31, 37, 41 }, draw.RootData);
                    Assert.Equal((indices, NativeGpuIndexFormat.Uint32, arguments),
                        (draw.Indices, draw.Format, draw.Arguments));
                });
        }
        finally
        {
            backend.DestroyLinearRegion(region);
            backend.DestroyGpuHeap(heap);
        }
    }

    private sealed record RasterCreation(NativeGpuRasterPipelineDescription Description, NativeGpuShaderProgram Program);
    private sealed record RasterDestruction(NativeGpuRasterPipelineHandle Pipeline);
    private sealed record RasterSelection(NativeGpuRasterPipelineHandle Pipeline);
    private sealed record DepthStencilSelection(NativeGpuDepthStencilState State);
    private sealed record ViewportSelection(NativeGpuViewport Viewport);
    private sealed record ScissorSelection(NativeGpuScissorRect Scissor);
    private sealed record RenderingBegin(NativeGpuColorAttachment[] Colors, NativeGpuDepthStencilAttachment? DepthStencil);
    private sealed record RenderingEnd;
    private sealed record DirectDraw(byte[] RootData, uint VertexCount, uint InstanceCount, uint FirstVertex, uint FirstInstance);
    private sealed record IndexedDraw(byte[] RootData, NativeGpuRange Indices, NativeGpuIndexFormat Format,
        uint IndexCount, uint InstanceCount, uint FirstIndex, int BaseVertex, uint FirstInstance);
    private sealed record IndirectDraw(byte[] RootData, NativeGpuRange Arguments);
    private sealed record IndirectIndexedDraw(byte[] RootData, NativeGpuRange Indices, NativeGpuIndexFormat Format,
        NativeGpuRange Arguments);
    private sealed class ConsumerRenderView(NativeGpuRenderViewFlags flags) : NativeGpuRenderViewHandle(flags);

    private sealed partial class ExternalBackend
    {
        public NativeGpuRasterPipelineHandle CreateRasterPipeline(NativeGpuRasterPipelineDescription description,
            NativeGpuShaderProgram program)
        {
            observeRaster?.Invoke(new RasterCreation(description, program));
            return new RasterPipeline();
        }

        public void DestroyRasterPipeline(NativeGpuRasterPipelineHandle pipeline)
            => observeRaster?.Invoke(new RasterDestruction((RasterPipeline)pipeline));

        private sealed class RasterPipeline : NativeGpuRasterPipelineHandle;
    }

    // The spy snapshots spans only to observe public argument delivery from another assembly.
    // Backend tests cover actual native recording, rendering defaults, and resource lifetimes.
    private sealed partial class ExternalCommands
    {
        public override void SetPipeline(NativeGpuRasterPipelineHandle pipeline)
            => observe?.Invoke(new RasterSelection(pipeline));
        public override void SetDepthStencilState(NativeGpuDepthStencilState state)
            => observe?.Invoke(new DepthStencilSelection(state));
        public override void SetViewport(NativeGpuViewport viewport)
            => observe?.Invoke(new ViewportSelection(viewport));
        public override void SetScissor(NativeGpuScissorRect scissor)
            => observe?.Invoke(new ScissorSelection(scissor));
        public override void BeginRendering(ReadOnlySpan<NativeGpuColorAttachment> colorAttachments,
            NativeGpuDepthStencilAttachment? depthStencilAttachment = null)
            => observe?.Invoke(new RenderingBegin(colorAttachments.ToArray(), depthStencilAttachment));
        public override void EndRendering() => observe?.Invoke(new RenderingEnd());
        public override void Draw(ReadOnlySpan<byte> rootData, uint vertexCount, uint instanceCount = 1,
            uint firstVertex = 0, uint firstInstance = 0)
            => observe?.Invoke(new DirectDraw(rootData.ToArray(), vertexCount, instanceCount, firstVertex, firstInstance));
        public override void DrawIndexed(ReadOnlySpan<byte> rootData, NativeGpuRange indices, NativeGpuIndexFormat format,
            uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
            => observe?.Invoke(new IndexedDraw(rootData.ToArray(), indices, format, indexCount,
                instanceCount, firstIndex, baseVertex, firstInstance));
        public override void DrawIndirect(ReadOnlySpan<byte> rootData, NativeGpuRange arguments)
            => observe?.Invoke(new IndirectDraw(rootData.ToArray(), arguments));
        public override void DrawIndexedIndirect(ReadOnlySpan<byte> rootData, NativeGpuRange indices,
            NativeGpuIndexFormat format, NativeGpuRange arguments)
            => observe?.Invoke(new IndirectIndexedDraw(rootData.ToArray(), indices, format, arguments));
    }
}
