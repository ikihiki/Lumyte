using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Incremental.Benchmarks;

/// <summary>Real CPU update/layout/record; GPU and presentation are excluded.</summary>
public class RenderingWorkloadCpuBenchmarks
{
    private readonly IReadOnlyList<DrawList2D>?[] _history = new IReadOnlyList<DrawList2D>?[3];
    private IRenderingWorkload _workload = null!;
    private int _frame;

    /// <summary>Gets or sets the workload identifier.</summary>
    [ParamsSource(nameof(Cases))]
    public string CaseName { get; set; } = null!;

    /// <summary>Gets or sets the requested workload scale.</summary>
    [ParamsSource(nameof(Counts))]
    public int Count { get; set; }

    /// <summary>Gets cases selected by the parent command line.</summary>
    public IEnumerable<string> Cases => RenderingWorkloadCatalog.Select(Environment.GetEnvironmentVariable("LUMYTE_WORKLOAD_SUITE") ?? "all", Environment.GetEnvironmentVariable("LUMYTE_WORKLOAD_CASE"));

    /// <summary>Gets the requested scale.</summary>
    public IEnumerable<int> Counts => [int.Parse(Environment.GetEnvironmentVariable("LUMYTE_WORKLOAD_COUNT") ?? "200", System.Globalization.CultureInfo.InvariantCulture)];

    /// <summary>Creates actual UI/text/render fixtures outside the measured method.</summary>
    [GlobalSetup]
    public void Setup() => _workload = RenderingWorkloadCatalog.Create(CaseName, Count);

    /// <summary>Updates, lays out and records while retaining three CPU snapshots.</summary>
    /// <returns>The immutable ordered recordings.</returns>
    [Benchmark]
    public object CpuFrame()
    {
        _workload.Update();
        _workload.Layout();
        IReadOnlyList<DrawList2D> result = _workload.Record();
        _history[_frame++ % 3] = result;
        if (_frame == int.MaxValue)
        {
            _frame = 0;
        }

        return result;
    }

    /// <summary>Checks fixture invariants and releases actual native/managed resources.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _workload.Verify();
        _workload.Dispose();
    }
}
