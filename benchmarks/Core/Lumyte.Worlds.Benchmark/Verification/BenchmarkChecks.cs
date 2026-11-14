namespace Lumyte.Worlds.Benchmark;

/// <summary>Deterministic CPU-only world fixture oracles.</summary>
public static class BenchmarkChecks
{
    /// <summary>Verifies bounded edits, alternating boundaries, phase independence and ownership release.</summary>
    public static void Verify()
    {
        foreach (int layers in new[] { 1, 4, 64 })
        {
            foreach (string policy in new[] { "none", "update", "draw", "input", "consume" })
            {
                var benchmark = new WorldsBenchmarks { Layers = layers, Policy = policy };
                benchmark.Setup();
                Require(benchmark.ConstructOwnership().Worlds.Count == layers);
                Require(benchmark.EditInactive());
                Require(benchmark.CancelledBatch() == layers);
                Require(benchmark.UnchangedBoundary() == layers);
                Require(benchmark.FixedUpdate() == 1);
                Require(benchmark.Update() == 1);
                Require(benchmark.Draw() == 1);
                benchmark.HandleInput();
                Require(benchmark.ReplaceBoundary() == layers);
                Require(benchmark.ReplaceBoundary() == layers);
            }
        }

        foreach (int count in new[] { 1, 4, 32 })
        {
            var benchmark = new WorldLifetimeBenchmarks { Count = count };
            benchmark.Setup();
            Require(benchmark.DestroyBoundary() == 0);
        }

        WorldManager manager = WorldsBenchmarks.Create(2, 1);
        World lower = manager.Worlds[0];
        World upper = manager.Worlds[1];
        var scene = (CountingScene)lower.Scenes[0];
        manager.Stack.Push(lower);
        manager.Stack.Push(upper, new WorldLayerPolicy(true, false, true));
        Require(manager.Stack.Worlds.Count == 0);
        manager.BeginFrame();
        manager.Stack.FixedUpdate(TimeSpan.FromTicks(1));
        manager.Stack.Update(TimeSpan.Zero);
        manager.Stack.Draw(new CpuDrawContext());
        Require(scene.Fixed == 0 && scene.Updated == 0 && scene.Late == 0 && scene.Drawn == 1);
        manager.Stack.Pop();
        manager.BeginFrame();
        manager.Stack.Update(TimeSpan.Zero);
        Require(scene.Updated == 1 && scene.Late == 1);
        manager.RequestDestroy(lower);
        manager.RequestDestroy(lower);
        manager.BeginFrame();
        Require(lower.IsDestroyed && lower.Scenes.Count == 0 && scene.World is null);
        Require(manager.Worlds.Count == 1 && manager.Stack.Worlds.Count == 0);
    }

    private static void Require(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("World benchmark invariant failed.");
        }
    }
}
