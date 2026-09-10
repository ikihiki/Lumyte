namespace Lumyte.Graphics.Native.Tests.Device;

public sealed partial class ExternalNativeGpuBackendTests
{
    [Fact]
    public void ConsumerPassesRawShaderAndEntryToAnExternalComputePipeline()
    {
        List<object> observed = [];
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, observeCompute: observed.Add);
        byte[] bytes = [99, 23, 41, 67, 89, 101];
        var shader = new NativeGpuShaderCode
        {
            Stage = GpuShaderStage.Compute,
            Code = bytes.AsMemory(1, 4),
            EntryPoint = "computeParticles",
        };
        var program = new NativeGpuShaderProgram(shader);

        NativeGpuComputePipelineHandle pipeline = backend.CreateComputePipeline(program);
        backend.DestroyComputePipeline(pipeline);

        Assert.Collection(observed,
            value =>
            {
                ComputeCreation creation = Assert.IsType<ComputeCreation>(value);
                Assert.Same(program, creation.Program);
                NativeGpuShaderCode code = Assert.IsType<NativeGpuShaderCode>(creation.Program.Compute);
                Assert.Equal((GpuShaderStage.Compute, "computeParticles"), (code.Stage, code.EntryPoint));
                Assert.Equal(new byte[] { 23, 41, 67, 89 }, code.Code.ToArray());
            },
            value => Assert.Same(pipeline, Assert.IsType<ComputeDestruction>(value).Pipeline));
    }

    [Fact]
    public void ConsumerPassesDirectAndIndirectWorkToAnExternalRecording()
    {
        List<object> observed = [];
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, observeCommands: observed.Add);
        var shader = new NativeGpuShaderCode { Stage = GpuShaderStage.Compute, Code = new byte[] { 1, 2, 3, 4 } };
        NativeGpuComputePipelineHandle pipeline = backend.CreateComputePipeline(new(shader));
        NativeGpuMemoryRequirements requirements = backend.GetLinearMemoryRequirements(1ul << 42, NativeGpuMemoryKind.GpuOnly);
        NativeGpuHeap heap = backend.CreateGpuHeap(requirements.Size, requirements.Alignment,
            NativeGpuMemoryKind.GpuOnly, [requirements.Compatibility]);
        NativeGpuLinearRegion region = backend.CreateLinearRegion(requirements.Size, heap, 0);
        var arguments = new NativeGpuRange(region, (1ul << 40) + 256, 12);
        Span<byte> root = stackalloc byte[] { 13, 29, 43, 61, 71, 89, 103, 127 };

        try
        {
            using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
            commands.SetComputePipeline(pipeline);
            commands.Dispatch(root, (1u << 31) + 3, 17, 23);
            commands.DispatchIndirect(root.Slice(4), arguments);
            root.Clear();

            Assert.Collection(observed,
                value => Assert.Same(pipeline, Assert.IsType<ComputeSelection>(value).Pipeline),
                value =>
                {
                    DirectCompute work = Assert.IsType<DirectCompute>(value);
                    Assert.Equal(((1u << 31) + 3, 17u, 23u), (work.X, work.Y, work.Z));
                    Assert.Equal(new byte[] { 13, 29, 43, 61, 71, 89, 103, 127 }, work.RootData);
                },
                value =>
                {
                    IndirectCompute work = Assert.IsType<IndirectCompute>(value);
                    Assert.Equal(arguments, work.Arguments);
                    Assert.Equal(new byte[] { 71, 89, 103, 127 }, work.RootData);
                });
        }
        finally
        {
            backend.DestroyLinearRegion(region);
            backend.DestroyGpuHeap(heap);
            backend.DestroyComputePipeline(pipeline);
        }
    }

    [Fact]
    public void ConsumerCanDispatchAnEmptyRootWithDefaultSecondaryAxes()
    {
        List<object> observed = [];
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, observeCommands: observed.Add);
        using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();

        commands.Dispatch([], 11);

        DirectCompute work = Assert.IsType<DirectCompute>(Assert.Single(observed));
        Assert.Empty(work.RootData);
        Assert.Equal((11u, 1u, 1u), (work.X, work.Y, work.Z));
    }

    [Fact]
    public void ConsumerReceivesDeviceLimitsWithoutNarrowingDescriptorOrGroupCounts()
    {
        using INativeGpuBackend backend = new ExternalBackend(_ => { });

        NativeGpuLimits limits = backend.Limits;

        Assert.Equal(new NativeGpuLimits(256, new(65535, 65535, 65535, ulong.MaxValue),
            new((1ul << 40) + 64, 32, 48, 32, 24, 16, 16, 8)), limits);
    }

    private sealed record ComputeCreation(NativeGpuShaderProgram Program);
    private sealed record ComputeDestruction(NativeGpuComputePipelineHandle Pipeline);
    private sealed record ComputeSelection(NativeGpuComputePipelineHandle Pipeline);
    private sealed record DirectCompute(byte[] RootData, uint X, uint Y, uint Z);
    private sealed record IndirectCompute(byte[] RootData, NativeGpuRange Arguments);

    private sealed partial class ExternalBackend
    {
        public NativeGpuComputePipelineHandle CreateComputePipeline(NativeGpuShaderProgram program)
        {
            observeCompute?.Invoke(new ComputeCreation(program));
            return new ComputePipeline();
        }

        public void DestroyComputePipeline(NativeGpuComputePipelineHandle pipeline)
            => observeCompute?.Invoke(new ComputeDestruction((ComputePipeline)pipeline));

        private sealed class ComputePipeline : NativeGpuComputePipelineHandle;
    }

    // Snapshotting here only demonstrates that a separate assembly can receive a span and
    // keep the work's values. Actual GPU recording/snapshot behavior is tested by each backend.
    private sealed partial class ExternalCommands
    {
        public override void SetComputePipeline(NativeGpuComputePipelineHandle pipeline)
            => observe?.Invoke(new ComputeSelection(pipeline));

        public override void Dispatch(ReadOnlySpan<byte> rootData, uint x, uint y = 1, uint z = 1)
            => observe?.Invoke(new DirectCompute(rootData.ToArray(), x, y, z));

        public override void DispatchIndirect(ReadOnlySpan<byte> rootData, NativeGpuRange arguments)
            => observe?.Invoke(new IndirectCompute(rootData.ToArray(), arguments));
    }
}
