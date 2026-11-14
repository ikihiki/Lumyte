using BenchmarkDotNet.Attributes;

namespace Lumyte.Signals.Benchmark;

/// <summary>Exercises genuinely pending completion and failed-load recovery.</summary>
[BenchmarkCategory("Standalone", "Core", "Signals", "Async", "Lifetime", "Screening", "Comparison", "Diagnostic")]
public class AsyncCompletionBenchmarks
{
    /// <summary>Publishes a pending load after its loader is explicitly completed.</summary>
    /// <returns>The terminal result after actual operation completion.</returns>
    [Benchmark]
    public async Task<int> PendingCompletionAsync()
    {
        var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using AsyncSignal<int> signal = Signal.FromAsync(() => pending.Task);
        Task<AsyncSignalState<int>> operation = signal.ReloadAsync();
        if (operation.IsCompleted || signal.Peek().Status != AsyncSignalStatus.Pending)
        {
            throw new InvalidOperationException("Loader did not suspend.");
        }

        pending.SetResult(42);
        AsyncSignalState<int> result = await operation;
        return result.Status == AsyncSignalStatus.Succeeded ? result.Result : -1;
    }

    /// <summary>Observes a real failed load and then successfully reloads the same adapter.</summary>
    /// <returns>The successfully recovered value.</returns>
    [Benchmark]
    public async Task<int> FailureAndRecoveryAsync()
    {
        var expected = new InvalidOperationException("controlled loader failure");
        bool fail = true;
        using AsyncSignal<int> signal = Signal.FromAsync(() => fail ? Task.FromException<int>(expected) : Task.FromResult(42));
        AsyncSignalState<int> failure = await signal.ReloadAsync();
        if (failure.Status != AsyncSignalStatus.Failed || !ReferenceEquals(expected, failure.Error))
        {
            throw new InvalidOperationException("Expected loader failure was not observed.");
        }

        fail = false;
        AsyncSignalState<int> recovered = await signal.ReloadAsync();
        return recovered.Status == AsyncSignalStatus.Succeeded ? recovered.Result : -1;
    }
}
