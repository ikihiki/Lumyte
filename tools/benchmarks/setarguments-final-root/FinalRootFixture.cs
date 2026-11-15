using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Graphics.FinalRoot.Benchmarks;

internal sealed class FinalRootFixture : IDisposable
{
    private readonly VulkanDevice _device;
    private readonly IGraphicsBuffer<uint> _output;
    private readonly IGraphicsBuffer<uint> _readback;
    private readonly IArgumentTable _table;
    private readonly IGraphicsShader _shader;
    private readonly IGraphicsComputePipeline _pipeline;
    private readonly IGpuRef<uint>[] _refs;
    private readonly ReferenceRoot _arguments = new();
    // Keep distinct diagnostic providers alive for explicit snapshots; never mutate them.
    private readonly ReferenceRoot[] _snapshotSources;
    private readonly ShaderValueSnapshot[] _snapshots;
    private readonly uint[] _actual;
    private uint _generation;

    private FinalRootFixture(int count, ShaderArtifact artifact)
    {
        Count = count;
        _device = VulkanDevice.Create();
        Require(!_device.ValidationEnabled, "Managed validation must be disabled for measurement.");
        ulong n = (ulong)count;
        _output = _device.CreateBuffer<uint>(new() { Count = n, Usage = BufferUsage.ShaderRead | BufferUsage.ShaderWrite | BufferUsage.CopySource });
        _readback = _device.CreateBuffer<uint>(new() { Count = n, Memory = MemoryPreference.Readback, Usage = BufferUsage.CopyDestination });
        _table = _device.CreateArgumentTable(new() { BufferCapacity = 1 });
        IGpuRef<uint> whole = _table.WriteBuffer(0, _output.SliceElements(0, n), ShaderBufferAccess.ReadWrite);
        _refs = new IGpuRef<uint>[count];
        _snapshotSources = new ReferenceRoot[count];
        _snapshots = new ShaderValueSnapshot[count];
        _actual = new uint[count];
        for (int i = 0; i < count; i++)
        {
            _refs[i] = whole.GetElement((ulong)i);
            _snapshotSources[i] = new() { Value = 100u + (uint)i, Output = _refs[i] };
            _snapshots[i] = IShaderArguments.Capture(in _snapshotSources[i]);
            Require(_snapshots[i].Data.Length == 20, "Generated root wire size must be 20 bytes (32-byte native uniform capacity).");
            Require(BitConverter.ToUInt32(_snapshots[i].Data.Span) == 100u + (uint)i, "Generated scalar capture mismatch.");
            Require(_snapshots[i].Values.Count == 1 && _snapshots[i].Values[0].Offset == 4 &&
                ReferenceEquals(_snapshots[i].Values[0].Reference, _refs[i]), "Generated 16-byte output slot must start at byte 4.");
        }
        _shader = _device.CreateShader(artifact);
        _pipeline = _device.CreateComputePipeline(new() { ComputeShader = _shader });
    }

    internal int Count { get; }

    internal static async Task<FinalRootFixture> CreateAsync(int count)
    {
        if (count is not (1 or 64)) throw new ArgumentOutOfRangeException(nameof(count));
        ShaderArtifact artifact = await new SlangShaderCompiler().CompileAsync(new()
        {
            Source = ShaderSource,
            Stage = ShaderStage.Compute,
            Target = ShaderTarget.SpirV,
        });
        return new(count, artifact);
    }

    // Both timed methods record the same number of native dispatches and no copies/readback.
    internal async Task InterfaceSetDispatchComplete()
    {
        _generation = (_generation % 1_000_000) + 1;
        using IGraphicsCommandBuffer commands = _device.CreateCommandBuffer(new());
        PrepareOutput(commands);
        IComputeEncoder compute = commands.BeginComputePass(new());
        try
        {
            compute.SetPipeline(_pipeline);
            compute.SetArgumentTable(_table);
            for (int i = 0; i < Count; i++)
            {
                _arguments.Value = _generation + (uint)i;
                _arguments.Output = _refs[i];
                compute.SetArguments(in _arguments);
                compute.Dispatch(1);
            }
        }
        finally { compute.End(); }
        commands.Finish();
        using IGraphicsSubmission submission = _device.Queue.Submit([commands]);
        await submission.WaitAsync();
    }

    internal async Task ExplicitSnapshotSetDispatchComplete()
    {
        using IGraphicsCommandBuffer commands = _device.CreateCommandBuffer(new());
        PrepareOutput(commands);
        IComputeEncoder compute = commands.BeginComputePass(new());
        try
        {
            compute.SetPipeline(_pipeline);
            compute.SetArgumentTable(_table);
            for (int i = 0; i < Count; i++)
            {
                compute.SetArguments(_snapshots[i]);
                compute.Dispatch(1);
            }
        }
        finally { compute.End(); }
        commands.Finish();
        using IGraphicsSubmission submission = _device.Queue.Submit([commands]);
        await submission.WaitAsync();
    }

    private void PrepareOutput(IGraphicsCommandBuffer commands)
    {
        // Covers initial use, previous timed ComputeWrite, and untimed verification CopyRead.
        // Host waiting does not replace an explicit device-side WAW memory dependency.
        commands.Barrier(new BufferBarrierDesc<uint>
        {
            Buffer = _output.SliceElements(0, (ulong)Count),
            Before = new(PipelineStage.ComputeShader | PipelineStage.Copy,
                ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite | ResourceAccess.CopyRead),
            After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite),
        });
    }

    internal async Task VerifyAsync(bool snapshot)
    {
        // Entirely separate, untimed copy submission. Never call a second compute implementation.
        using IGraphicsCommandBuffer commands = _device.CreateCommandBuffer(new());
        ulong n = (ulong)Count;
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = _output.SliceElements(0, n), Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = _readback.SliceElements(0, n), Before = new(PipelineStage.Host | PipelineStage.Copy, ResourceAccess.HostRead | ResourceAccess.CopyWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyWrite) });
        commands.CopyBuffer(_output.SliceElements(0, n), _readback.SliceElements(0, n));
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = _readback.SliceElements(0, n), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = _device.Queue.Submit([commands]);
        await submission.WaitAsync();
        await _readback.MapAsync();
        try { _readback.CopyTo(_actual); }
        finally { _readback.Unmap(); }
        uint start = snapshot ? 100u : _generation;
        for (int i = 0; i < Count; i++)
            Require(_actual[i] == start + (uint)i,
                $"{(snapshot ? "snapshot" : "interface")} Count={Count} output[{i}]: expected {start + (uint)i}, got {_actual[i]}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public void Dispose()
    {
        _pipeline.Dispose();
        _shader.Dispose();
        _table.Dispose();
        _readback.Dispose();
        _output.Dispose();
        _device.Dispose();
    }

    private const string ShaderSource = """
        #include "lumyte.slang"
        struct ReferenceRoot
        {
            uint Value;
            GpuRWBufferRef<uint> Output;
        };
        ConstantBuffer<ReferenceRoot> arguments;
        [shader("compute")]
        [numthreads(1, 1, 1)]
        void main()
        {
            arguments.Output.Store(0, arguments.Value);
        }
        """;
}
