using BenchmarkDotNet.Attributes;

namespace Lumyte.Worlds.Benchmark.Management;

/// <summary>Separates finite destruction batch application from fixture construction.</summary>
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class WorldLifetimeBenchmarks
{
    private WorldManager _manager = null!;

    /// <summary>Gets or sets the number of worlds to destroy.</summary>
    [Params(1, 4, 32)]
    public int Count { get; set; }

    /// <summary>Prepares fresh active worlds and duplicate idempotent destruction requests.</summary>
    [IterationSetup]
    public void Setup()
    {
        _manager = WorldsBenchmarks.Create(Count, 8);
        foreach (World world in _manager.Worlds)
        {
            _manager.Stack.Push(world);
        }

        _manager.BeginFrame();
        foreach (World world in _manager.Worlds)
        {
            _manager.RequestDestroy(world);
            _manager.RequestDestroy(world);
        }
    }

    /// <summary>Commits the pending destruction batch and releases scene ownership.</summary>
    /// <returns>The observed operation result.</returns>
    [Benchmark]
    public int DestroyBoundary()
    {
        _manager.BeginFrame();
        return _manager.Worlds.Count;
    }
}
