using BenchmarkDotNet.Attributes;

namespace Lumyte.Worlds.Benchmark;

/// <summary>Measures CPU scene ownership, boundary commits and independent frame dispatch phases.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Core", "Worlds")]
public class WorldsBenchmarks
{
    private readonly CpuDrawContext _draw = new();
    private WorldManager _manager = null!;
    private World _inactive = null!;
    private World _alternate = null!;
    private CountingScene _editable = null!;
    private CountingScene[] _scenes = [];
    private bool _replace;

    /// <summary>Gets or sets the number of committed layers.</summary>
    [Params(1, 4, 64)]
    public int Layers { get; set; }

    /// <summary>Gets or sets the top layer blocking policy or input consumption branch.</summary>
    [Params("none", "update", "draw", "input", "consume")]
    public string Policy { get; set; } = "none";

    /// <summary>Builds a fixed set of counting scenes outside frame dispatch measurements.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _manager = Create(Layers, 8);
        _scenes = _manager.Worlds.SelectMany(world => world.Scenes).Cast<CountingScene>().ToArray();
        WorldLayerPolicy policy = new(Policy == "update", Policy == "draw", Policy == "input");
        for (int i = 0; i < Layers; i++)
        {
            _manager.Stack.Push(_manager.Worlds[i], i == Layers - 1 ? policy : default);
        }

        _scenes[^1].Consume = Policy == "consume";
        _manager.BeginFrame();
        _inactive = _manager.CreateWorld();
        _editable = new CountingScene();
        _alternate = _manager.CreateWorld();
    }

    /// <summary>Constructs owned worlds and scenes, without activating them.</summary>
    /// <returns>The observed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-WORLD-01")]
    public WorldManager ConstructOwnership() => Create(Layers, 8);

    /// <summary>Adds and removes the same detached scene, leaving bounded inactive ownership.</summary>
    /// <returns>The observed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-WORLD-01")]
    public bool EditInactive()
    {
        _inactive.AddScene(_editable);
        return _inactive.RemoveScene(_editable);
    }

    /// <summary>Queues a cancelling push/pop pair and commits its final snapshot.</summary>
    /// <returns>The observed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-WORLD-02")]
    public int CancelledBatch()
    {
        _manager.Stack.Push(_alternate);
        _manager.Stack.Pop();
        _manager.BeginFrame();
        return _manager.Stack.Worlds.Count;
    }

    /// <summary>Applies a real replacement, alternating between two legal world identities.</summary>
    /// <returns>The observed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-WORLD-02")]
    public int ReplaceBoundary()
    {
        _replace = !_replace;
        _manager.Stack.Replace(_replace ? _alternate : _manager.Worlds[Layers - 1]);
        _manager.BeginFrame();
        return _manager.Stack.Worlds.Count;
    }

    /// <summary>Computes snapshots and execution states even with no queued requests.</summary>
    /// <returns>The observed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-WORLD-02")]
    public int UnchangedBoundary()
    {
        _manager.BeginFrame();
        return _manager.Stack.Worlds.Count;
    }

    /// <summary>Dispatches input top-down until consumed or blocked.</summary>
    /// <returns>The observed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-WORLD-03")]
    public int HandleInput()
    {
        _manager.Stack.HandleInput();
        return _scenes[^1].Input;
    }

    /// <summary>Dispatches exactly one positive fixed simulation step.</summary>
    /// <returns>The observed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-WORLD-04")]
    public int FixedUpdate()
    {
        _manager.Stack.FixedUpdate(TimeSpan.FromTicks(166667));
        return _scenes[^1].Fixed;
    }

    /// <summary>Dispatches every ordinary update before any late update.</summary>
    /// <returns>The observed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-WORLD-04")]
    public int Update()
    {
        _manager.Stack.Update(TimeSpan.Zero);
        return _scenes[^1].Late;
    }

    /// <summary>Dispatches a shared CPU-only context, without any GPU work.</summary>
    /// <returns>The observed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-WORLD-05")]
    public int Draw()
    {
        _manager.Stack.Draw(_draw);
        return _scenes[^1].Drawn;
    }

    internal static WorldManager Create(int layers, int scenes)
    {
        var manager = new WorldManager();
        for (int i = 0; i < layers; i++)
        {
            World world = manager.CreateWorld();
            for (int j = 0; j < scenes; j++)
            {
                world.AddScene(new CountingScene());
            }
        }

        return manager;
    }
}
