namespace Lumyte.Graphics.Native.Tests.Device;

public sealed partial class ExternalNativeGpuBackendTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConsumerRecordsMeshProgramsThroughTheSharedRasterContract(bool amplification)
    {
        List<object> observed = [];
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, observeCommands: observed.Add);
        var mesh = new NativeGpuShaderCode { Stage = GpuShaderStage.Mesh, Code = new byte[] { 1 } };
        var amplify = new NativeGpuShaderCode { Stage = GpuShaderStage.Amplification, Code = new byte[] { 2 } };
        var program = amplification ? new NativeGpuShaderProgram(amplify, mesh) : new NativeGpuShaderProgram(mesh);
        NativeGpuRasterPipelineHandle pipeline = backend.CreateRasterPipeline(new()
        {
            Topology = null,
            MeshOutputTopology = NativeGpuMeshOutputTopology.Triangle,
            ColorTargets = [new(GpuFormat.Rgba8Unorm)]
        }, program);
        var view = new ConsumerRenderView(NativeGpuRenderViewFlags.None);
        var state = new NativeGpuDepthStencilState();

        try
        {
            using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
            commands.BeginRendering([new(view)]);
            commands.SetPipeline(pipeline);
            commands.SetDepthStencilState(state);
            commands.DispatchMesh([], 17);
            commands.EndRendering();

            Assert.Collection(observed,
                value => Assert.Same(view, Assert.Single(Assert.IsType<RenderingBegin>(value).Colors).View),
                value => Assert.Equal(new RasterSelection(pipeline), Assert.IsType<RasterSelection>(value)),
                value => Assert.Equal(new DepthStencilSelection(state), Assert.IsType<DepthStencilSelection>(value)),
                value =>
                {
                    DirectMesh work = Assert.IsType<DirectMesh>(value);
                    Assert.Empty(work.RootData);
                    Assert.Equal((17u, 1u, 1u), (work.X, work.Y, work.Z));
                },
                value => Assert.IsType<RenderingEnd>(value));
        }
        finally { backend.DestroyRasterPipeline(pipeline); }
    }

    [Fact]
    public void ConsumerReceivesDirectMeshRootAndGroupCountsWithoutNarrowing()
    {
        List<object> observed = [];
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, observeCommands: observed.Add);
        Span<byte> root = stackalloc byte[] { 1, 3, 5, 7, 11, 13, 17, 19 };
        using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();

        commands.DispatchMesh(root.Slice(2, 4), 0x81234567, 0xfedcba98, uint.MaxValue);
        root.Clear();

        DirectMesh work = Assert.IsType<DirectMesh>(Assert.Single(observed));
        Assert.Equal(new byte[] { 5, 7, 11, 13 }, work.RootData);
        Assert.Equal((0x81234567u, 0xfedcba98u, uint.MaxValue), (work.X, work.Y, work.Z));
    }

    [Fact]
    public void ConsumerReceivesIndirectMeshRangeWithoutRebasingOrReadingItsContents()
    {
        List<object> observed = [];
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, observeCommands: observed.Add);
        NativeGpuHeap heap = backend.CreateGpuHeap(1ul << 43, 256, NativeGpuMemoryKind.GpuOnly, []);
        NativeGpuLinearRegion region = backend.CreateLinearRegion(1ul << 41, heap, 1ul << 42);
        var arguments = new NativeGpuRange(region, (1ul << 40) + 32, 12);
        byte[] root = [2, 3, 5, 7, 11, 13, 17, 19];

        try
        {
            using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
            commands.DispatchMeshIndirect(root.AsSpan(4), arguments);
            root.AsSpan().Clear();

            IndirectMesh work = Assert.IsType<IndirectMesh>(Assert.Single(observed));
            Assert.Equal(new byte[] { 11, 13, 17, 19 }, work.RootData);
            Assert.Equal(arguments, work.Arguments);
        }
        finally
        {
            backend.DestroyLinearRegion(region);
            backend.DestroyGpuHeap(heap);
        }
    }

    [Fact]
    public void ConsumerReceivesIndependentMeshAndAmplificationLimits()
    {
        var meshDispatch = new NativeGpuDispatchLimits(0x81234567, 23, 29, 1ul << 40);
        var amplificationDispatch = new NativeGpuDispatchLimits(31, 37, 41, 1ul << 48);
        var expected = new NativeGpuMeshShaderLimits(meshDispatch, amplificationDispatch, 256, 512, 32768);
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, meshShaderLimits: expected);

        NativeGpuMeshShaderLimits? actual = backend.Limits.MeshShader;

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ConsumerCanDistinguishMeshOnlySupportFromMissingMeshSupport()
    {
        var meshOnly = new NativeGpuMeshShaderLimits(new(65535, 65535, 65535, 1ul << 22), null, 256, 256, 0);
        using INativeGpuBackend unsupported = new ExternalBackend(_ => { });
        using INativeGpuBackend mesh = new ExternalBackend(_ => { }, meshShaderLimits: meshOnly);

        Assert.Collection(new[] { unsupported.Limits.MeshShader, mesh.Limits.MeshShader },
            value => Assert.Null(value),
            value => Assert.Equal(meshOnly, value));
    }

    private sealed record DirectMesh(byte[] RootData, uint X, uint Y, uint Z);
    private sealed record IndirectMesh(byte[] RootData, NativeGpuRange Arguments);

    // Only the public assembly boundary is exercised here. Native recording and root
    // snapshots on submitted GPU work are verified in the backend integration projects.
    private sealed partial class ExternalCommands
    {
        public override void DispatchMesh(ReadOnlySpan<byte> rootData, uint x, uint y = 1, uint z = 1)
            => observe?.Invoke(new DirectMesh(rootData.ToArray(), x, y, z));

        public override void DispatchMeshIndirect(ReadOnlySpan<byte> rootData, NativeGpuRange arguments)
            => observe?.Invoke(new IndirectMesh(rootData.ToArray(), arguments));
    }
}
