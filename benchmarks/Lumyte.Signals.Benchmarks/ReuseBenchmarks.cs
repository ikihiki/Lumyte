using BenchmarkDotNet.Attributes;

namespace Lumyte.Signals.Benchmarks;

/// <summary>Separates repeated dependency map reuse from short-lived observer creation.</summary>
public class ReuseBenchmarks
{
    private Signal<int>[] _inputs = [];
    private SignalEffect _effect = null!;
    private Action _observe = null!;
    private int _next;
    private int _sum;
    private int _notifications;

    /// <summary>Gets or sets the number of distinct dependencies read by each observer.</summary>
    [Params(1, 16, 128)]
    public int Dependencies { get; set; }

    /// <summary>Checks values and verifies that temporary observers are detached.</summary>
    public static void Verify()
    {
        foreach (int dependencies in new[] { 1, 16, 128 })
        {
            var benchmark = new ReuseBenchmarks { Dependencies = dependencies };
            benchmark.Setup();
            try
            {
                int initial = dependencies * (dependencies - 1) / 2;
                if (benchmark._notifications != 1 || benchmark._sum != initial)
                {
                    throw new InvalidOperationException("Observer setup must read every input once.");
                }

                if (benchmark.ReevaluateStable() != initial + 1 || benchmark._notifications != 2)
                {
                    throw new InvalidOperationException("One update must rerun the stable observer once.");
                }

                if (benchmark.CreateAndDisposeEffect() != initial + 1 || benchmark._notifications != 3)
                {
                    throw new InvalidOperationException("A temporary observer must execute once initially.");
                }

                if (benchmark.ReevaluateStable() != initial + 2 || benchmark._notifications != 4)
                {
                    throw new InvalidOperationException("Disposed temporary observers must not run again.");
                }
            }
            finally
            {
                benchmark.Cleanup();
            }
        }
    }

    /// <summary>Allocates the graph and callback outside the measured operations.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _inputs = new Signal<int>[Dependencies];
        for (int index = 0; index < _inputs.Length; index++)
        {
            _inputs[index] = Signal.Create(index);
        }

        _observe = ReadAll;
        _effect = Signal.Effect(_observe);
    }

    /// <summary>Mutates one input and reevaluates an existing observer with stable dependencies.</summary>
    /// <returns>The latest observed sum.</returns>
    [Benchmark]
    public int ReevaluateStable()
    {
        _inputs[0].Value = ++_next;
        return _sum;
    }

    /// <summary>Creates and disposes one observer while reusing the same callback and inputs.</summary>
    /// <returns>The sum read by the temporary observer.</returns>
    [Benchmark]
    public int CreateAndDisposeEffect()
    {
        using SignalEffect temporary = Signal.Effect(_observe);
        return _sum;
    }

    /// <summary>Releases the stable observer.</summary>
    [GlobalCleanup]
    public void Cleanup() => _effect.Dispose();

    private void ReadAll()
    {
        int sum = 0;
        foreach (Signal<int> input in _inputs)
        {
            sum += input.Value;
        }

        _sum = sum;
        _notifications++;
    }
}
