using System.Numerics;

using BenchmarkDotNet.Attributes;
using Lumyte.Interaction;

using static Lumyte.Interaction.InteractionKit;

namespace Lumyte.Benchmarks;

[MemoryDiagnoser]
public class ActionRuntimeBenchmarks
{
    private readonly InputAction<Vector2> move = new("game.move");
    private BenchmarkMouse[] mice = null!;
    private ActionRuntime runtime = null!;
    private bool direction;

    [Params(1, 8, 32)]
    public int SourceCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        ActionMap map = ActionMap("Gameplay")[
            new ActionBinding<Vector2>(move, InputControls.MouseDelta)
        ];
        mice = [.. Enumerable.Range(0, SourceCount).Select(_ => new BenchmarkMouse())];
        runtime = new(
            new InteractionContext(),
            [map],
            mice: mice);
        for (int index = 1; index < mice.Length; index++)
        {
            mice[index].Move(new(index + 1, 0));
        }
    }

    [Benchmark]
    public Vector2 AggregateChangedSource()
    {
        direction = !direction;
        mice[0].Move(direction ? Vector2.UnitX : -Vector2.UnitX);
        return runtime.GetValue(move);
    }

    [GlobalCleanup]
    public void Cleanup() => runtime.Dispose();
}
