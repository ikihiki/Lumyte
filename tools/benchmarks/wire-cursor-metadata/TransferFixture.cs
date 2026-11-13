using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Shared;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Graphics.UploadStrategies.Benchmarks;

internal sealed class TransferFixture : IDisposable
{
    private static readonly Lazy<Task<ShaderArtifact>> Compiled = new(() => new SlangShaderCompiler().CompileAsync(new()
    {
        Source = ShaderSource,
        Stage = ShaderStage.Compute,
        Target = ShaderTarget.SpirV,
    }));

    private readonly VulkanDevice _device;
    private readonly ShaderDataLayout _numericLayout;
    private readonly ShaderDataLayout _referenceLayout;
    private readonly IArgumentTable _table;
    private readonly IGraphicsBuffer<uint> _inputUpload;
    private readonly IGraphicsBuffer<uint> _input;
    private readonly IGraphicsBuffer<uint> _output;
    private readonly IGraphicsBuffer<uint> _results;
    private readonly IGraphicsBuffer<uint> _readback;
    private readonly IGraphicsBuffer<uint> _outputReadback;
    private readonly IGraphicsShader _shader;
    private readonly IGraphicsComputePipeline _pipeline;
    private readonly IGraphicsBuffer<NumericValue> _numericUpload;
    private readonly IGraphicsBuffer<NumericValue> _numericStorage;
    private readonly IGraphicsBuffer<Vector3Value> _vectorUpload;
    private readonly IGraphicsBuffer<Vector3Value> _vectorStorage;
    private readonly IGraphicsShaderDataBuffer<ReferenceData> _referenceUpload;
    private readonly IGraphicsShaderDataBuffer<ReferenceData> _referenceStorage;
    private readonly NumericValue[] _numbers;
    private readonly ReferenceData[] _references;
    private readonly uint[] _initialInput;
    private readonly uint[] _actual;
    private readonly uint[] _actualOutput;
    private readonly VerificationArguments _verification;
    private readonly IGpuRef<uint>[] _inputs;
    private readonly IGpuRef<uint>[] _outputs;
    private bool _mappedForCpu;
    private uint _generation;

    private TransferFixture(int count, ShaderArtifact artifact)
    {
        Count = count;
        _numericLayout = ShaderDataLayout.Data(artifact, ShaderTarget.SpirV, nameof(NumericValue));
        _referenceLayout = ShaderDataLayout.Data(artifact, ShaderTarget.SpirV, nameof(ReferenceData));
        Require(_numericLayout.Size == 32, "Compiled NumericValue stride must be 32, not an assumed std140 stride.");
        _device = VulkanDevice.Create();
        Require(!_device.ValidationEnabled, "PR measurements must use the production path without managed validation.");
        _inputs = new IGpuRef<uint>[count];
        _outputs = new IGpuRef<uint>[count];
        _numbers = new NumericValue[count];
        _references = new ReferenceData[count];
        _initialInput = new uint[count];
        _actual = new uint[count * 32];
        _actualOutput = new uint[count];
        ulong n = (ulong)count;
        _inputUpload = _device.CreateBuffer<uint>(new() { Count = n, Memory = MemoryPreference.Upload, Usage = BufferUsage.CopySource });
        _input = _device.CreateBuffer<uint>(new() { Count = n, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead });
        _output = _device.CreateBuffer<uint>(new() { Count = n, Usage = BufferUsage.ShaderRead | BufferUsage.ShaderWrite | BufferUsage.CopySource });
        _results = _device.CreateBuffer<uint>(new() { Count = n * 32, Usage = BufferUsage.ShaderRead | BufferUsage.ShaderWrite | BufferUsage.CopySource });
        _readback = _device.CreateBuffer<uint>(new() { Count = n * 32, Memory = MemoryPreference.Readback, Usage = BufferUsage.CopyDestination });
        _outputReadback = _device.CreateBuffer<uint>(new() { Count = n, Memory = MemoryPreference.Readback, Usage = BufferUsage.CopyDestination });
        _numericUpload = _device.CreateShaderDataBuffer<NumericValue>(artifact, n, MemoryPreference.Upload);
        _numericStorage = _device.CreateShaderDataBuffer<NumericValue>(artifact, n);
        _referenceUpload = _device.CreateShaderDataBuffer<ReferenceData>(artifact, n, MemoryPreference.Upload);
        _referenceStorage = _device.CreateShaderDataBuffer<ReferenceData>(artifact, n);
        _vectorUpload = _device.CreateShaderDataBuffer<Vector3Value>(artifact, n, MemoryPreference.Upload);
        _vectorStorage = _device.CreateShaderDataBuffer<Vector3Value>(artifact, n);
        Require(ShaderDataLayout.Data(artifact, ShaderTarget.SpirV, nameof(Vector3Value)).Size == 12, "Compiled float3 element stride must be twelve bytes.");
        _table = _device.CreateArgumentTable(new() { BufferCapacity = 6 });
        IGpuRef<uint> input = _table.WriteBuffer(0, _input.SliceElements(0, n), ShaderBufferAccess.ReadOnly);
        IGpuRef<uint> output = _table.WriteBuffer(1, _output.SliceElements(0, n), ShaderBufferAccess.ReadWrite);
        IGpuRef<uint> results = _table.WriteBuffer(2, _results.SliceElements(0, n * 32), ShaderBufferAccess.ReadWrite);
        _verification = new()
        {
            Numeric = _table.WriteBuffer(3, _numericStorage.SliceElements(0, n)),
            Mixed = _table.WriteBuffer(4, _referenceStorage.SliceElements(0, n)),
            Vectors = _table.WriteBuffer(5, _vectorStorage.SliceElements(0, n)),
            Results = results,
            Count = (uint)count,
        };
        for (int i = 0; i < count; i++)
        {
            _inputs[i] = input.GetElement((ulong)i);
            _outputs[i] = output.GetElement((ulong)i);
            _references[i] = new() { Input = _inputs[i], Output = _outputs[i] };
            _initialInput[i] = (uint)(1000 + i);
        }

        _shader = _device.CreateShader(artifact);
        _pipeline = _device.CreateComputePipeline(new() { ComputeShader = _shader });
        Advance();
    }

    internal int Count { get; }
    internal int NumericStride => _numericLayout.Size;
    internal int ReferenceStride => _referenceLayout.Size;
    internal uint Generation => _generation;

    internal static async Task<TransferFixture> CreateAsync(int count)
    {
        if (count is not (1 or 64)) throw new ArgumentOutOfRangeException(nameof(count));
        var fixture = new TransferFixture(count, await Compiled.Value);
        try
        {
            await fixture.InitializeInputAsync();

            return fixture;
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    internal void Advance()
    {
        _generation = (_generation % 4096) + 1;
        UpdateNumeric();
        UpdateReferences();
    }

    private void UpdateNumeric()
    {
        for (int i = 0; i < Count; i++)
        {
            uint x = _generation + (uint)i;
            _numbers[i] = new() { Header = x, Position = new(x + 1, x + 2, x + 3), Tail = new(x + 4, x + 5, x + 6, x + 7) };
        }
    }

    private void UpdateReferences()
    {
        for (int i = 0; i < Count; i++)
        {
            uint x = _generation + (uint)i;
            int target = (i + (int)(_generation % (uint)Count)) % Count;
            _references[i].Input = _inputs[target];
            _references[i].Output = _outputs[target];
            _references[i].Factor = x + 11;
            _references[i].Position = new(x + 12, x + 13, x + 14);
            _references[i].Matrix = new(
                x + 20, x + 21, x + 22, x + 23,
                x + 24, x + 25, x + 26, x + 27,
                x + 28, x + 29, x + 30, x + 31,
                x + 32, x + 33, x + 34, x + 35);
        }
    }

    internal async Task MapForCpuAsync()
    {
        Require(!_mappedForCpu, "CPU mapping is already active.");
        await _referenceUpload.MapAsync();
        _mappedForCpu = true;
    }

    internal void UnmapForCpu()
    {
        if (!_mappedForCpu) return;
        _referenceUpload.Unmap();
        _mappedForCpu = false;
    }

    internal int MappedReferenceWrite()
    {
        Require(_mappedForCpu, "CPU route must be mapped by untimed setup.");
        _generation = (_generation % 4096) + 1;
        UpdateReferences();
        _referenceUpload.CopyFrom(_references);
        // Observe actual source work without exposing variant-specific metadata in this timed path.
        return _references.Length;
    }

    internal async Task ReferenceWriteCopyComplete()
    {
        Require(!_mappedForCpu, "GPU work must not use CPU-mapped upload storage.");
        _generation = (_generation % 4096) + 1;
        UpdateReferences();
        await _referenceUpload.MapAsync();
        _referenceUpload.CopyFrom(_references);
        _referenceUpload.Unmap();
        using IGraphicsCommandBuffer commands = _device.CreateCommandBuffer(new());
        RecordReferenceCopy(commands);
        commands.Finish();
        using IGraphicsSubmission submission = _device.Queue.Submit([commands]);
        await submission.WaitAsync();
    }

    private void RecordNumericCopy(IGraphicsCommandBuffer commands)
    {
        ulong n = (ulong)Count;
        commands.Barrier(new BufferBarrierDesc<NumericValue> { Buffer = _numericUpload.SliceElements(0, n), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.Barrier(new BufferBarrierDesc<NumericValue> { Buffer = _numericStorage.SliceElements(0, n), Before = new(PipelineStage.Copy | PipelineStage.ComputeShader, ResourceAccess.CopyWrite | ResourceAccess.ShaderRead), After = new(PipelineStage.Copy, ResourceAccess.CopyWrite) });
        commands.CopyBuffer(_numericUpload.SliceElements(0, n), _numericStorage.SliceElements(0, n));
        commands.Barrier(new BufferBarrierDesc<NumericValue> { Buffer = _numericStorage.SliceElements(0, n), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead) });
    }

    private void RecordReferenceCopy(IGraphicsCommandBuffer commands)
    {
        ulong n = (ulong)Count;
        commands.Barrier(new ShaderDataBufferBarrierDesc<ReferenceData> { Buffer = _referenceUpload.SliceElements(0, n), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.Barrier(new ShaderDataBufferBarrierDesc<ReferenceData> { Buffer = _referenceStorage.SliceElements(0, n), Before = new(PipelineStage.Copy | PipelineStage.ComputeShader, ResourceAccess.CopyWrite | ResourceAccess.ShaderRead), After = new(PipelineStage.Copy, ResourceAccess.CopyWrite) });
        commands.CopyBuffer(_referenceUpload.SliceElements(0, n), _referenceStorage.SliceElements(0, n));
        commands.Barrier(new ShaderDataBufferBarrierDesc<ReferenceData> { Buffer = _referenceStorage.SliceElements(0, n), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead) });
    }

    private async Task InitializeInputAsync()
    {
        await _inputUpload.MapAsync();
        _inputUpload.CopyFrom(_initialInput);
        _inputUpload.Unmap();
        var vectors = new Vector3Value[Count];
        for (int i = 0; i < Count; i++) vectors[i].Value = new(2000 + i, 3000 + i, 4000 + i);
        await _vectorUpload.MapAsync();
        _vectorUpload.CopyFrom(vectors);
        _vectorUpload.Unmap();
        using IGraphicsCommandBuffer commands = _device.CreateCommandBuffer(new());
        ulong n = (ulong)Count;
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = _inputUpload.SliceElements(0, n), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(_inputUpload.SliceElements(0, n), _input.SliceElements(0, n));
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = _input.SliceElements(0, n), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead) });
        commands.Barrier(new BufferBarrierDesc<Vector3Value> { Buffer = _vectorUpload.SliceElements(0, n), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(_vectorUpload.SliceElements(0, n), _vectorStorage.SliceElements(0, n));
        commands.Barrier(new BufferBarrierDesc<Vector3Value> { Buffer = _vectorStorage.SliceElements(0, n), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead) });
        commands.Finish();
        using IGraphicsSubmission submission = _device.Queue.Submit([commands]);
        await submission.WaitAsync();
    }

    internal async Task VerifyAsync(bool preserveReferenceUpload = false, bool referenceAlreadyCopied = false)
    {
        Require(!_mappedForCpu, "Verification must finish before persistent mapping.");
        if (preserveReferenceUpload) UpdateNumeric();
        else Advance();
        await _numericUpload.MapAsync();
        _numericUpload.CopyFrom(_numbers);
        _numericUpload.Unmap();
        if (!preserveReferenceUpload)
        {
            await _referenceUpload.MapAsync();
            _referenceUpload.CopyFrom(_references);
            _referenceUpload.Unmap();
        }
        VerifyReferenceMetadata(_referenceUpload);
        using IGraphicsCommandBuffer commands = _device.CreateCommandBuffer(new());
        RecordNumericCopy(commands);
        if (!referenceAlreadyCopied) RecordReferenceCopy(commands);
        // Both registrations are ReadWrite; include both access bits and allocation usages.
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = _output.SliceElements(0, (ulong)Count), Before = new(PipelineStage.Copy | PipelineStage.ComputeShader, ResourceAccess.CopyRead | ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite) });
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = _results.SliceElements(0, (ulong)Count * 32), Before = new(PipelineStage.Copy | PipelineStage.ComputeShader, ResourceAccess.CopyRead | ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite) });
        IComputeEncoder compute = commands.BeginComputePass(new());
        compute.SetPipeline(_pipeline);
        compute.SetArgumentTable(_table);
        compute.SetArguments(in _verification);
        compute.Dispatch((uint)Count);
        compute.End();
        ulong n = (ulong)Count;
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = _results.SliceElements(0, n * 32), Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(_results.SliceElements(0, n * 32), _readback.SliceElements(0, n * 32));
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = _readback.SliceElements(0, n * 32), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = _output.SliceElements(0, n), Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(_output.SliceElements(0, n), _outputReadback.SliceElements(0, n));
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = _outputReadback.SliceElements(0, n), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = _device.Queue.Submit([commands]);
        await submission.WaitAsync();
        VerifyReferenceMetadata(_referenceStorage);
        await _readback.MapAsync();
        _readback.CopyTo(_actual);
        _readback.Unmap();
        await _outputReadback.MapAsync();
        _outputReadback.CopyTo(_actualOutput);
        _outputReadback.Unmap();
        for (int i = 0; i < Count; i++)
        {
            uint x = _generation + (uint)i;
            int b = i * 32;
            for (int j = 0; j < 8; j++) Require(_actual[b + j] == x + (uint)j, "Numeric shader field/array stride mismatch.");
            for (int j = 0; j < 16; j++) Require(_actual[b + 8 + j] == x + 20 + (uint)j, "Row-major matrix field mismatch.");
            Require(_actual[b + 24] == x + 11, "Mixed scalar mismatch.");
            for (int j = 0; j < 3; j++) Require(_actual[b + 25 + j] == x + 12 + (uint)j, "Mixed Vector3 mismatch.");
            int target = (i + (int)(_generation % (uint)Count)) % Count;
            Require(_actual[b + 28] == _initialInput[target], "Read-only reference range mismatch.");
            Require(_actual[b + 29] == (uint)(2000 + i) && _actual[b + 30] == (uint)(3000 + i) && _actual[b + 31] == (uint)(4000 + i), "Twelve-byte Vector3 stride mismatch.");
            Require(_actualOutput[target] == _initialInput[target] + x + 11, "Writable reference range mismatch.");
        }
    }

    private void VerifyReferenceMetadata(IGraphicsShaderDataBuffer<ReferenceData> buffer)
    {
        var source = (IShaderDataSource)buffer;
        for (int i = 0; i < Count; i++)
        {
            var metadata = source.ReadReferences((ulong)i);
            // Compatibility reflection is verification-only; neither timed entry point calls it.
            // Old common metadata owns wire fixups; new backend-private metadata owns IDs only.
            object boxed = metadata;
            var type = boxed.GetType();
            var identitiesProperty = type.GetProperty("References");
            object[] identities;
            if (identitiesProperty != null)
            {
                identities = ((System.Collections.IEnumerable)identitiesProperty.GetValue(boxed)!)
                    .Cast<object>().ToArray();
            }
            else
            {
                var values = ((System.Collections.IEnumerable)(type.GetProperty("Values")
                    ?? throw new InvalidOperationException("Unknown metadata contract."))
                    .GetValue(boxed)!).Cast<ShaderValue>().ToArray();
                Require(values.Select(v => v.Offset).Order().SequenceEqual(new[] { 80, 96 }),
                    "Legacy reference wire slots differ from the generated transfer layout.");
                Require(values.All(v => v.ValueType == typeof(uint)), "Unexpected legacy reference kind.");
                identities = values.Select(v => v.Reference!).ToArray();
            }
            Require(identities.Length == 2, "Exactly two reference identities must be retained.");
            Require(identities.All(v => v is IGpuRef<uint> && v is IShaderReference),
                "Unexpected reference metadata kind.");
            Require(ReferenceEquals(identities[0], _references[i].Input), "Input reference metadata mismatch.");
            Require(ReferenceEquals(identities[1], _references[i].Output), "Output reference metadata mismatch.");
            Require(((IShaderReference)identities[0]).BufferAccess == ShaderBufferAccess.ReadOnly,
                "Input access identity mismatch.");
            Require(((IShaderReference)identities[1]).BufferAccess == ShaderBufferAccess.ReadWrite,
                "Output access identity mismatch.");
        }
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public void Dispose()
    {
        UnmapForCpu();
        _pipeline.Dispose();
        _shader.Dispose();
        _table.Dispose();
        _vectorStorage.Dispose();
        _vectorUpload.Dispose();
        _referenceStorage.Dispose();
        _referenceUpload.Dispose();
        _numericStorage.Dispose();
        _numericUpload.Dispose();
        _outputReadback.Dispose();
        _readback.Dispose();
        _results.Dispose();
        _output.Dispose();
        _input.Dispose();
        _inputUpload.Dispose();
        _device.Dispose();
    }

    private const string ShaderSource = """
        #include "lumyte.slang"
        struct NumericValue { uint Header; float3 Position; float4 Tail; };
        struct Vector3Value { float3 Value; };
        struct ReferenceData
        {
            float4x4 Matrix;
            uint Factor;
            float3 Position;
            GpuBufferRef<uint> Input;
            GpuRWBufferRef<uint> Output;
        };
        struct VerificationArguments
        {
            GpuBufferRef<NumericValue> Numeric;
            GpuBufferRef<Vector3Value> Vectors;
            GpuBufferRef<ReferenceData> Mixed;
            GpuRWBufferRef<uint> Results;
            uint Count;
        };
        ConstantBuffer<VerificationArguments> arguments;
        [shader("compute")]
        [numthreads(1, 1, 1)]
        void main(uint3 tid : SV_DispatchThreadID)
        {
            uint i = tid.x;
            if (i >= arguments.Count) return;
            NumericValue n = arguments.Numeric.Load(i);
            ReferenceData m = arguments.Mixed.Load(i);
            uint b = i * 32;
            arguments.Results.Store(b, n.Header);
            arguments.Results.Store(b + 1, uint(n.Position.x));
            arguments.Results.Store(b + 2, uint(n.Position.y));
            arguments.Results.Store(b + 3, uint(n.Position.z));
            arguments.Results.Store(b + 4, uint(n.Tail.x));
            arguments.Results.Store(b + 5, uint(n.Tail.y));
            arguments.Results.Store(b + 6, uint(n.Tail.z));
            arguments.Results.Store(b + 7, uint(n.Tail.w));
            for (uint r = 0; r < 4; r++)
                for (uint c = 0; c < 4; c++)
                    arguments.Results.Store(b + 8 + r * 4 + c, uint(m.Matrix[r][c]));
            arguments.Results.Store(b + 24, m.Factor);
            arguments.Results.Store(b + 25, uint(m.Position.x));
            arguments.Results.Store(b + 26, uint(m.Position.y));
            arguments.Results.Store(b + 27, uint(m.Position.z));
            Vector3Value vector = arguments.Vectors.Load(i);
            arguments.Results.Store(b + 29, uint(vector.Value.x));
            arguments.Results.Store(b + 30, uint(vector.Value.y));
            arguments.Results.Store(b + 31, uint(vector.Value.z));
            uint value = m.Input.Load(0);
            arguments.Results.Store(b + 28, value);
            m.Output.Store(0, value + m.Factor);
        }
        """;
}
