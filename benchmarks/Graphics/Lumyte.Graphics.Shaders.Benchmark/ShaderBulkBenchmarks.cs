using System.Numerics;
using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Benchmarks.Testing;

namespace Lumyte.Graphics.Shaders.Benchmark;

/// <summary>Generated vector/matrix wire bytes and real same-device reference relocation.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class ShaderBulkBenchmarks
{
    private CpuGraphicsDevice _device = null!;
    private IGraphicsBuffer<uint> _buffer = null!;
    private IArgumentTable _table = null!;
    private NumericArguments[] _values = null!;
    private IGraphicsShaderDataBuffer<NumericArguments> _staging = null!;
    private NumericArguments _numeric;
    private ReferencedArguments _referenced;

    /// <summary>Gets or sets the number of roots recorded into one pass.</summary>
    [Params(1, 128)]
    public int Roots { get; set; }

    /// <summary>Creates device-owned references outside recording.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _device = new CpuGraphicsDevice();
        _buffer = _device.CreateBuffer<uint>(new BufferDesc<uint> { Count = 64, Usage = BufferUsage.ShaderRead });
        _table = _device.CreateArgumentTable(new ArgumentTableDesc { BufferCapacity = 2 });
        _numeric = new NumericArguments { Position = new Vector3(2, 3, 5), Transform = Matrix4x4.CreateTranslation(7, 11, 13), Tag = 17 };
        _values = Enumerable.Repeat(_numeric, Roots).ToArray();

        // Header-only opaque artifact satisfies transport validation, never shader execution.
        byte[] opaqueCode = new byte[20];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(opaqueCode, 0x07230203);
        var artifact = ShaderArtifact.PackTarget(ShaderTarget.SpirV, ShaderStage.Compute, "main", "opaque CPU fixture; not executable GPU code", opaqueCode, "{}");
        _staging = _device.CreateShaderDataBuffer<NumericArguments>(artifact, (ulong)Roots, MemoryPreference.Upload);
        _referenced = new ReferencedArguments { Numeric = _numeric, First = _table.WriteBuffer(0, new BufferSlice<uint>(_buffer, 0, 32)), Second = _table.WriteBuffer(1, new BufferSlice<uint>(_buffer, 32, 32)) };
    }

    /// <summary>Captures independent generated unmanaged vector/matrix bytes.</summary>
    /// <returns>The immutable snapshot.</returns>
    [Benchmark]
    public ShaderValueSnapshot CaptureNumeric() => IShaderArguments.Capture(in _numeric);

    /// <summary>Captures ordered opaque slots separately from raw numeric ranges.</summary>
    /// <returns>The immutable snapshot with borrowed resource identities.</returns>
    [Benchmark]
    public ShaderValueSnapshot CaptureReferences() => IShaderArguments.Capture(in _referenced);

    /// <summary>Runs actual encoder SetArguments and table selection without native GPU initialization.</summary>
    /// <returns>The device checksum after reference relocation.</returns>
    [Benchmark]
    public ulong RecordReferenceBindings()
    {
        using IGraphicsCommandBuffer commands = _device.CreateCommandBuffer(new CommandBufferDesc());
        IComputeEncoder encoder = commands.BeginComputePass(new ComputePassDesc());
        encoder.SetArgumentTable(_table);
        for (int index = 0; index < Roots; index++)
        {
            encoder.SetArguments(in _referenced);
        }

        encoder.End();
        commands.Finish();
        return _device.Counters.Checksum;
    }

    /// <summary>Maps and copies generated typed wire values using static TransferSize stride.</summary>
    /// <returns>The observed upload byte count.</returns>
    [Benchmark]
    public async Task<ulong> CopyTypedBulkAsync()
    {
        await _staging.MapAsync();
        try
        {
            _staging.CopyFrom(_values);
        }
        finally
        {
            _staging.Unmap();
        }

        return _staging.SizeInBytes;
    }

    /// <summary>Releases all borrowed resources after recording.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _staging.Dispose();
        _table.Dispose();
        _buffer.Dispose();
        _device.Dispose();
    }
}
