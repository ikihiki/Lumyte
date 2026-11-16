using BenchmarkDotNet.Attributes;

namespace Lumyte.UI.Benchmark;

/// <summary>Public layout operations including real invalidating mutations.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class LayoutBenchmarks
{
    private UiTreeWorkload _workload = null!;

    /// <summary>Gets or sets actual wrapper depth.</summary>
    [Params(0, 24)]
    public int Depth { get; set; }

    /// <summary>Mounts and primes layout.</summary>
    [GlobalSetup]
    public void Setup() => _workload = new(32, Depth);

    /// <summary>Mounts, initially lays out and disposes a fresh runtime tree.</summary>
    /// <returns>The root width.</returns>
    [Benchmark]
    public float InitialLayout()
    {
        using var surface = new UiSurface(_workload.Declare());
        surface.SetViewport(new(512, 4096));
        surface.UpdateLayout();
        return surface.RootElement.Size.X;
    }

    /// <summary>Mutates one leaf then updates tree and layout.</summary>
    [Benchmark]
    public void DirtyLeaf() => _workload.LayoutLeaf();

    /// <summary>Mutates viewport then updates layout.</summary>
    [Benchmark]
    public void Viewport() => _workload.LayoutViewport();

    /// <summary>Releases runtime state.</summary>
    [GlobalCleanup]
    public void Cleanup() => _workload.Dispose();
}
