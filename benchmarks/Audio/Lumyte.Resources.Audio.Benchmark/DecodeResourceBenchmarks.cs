using BenchmarkDotNet.Attributes;
using Lumyte.Execution;
using Lumyte.Memory;
using Lumyte.Resources.Execution;

namespace Lumyte.Resources.Audio.Benchmark;

/// <summary>Real manager and scheduled decode; no audio owner or physical output.</summary>
[BenchmarkCategory("Audio", "CPU", "Resources", "DecodeStage", "Screening", "Comparison", "Diagnostic")]
public class DecodeResourceBenchmarks
{
    private readonly ResourceKey<AudioPcmData> _warmKey = new("wave/warm");
    private readonly ResourceKey<AudioPcmData> _coldKey = new("wave/cold");
    private MemoryManager _memory = null!;
    private ExecutionRuntime _execution = null!;
    private ResourceManager _manager = null!;
    private ResourceReference<AudioPcmData> _resident = null!;

    /// <summary>Gets or sets a value indicating whether to use nonseekable seven-byte reads with unknown length.</summary>
    [Params(false, true)]
    public bool PartialReads { get; set; }

    /// <summary>Gets or sets finite stereo PCM16 frames.</summary>
    [Params(257, 16384)]
    public int Frames { get; set; }

    /// <summary>Gets the observable source for unmeasured correctness checks.</summary>
    public WaveSource Source { get; private set; } = null!;

    /// <summary>Starts one actual CPU runtime and retains the warm generation.</summary>
    [GlobalSetup]
    public void Setup()
    {
        Source = new WaveSource(SyntheticWave.Create("16", 2, Frames), PartialReads);
        var runtime = new ResourceRuntime();
        _execution = new ExecutionRuntime([runtime]);
        _execution.PrepareOnCurrentHost(OwnerPlacement.Dedicated, CpuExecutionMode.ThreadPool, 1, new ExecutionOptions());
        _memory = new MemoryManager();
        _manager = new ResourceManager("decode-benchmark", Source, [new AudioDecodeStage()], _memory, runtime);
        _execution.StartAsync().AsTask().GetAwaiter().GetResult();
        _resident = _manager.RetainAsync(_warmKey).AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Releases resources before stopping their executor.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _resident.Dispose();
        _manager.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _execution.StopAsync().AsTask().GetAwaiter().GetResult();
        _memory.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Includes snapshot/stream allocation, copying, decode, scheduling, leases and trim.</summary>
    /// <returns>Decoded checksum.</returns>
    [Benchmark]
    public async Task<double> ColdRetainDecodeAndTrimAsync()
    {
        double checksum;
        using (ResourceReference<AudioPcmData> reference = await _manager.RetainAsync(_coldKey))
        using (ResourceLease<AudioPcmData> lease = reference.Pin())
        {
            checksum = SyntheticWave.Checksum(lease.Value.Samples.Span);
        }

        await _manager.TrimAsync();
        return checksum;
    }

    /// <summary>Retains, pins and returns warm ownership without I/O or sample scanning.</summary>
    /// <returns>Sample count.</returns>
    [Benchmark]
    public async Task<int> WarmRetainAndPinAsync()
    {
        using ResourceReference<AudioPcmData> reference = await _manager.RetainAsync(_warmKey);
        using ResourceLease<AudioPcmData> lease = reference.Pin();
        return lease.Value.Samples.Length;
    }

    /// <summary>Pins an existing reference without reference reacquisition.</summary>
    /// <returns>Sample count.</returns>
    [Benchmark]
    public int ResidentPin()
    {
        using ResourceLease<AudioPcmData> lease = _resident.Pin();
        return lease.Value.Samples.Length;
    }
}
