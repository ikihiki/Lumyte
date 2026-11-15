using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Benchmarks.Testing;

namespace Lumyte.Graphics.Validation.Benchmark.Transfers;

/// <summary>Measures explicit validation decoration over the same CPU recording device and operations.</summary>
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class TransferBenchmarks
{
    private CpuGraphicsDevice _raw = null!;
    private ValidationDevice? _validation;
    private IGraphicDevice _device = null!;
    private IGraphicsBuffer<int> _upload = null!;
    private IGraphicsBuffer<int> _source = null!;
    private IGraphicsBuffer<int> _destination = null!;
    private int[] _values = [];

    /// <summary>Gets or sets a value indicating whether the production validation wrapper is applied.</summary>
    [Params(false, true)]
    public bool Validated { get; set; }

    /// <summary>Gets or sets the consumed transfer size in integer elements.</summary>
    [Params(16, 1024)]
    public int ElementCount { get; set; }

    /// <summary>Gets the exact recording counters after the operation.</summary>
    public CpuGraphicsCounters Counters => _raw.Counters;

    /// <summary>Creates identical live resources outside measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _raw = new();
        _validation = Validated ? new ValidationDevice(_raw) : null;
        _device = _validation is null ? _raw : _validation;
        _upload = _device.CreateBuffer<int>(new() { Count = (ulong)ElementCount, Memory = MemoryPreference.Upload, Usage = BufferUsage.CopySource });
        _source = _device.CreateBuffer<int>(new() { Count = (ulong)ElementCount, Usage = BufferUsage.CopySource });
        _destination = _device.CreateBuffer<int>(new() { Count = (ulong)ElementCount, Usage = BufferUsage.CopyDestination });
        _values = Enumerable.Range(1, ElementCount).ToArray();
    }

    /// <summary>Maps, writes real managed upload storage and unmaps; no GPU copy executes.</summary>
    /// <returns>The observation checksum after consuming every copied byte in storage.</returns>
    [Benchmark]
    public ulong MapCopyUnmap()
    {
        _raw.ResetCounters();
        _upload.MapAsync().GetAwaiter().GetResult();
        _upload.CopyFrom(_values);
        _upload.Unmap();
        return _raw.Counters.Checksum;
    }

    /// <summary>Records a typed buffer copy and observes immediate synthetic completion, with optional validation.</summary>
    /// <returns>The completed synthetic submission count.</returns>
    [Benchmark]
    public long CopyRecordFinishRetire()
    {
        _raw.ResetCounters();
        using IGraphicsCommandBuffer commands = _device.CreateCommandBuffer(new());
        commands.CopyBuffer(_source.SliceElements(0, (ulong)ElementCount), _destination.SliceElements(0, (ulong)ElementCount));
        commands.Barrier(new MemoryBarrierDesc
        {
            Before = new(PipelineStage.Copy, ResourceAccess.CopyRead | ResourceAccess.CopyWrite),
            After = new(PipelineStage.Copy, ResourceAccess.CopyRead | ResourceAccess.CopyWrite),
        });
        commands.Finish();
        using IGraphicsSubmission submission = _device.Queue.Submit([commands]);
        submission.WaitAsync().GetAwaiter().GetResult();
        return _raw.Counters.Submissions;
    }

    /// <summary>Releases all children before wrapper and borrowed raw device.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _destination.Dispose();
        _source.Dispose();
        _upload.Dispose();
        _validation?.Dispose();
        _raw.Dispose();
    }
}
