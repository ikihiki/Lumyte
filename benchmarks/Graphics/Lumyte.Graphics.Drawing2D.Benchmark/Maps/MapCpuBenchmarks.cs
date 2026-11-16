using BenchmarkDotNet.Attributes;
using Lumyte.Benchmarks.Maps;
using Lumyte.Graphics.Drawing2D.Benchmark.Renderer;
using Lumyte.Maps;
using Lumyte.Maps.Mvt;
using Lumyte.Resources;

namespace Lumyte.Graphics.Drawing2D.Benchmark.Maps;

/// <summary>Real map CPU phases; the managed recorder never executes GPU work.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class MapCpuBenchmarks
{
    private byte[] _bytes = null!;
    private MapWorkload _map = null!;
    private DrawingCpuHarness _cpu = null!;
    private DrawList2D _drawing = null!;
    private int _frame;
    private string _hash = null!;

    /// <summary>Gets the generated MVT SHA256 for source-level reproducibility checks.</summary>
    public string FixtureHash => _hash;

    /// <summary>Gets the current camera for deterministic path checks.</summary>
    public MapCamera Camera => _map.Session.Camera;

    /// <summary>Gets actual managed-recorder operation observations, not GPU execution counters.</summary>
    public Lumyte.Graphics.Benchmarks.Testing.CpuGraphicsCounters Counters => _cpu.Device.Counters;

    /// <summary>Preloads the fixed map and warms the managed recorder outside measurement.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _bytes = DenseMapFixture.CreateBytes();
        _hash = DenseMapFixture.Hash;
        _map = new();
        await _map.WarmAsync();
        _drawing = _map.Record();
        _cpu = new();
        _cpu.WarmPrepareRecordDispose(_drawing);
    }

    /// <summary>Decodes the generated MVT bytes; excludes fixture generation and hashing.</summary>
    /// <returns>The deterministic fixture value.</returns>
    [Benchmark]
    public MapTileContent MvtDecoderDecode() => MvtDecoder.Decode(_bytes, DenseMapFixture.Profile, _hash);

    /// <summary>Acquires and opens a real memory-source snapshot; no decode or network.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Benchmark]
    public async Task<long> SourceAcquireAsync()
    {
        await using ResourceSourceSnapshot snapshot = await _map.Source.OpenSnapshotAsync(_map.Registration.GetKey(DenseMapFixture.Tile).Address.Id);
        await using Stream stream = await snapshot.OpenReadAsync();
        return stream.Length;
    }

    /// <summary>Records the already published static map session.</summary>
    /// <returns>The deterministic fixture value.</returns>
    [Benchmark]
    public DrawList2D StaticSessionRecord() => _map.Record();

    /// <summary>Applies the deterministic M3 camera sample and records its geometry.</summary>
    /// <returns>The deterministic fixture value.</returns>
    [Benchmark]
    public DrawList2D FixedPanContinuousZoomRecord()
    {
        _map.Move(_frame++);
        return _map.Record();
    }

    /// <summary>Prepares, records commands and disposes using the CPU mock; no GPU execution.</summary>
    /// <returns>The deterministic fixture value.</returns>
    [Benchmark]
    public ulong WarmCpuPrepareCommandRecordDispose() => _cpu.WarmPrepareRecordDispose(_drawing);

    /// <summary>Disposes the managed recorder and owned resource pipeline outside measurement.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        _cpu.Dispose();
        await _map.DisposeAsync();
    }
}
