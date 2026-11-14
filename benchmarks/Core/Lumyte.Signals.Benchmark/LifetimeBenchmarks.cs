using BenchmarkDotNet.Attributes;

namespace Lumyte.Signals.Benchmark;

/// <summary>Exercises scope ownership, recovery and actual parallel graph access.</summary>
public class LifetimeBenchmarks
{
    /// <summary>Gets or sets the number of scope-owned observers.</summary>
    [Params(0, 16, 256)]
    public int Observers { get; set; }

    /// <summary>Constructs and disposes a scope before mutating its surviving source.</summary>
    /// <returns>The callback count, which must stop increasing after scope disposal.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-SIG-05")]
    public int ScopeLifetime()
    {
        var source = Signal.Create(0);
        int calls = 0;
        using (var scope = new SignalScope())
        {
            scope.Run(() =>
            {
                for (int index = 0; index < Observers; index++)
                {
                    _ = Signal.Effect(() =>
                    {
                        _ = source.Value;
                        calls++;
                    });
                }
            });
            source.Value = 1;
        }

        source.Value = 2;
        return calls;
    }

    /// <summary>Retries a failed computed read in the same source revision.</summary>
    /// <returns>The recovered result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-SIG-08")]
    public int FailureAndRetry()
    {
        bool fail = true;
        Computed<int> computed = Signal.Computed(() => fail ? throw new InvalidOperationException("controlled") : 42);
        try
        {
            _ = computed.Value;
        }
        catch (InvalidOperationException)
        {
            fail = false;
        }

        return computed.Value;
    }

    /// <summary>Runs two actual workers against distinct sources in one shared observed graph.</summary>
    /// <returns>The final consistent sum after both workers join.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-SIG-11")]
    public async Task<int> ParallelWaveAsync()
    {
        var left = Signal.Create(0);
        var right = Signal.Create(0);
        Computed<int> sum = Signal.Computed(() => left.Value + right.Value);
        using SignalEffect effect = Signal.Effect(() => _ = sum.Value);
        await Task.WhenAll(
            Task.Run(() => Write(left)),
            Task.Run(() => Write(right)));
        return sum.Value;
    }

    private static void Write(Signal<int> source)
    {
        for (int index = 1; index <= 64; index++)
        {
            source.Value = index;
        }
    }
}
