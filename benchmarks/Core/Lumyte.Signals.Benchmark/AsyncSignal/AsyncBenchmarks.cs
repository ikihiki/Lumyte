using BenchmarkDotNet.Attributes;

namespace Lumyte.Signals.Benchmark;

/// <summary>Measures asynchronous adapters with deterministic inline task completion.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Core", "Signals", "Async", "Screening", "Comparison", "Diagnostic")]
public class AsyncBenchmarks
{
    private static readonly Task<int> _completed = Task.FromResult(42);
    private static readonly Func<Task<int>> _completedLoader = () => _completed;
    private AsyncSignal<int> _plain = null!;
    private AsyncSignal<int> _observed = null!;
    private AsyncSignal<int> _pending = null!;
    private SignalEffect _effect = null!;
    private Task<int> _nextPending = _completed;
    private int _notifications;
    private int _observedResult;

    internal int Notifications => _notifications;

    internal AsyncSignalStatus PendingStatus => _pending.Value.Status;

    /// <summary>Creates reusable adapters and their observer outside the measured operation.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _plain = Signal.FromAsync(_completedLoader);
        _observed = Signal.FromAsync(_completedLoader);
        _pending = Signal.FromAsync(() => _nextPending);
        _effect = Signal.Effect(() =>
        {
            AsyncSignalState<int> state = _observed.Value;
            _observedResult = state.Result;
            _notifications++;
        });
    }

    /// <summary>Releases all subscriptions and adapters after measurement.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _effect.Dispose();
        _pending.Dispose();
        _observed.Dispose();
        _plain.Dispose();
    }

    /// <summary>Measures creation, a synchronously completed load, and disposal.</summary>
    /// <returns>The loaded result.</returns>
    [Benchmark]
    public int CreateCompleted()
    {
        using AsyncSignal<int> signal = Signal.FromAsync(_completedLoader);
        return signal.Value.Result;
    }

    /// <summary>Measures a reload's Pending and Succeeded transitions without observers.</summary>
    /// <returns>The loaded result.</returns>
    [Benchmark]
    public int ReloadCompleted() => _plain.ReloadAsync().GetAwaiter().GetResult().Result;

    /// <summary>Measures the same reload with one synchronous observer notified twice.</summary>
    /// <returns>The result last delivered to the observer.</returns>
    [Benchmark]
    public int ReloadCompletedObserved()
    {
        _ = _observed.ReloadAsync().GetAwaiter().GetResult();
        return _observedResult;
    }

    /// <summary>Measures two pending loads, supersession, cancellation, and stale completion cleanup.</summary>
    /// <remarks>Includes allocating two loader task sources; their continuations run inline without ThreadPool scheduling.</remarks>
    /// <returns>The number of canceled operation completion tasks.</returns>
    [Benchmark]
    public int SupersedeAndCancelPending()
    {
        var first = new TaskCompletionSource<int>();
        var second = new TaskCompletionSource<int>();
        _nextPending = first.Task;
        Task<AsyncSignalState<int>> obsolete = _pending.ReloadAsync();
        _nextPending = second.Task;
        Task<AsyncSignalState<int>> current = _pending.ReloadAsync();
        _pending.Cancel();
        first.SetResult(1);
        second.SetResult(2);
        return (obsolete.GetAwaiter().GetResult().Status == AsyncSignalStatus.Canceled ? 1 : 0)
            + (current.GetAwaiter().GetResult().Status == AsyncSignalStatus.Canceled ? 1 : 0);
    }
}
