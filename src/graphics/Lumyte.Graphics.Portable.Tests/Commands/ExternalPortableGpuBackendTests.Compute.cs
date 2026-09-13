using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Lumyte.Graphics.Portable.Tests.Device;

public sealed partial class ExternalPortableGpuBackendTests
{
    [Fact]
    public void ConsumerCreatesALogicalPipelineFromRawWgslAndExplicitProgramInputs()
    {
        const string wgsl = "@compute @workgroup_size(1) fn main() {}";
        List<object> observed = [];
        using IPortableGpuBackend backend = new ExternalBackend(observed.Add);
        GpuShaderModuleHandle module = backend.CreateShaderModule(wgsl);
        var description = new GpuShaderProgramDescription([new(module, GpuShaderStage.Compute, "main")], [], 16);

        GpuComputePipelineHandle pipeline = backend.CreateComputePipeline(description);
        backend.DestroyComputePipeline(pipeline);
        backend.DestroyShaderModule(module);

        Assert.Collection(observed,
            value => Assert.Equal(wgsl, Assert.IsType<ModuleCreation>(value).Wgsl),
            value => Assert.Same(description, Assert.IsType<PipelineCreation>(value).Description),
            value => Assert.Same(pipeline, value),
            value => Assert.Same(module, value));
    }

    [Fact]
    public void TypedRootForwardsItsExactBytesToAnExternalRecording()
    {
        List<object> observed = [];
        using IPortableGpuBackend backend = new ExternalBackend(observed.Add);
        using GpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
        var root = new ComputeRoot(0x12345678, 1.5f);

        commands.SetComputeRootData(in root);
        root = new(0, 0);

        byte[] bytes = Assert.IsType<RootCall>(Assert.Single(observed)).Bytes;
        Assert.Equal(8, bytes.Length);
        Assert.Equal((0x12345678u, 1.5f),
            (BinaryPrimitives.ReadUInt32LittleEndian(bytes), BitConverter.ToSingle(bytes, 4)));
    }

    [Fact]
    public void ExternalRecordingConsumesDynamicOffsetsBeforeSourceReuse()
    {
        List<object> observed = [];
        using IPortableGpuBackend backend = new ExternalBackend(observed.Add);
        var layout = backend.CreateBindingLayout([]);
        var bindings = backend.CreateBindings(layout, []);
        using GpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
        uint[] offsets = [256, 512];
        observed.Clear();

        commands.SetComputeBindings(3, bindings, offsets);
        Array.Fill(offsets, 0u);

        var call = Assert.IsType<ComputeBindingsCall>(Assert.Single(observed));
        Assert.Equal((3u, bindings), (call.Group, call.Bindings));
        Assert.Equal(new uint[] { 256, 512 }, call.Offsets);
        commands.Dispose();
        backend.DestroyBindings(bindings);
        backend.DestroyBindingLayout(layout);
    }

    [Fact]
    public void ExternalRecordingReceivesOrderedComputeAndBufferOperations()
    {
        List<object> observed = [];
        using IPortableGpuBackend backend = new ExternalBackend(observed.Add);
        var module = backend.CreateShaderModule("@compute @workgroup_size(1) fn main() {}");
        var pipeline = backend.CreateComputePipeline(new([new(module, GpuShaderStage.Compute, "main")], []));
        var source = backend.CreateBuffer(new((1ul << 40) + 64, GpuBufferUsage.IndirectArguments | GpuBufferUsage.CopySource));
        var destination = backend.CreateBuffer(new((1ul << 41) + 64, GpuBufferUsage.CopyDestination));
        var arguments = new GpuBufferRange(source, (1ul << 40) + 16, 12);
        var output = new GpuBufferRange(destination, (1ul << 41) + 16, 12);
        using GpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
        observed.Clear();

        commands.BeginCompute();
        commands.SetComputePipeline(pipeline);
        commands.Dispatch(64, 2);
        commands.DispatchIndirect(arguments);
        commands.EndCompute();
        commands.CopyBuffer(arguments, output);

        Assert.Collection(observed,
            value => Assert.IsType<BeginComputeCall>(value),
            value => Assert.Same(pipeline, Assert.IsType<ComputePipelineCall>(value).Pipeline),
            value => Assert.Equal(new DispatchCall(64, 2, 1), value),
            value => Assert.Equal(arguments, Assert.IsType<IndirectCall>(value).Arguments),
            value => Assert.IsType<EndComputeCall>(value),
            value => Assert.Equal(new CopyCall(arguments, output), value));
        commands.Dispose();
        backend.DestroyBuffer(destination);
        backend.DestroyBuffer(source);
        backend.DestroyComputePipeline(pipeline);
        backend.DestroyShaderModule(module);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private readonly record struct ComputeRoot(uint Index, float Weight);

    private sealed record ModuleCreation(string Wgsl);
    private sealed record PipelineCreation(GpuShaderProgramDescription Description);
    private sealed record BeginComputeCall;
    private sealed record EndComputeCall;
    private sealed record ComputePipelineCall(GpuComputePipelineHandle Pipeline);
    private sealed record ComputeBindingsCall(uint Group, GpuBindingsHandle Bindings, uint[] Offsets);
    private sealed record RootCall(byte[] Bytes);
    private sealed record DispatchCall(uint X, uint Y, uint Z);
    private sealed record IndirectCall(GpuBufferRange Arguments);
    private sealed record CopyCall(GpuBufferRange Source, GpuBufferRange Destination);

    private sealed partial class ExternalBackend
    {
        public IGpuQueue MainQueue { get; } = new ExternalQueue(observe);
        public GpuShaderModuleHandle CreateShaderModule(string wgsl)
        { observe(new ModuleCreation(wgsl)); return new Module(); }
        public void DestroyShaderModule(GpuShaderModuleHandle module) => observe(module);
        public GpuComputePipelineHandle CreateComputePipeline(GpuShaderProgramDescription shaders)
        { observe(new PipelineCreation(shaders)); return new Pipeline(); }
        public void DestroyComputePipeline(GpuComputePipelineHandle pipeline) => observe(pipeline);

        private sealed class Module : GpuShaderModuleHandle;
        private sealed class Pipeline : GpuComputePipelineHandle;
    }

    private sealed class ExternalCommands(Action<object> observe) : GpuCommandBuffer
    {
        public override void BeginCompute() => observe(new BeginComputeCall());
        public override void EndCompute() => observe(new EndComputeCall());
        public override void SetComputePipeline(GpuComputePipelineHandle pipeline) => observe(new ComputePipelineCall(pipeline));
        public override void SetComputeBindings(uint group, GpuBindingsHandle bindings, ReadOnlySpan<uint> dynamicOffsets = default)
            => observe(new ComputeBindingsCall(group, bindings, dynamicOffsets.ToArray()));
        public override void SetComputeRootData(ReadOnlySpan<byte> bytes) => observe(new RootCall(bytes.ToArray()));
        public override void Dispatch(uint x, uint y = 1, uint z = 1) => observe(new DispatchCall(x, y, z));
        public override void DispatchIndirect(GpuBufferRange arguments) => observe(new IndirectCall(arguments));
        public override void CopyBuffer(GpuBufferRange source, GpuBufferRange destination) => observe(new CopyCall(source, destination));
        public override void Dispose() => observe(this);
    }
}
