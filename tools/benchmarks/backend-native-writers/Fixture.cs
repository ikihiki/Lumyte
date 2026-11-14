using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Vulkan;
namespace BackendNativeWriters;
internal sealed class Fixture : IDisposable
{
    private readonly VulkanDevice _device;
    private readonly IGraphicsBuffer<uint> _output, _readback;
    private readonly IGraphicsShaderDataBuffer<Inputs> _upload;
    private readonly IArgumentTable _table;
    private readonly IGraphicsShader _shader;
    private readonly IGraphicsComputePipeline _pipeline;
    private readonly Inputs[] _values;
    private readonly IGpuRef<uint>[] _refs;
    private readonly uint[] _actual;
    private readonly int _count;
    private uint _generation;
    private bool _mapped;
    private Fixture(int count, ShaderArtifact artifact)
    {
        _count = count;
        _device = VulkanDevice.Create();
        if (_device.ValidationEnabled) throw new InvalidOperationException("Validation must be disabled.");
        _output = _device.CreateBuffer<uint>(new() { Count = (ulong)count, Usage = BufferUsage.ShaderRead | BufferUsage.ShaderWrite | BufferUsage.CopySource });
        _readback = _device.CreateBuffer<uint>(new() { Count = (ulong)count, Memory = MemoryPreference.Readback, Usage = BufferUsage.CopyDestination });
        _upload = _device.CreateShaderDataBuffer<Inputs>(artifact, (ulong)count, MemoryPreference.Upload);
        _table = _device.CreateArgumentTable(new() { BufferCapacity = 1 });
        var whole = _table.WriteBuffer(0, _output.SliceElements(0, (ulong)count), ShaderBufferAccess.ReadWrite);
        _refs = new IGpuRef<uint>[count];
        _values = new Inputs[count];
        _actual = new uint[count];
        for (int i = 0; i < count; i++) { _refs[i] = whole.GetElement((ulong)i); _values[i] = new(); }
        Advance();
        var snapshot = IShaderArguments.Capture(in _values[0]);
        if (snapshot.Data.Length != 48 || snapshot.Values.Count != 2) throw new InvalidOperationException("Expected generated 48-byte wire/two references.");
        _shader = _device.CreateShader(artifact);
        _pipeline = _device.CreateComputePipeline(new() { ComputeShader = _shader });
    }
    internal static async Task<Fixture> Create(int count) => new(count, await new SlangShaderCompiler().CompileAsync(new()
    {
        Source = Source, Stage = ShaderStage.Compute, Target = ShaderTarget.SpirV,
    }));
    private void Advance()
    {
        _generation = (_generation % 1_000_000) + 1;
        for (int i = 0; i < _count; i++)
        {
            var value = _values[i];
            value.Value = _generation + (uint)i;
            value.Padding0 = 17; value.Padding1 = 23; value.Padding2 = 29;
            // Rotate identities while retaining the same native output allocation/address.
            value.Source = _refs[(i + 1 + (int)(_generation % (uint)_count)) % _count];
            value.Target = _refs[i];
        }
    }
    private void Record(IGraphicsCommandBuffer commands, bool dispatch)
    {
        var compute = commands.BeginComputePass(new());
        try
        {
            compute.SetPipeline(_pipeline); compute.SetArgumentTable(_table);
            for (int i = 0; i < _count; i++)
            {
                compute.SetArguments(in _values[i]);
                if (dispatch) compute.Dispatch(1);
            }
        }
        finally { compute.End(); }
    }
    internal void SetRecordCpu()
    {
        Advance();
        // Fresh Recording owner/generation per invocation. End pass, discard unsubmitted
        // commands via Dispose; do not Finish/Submit and do not reuse an accumulating owner.
        using var commands = _device.CreateCommandBuffer(new());
        Record(commands, false);
    }
    internal async Task FullCompletion()
    {
        Advance();
        using var commands = _device.CreateCommandBuffer(new());
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = _output.SliceElements(0, (ulong)_count), Before = new(PipelineStage.ComputeShader | PipelineStage.Copy, ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite | ResourceAccess.CopyRead), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite) });
        Record(commands, true);
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = _output.SliceElements(0, (ulong)_count), Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = _readback.SliceElements(0, (ulong)_count), Before = new(PipelineStage.Host | PipelineStage.Copy, ResourceAccess.HostRead | ResourceAccess.CopyWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyWrite) });
        commands.CopyBuffer(_output.SliceElements(0, (ulong)_count), _readback.SliceElements(0, (ulong)_count));
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = _readback.SliceElements(0, (ulong)_count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using var submission = _device.Queue.Submit([commands]);
        await submission.WaitAsync();
        // Dispose order: completed submission first, commands second; no CPU completion fake.
    }
    internal async Task AssertOutput()
    {
        await _readback.MapAsync();
        try { _readback.CopyTo(_actual); } finally { _readback.Unmap(); }
        for (int i = 0; i < _count; i++)
            if (_actual[i] != _generation + (uint)i + 69) throw new InvalidOperationException($"GPU oracle failed at {i}: {_actual[i]}");
    }
    internal async Task Map() { await _upload.MapAsync(); _mapped = true; }
    internal void CopyFromCpu() { Advance(); _upload.CopyFrom(_values); }
    internal void AssertMetadata()
    {
        for (int i = 0; i < _count; i++)
        {
            var metadata = ((IShaderDataSource)_upload).ReadReferences((ulong)i);
            if (metadata.References.Count != 2 || !ReferenceEquals(metadata.References[0], _values[i].Source) || !ReferenceEquals(metadata.References[1], _values[i].Target))
                throw new InvalidOperationException("Typed upload reference identity/order mismatch.");
        }
    }
    public void Dispose()
    {
        if (_mapped) _upload.Unmap();
        _pipeline.Dispose(); _shader.Dispose(); _table.Dispose(); _upload.Dispose(); _readback.Dispose(); _output.Dispose(); _device.Dispose();
    }
    private const string Source = """
        #include "lumyte.slang"
        struct Inputs
        {
            uint Value; uint Padding0; uint Padding1; uint Padding2;
            GpuRWBufferRef<uint> Source;
            GpuRWBufferRef<uint> Target;
        };
        ConstantBuffer<Inputs> arguments;
        [shader("compute")]
        [numthreads(1, 1, 1)]
        void main() { arguments.Target.Store(0, arguments.Value + arguments.Padding0 + arguments.Padding1 + arguments.Padding2); }
        """;
}
