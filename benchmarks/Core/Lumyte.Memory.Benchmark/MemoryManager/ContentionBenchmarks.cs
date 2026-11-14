using BenchmarkDotNet.Attributes;

namespace Lumyte.Memory.Benchmark;

/// <summary>Fixed background workers perform finite shared-ledger waves.</summary>
[BenchmarkCategory("Contention", "Standalone", "Core", "Memory", "Concurrency", "Screening", "Comparison", "Diagnostic")]
public class ContentionBenchmarks
{
    private readonly MemoryPoolId _pool = new("contended");
    private readonly MemoryManager _manager = new();
    private Thread[] _threads = [];
    private AutoResetEvent[] _starts = [];
    private CountdownEvent _completed = null!;
    private volatile bool _stop;
    private Exception? _failure;

    /// <summary>Gets or sets fixed worker count.</summary>
    [Params(2, 4)]
    public int Workers { get; set; }

    /// <summary>Creates workers and synchronization outside measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _completed = new CountdownEvent(Workers);
        _starts = new AutoResetEvent[Workers];
        _threads = new Thread[Workers];
        for (int i = 0; i < Workers; i++)
        {
            var start = new AutoResetEvent(false);
            _starts[i] = start;
            var thread = new Thread(() => Work(start));
            _threads[i] = thread;
            thread.Start();
        }
    }

    /// <summary>Starts all workers, joins the finite wave and consumes final ledger state.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    public ulong SharedBudgetWave()
    {
        _completed.Reset(Workers);
        foreach (AutoResetEvent start in _starts)
        {
            start.Set();
        }

        _completed.Wait();
        if (_failure is not null)
        {
            throw new InvalidOperationException("Worker failed.", _failure);
        }

        return _manager.GetSnapshot(_pool).CommittedBytes;
    }

    /// <summary>Stops and joins the fixed workers and releases synchronization owners.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _stop = true;
        foreach (AutoResetEvent start in _starts)
        {
            start.Set();
        }

        foreach (Thread thread in _threads)
        {
            thread.Join();
        }

        foreach (AutoResetEvent start in _starts)
        {
            start.Dispose();
        }

        _completed.Dispose();
        _manager.DisposeAsync().GetAwaiter().GetResult();
    }

    private void Work(AutoResetEvent start)
    {
        while (true)
        {
            start.WaitOne();
            if (_stop)
            {
                return;
            }

            try
            {
                IMemoryScope scope = _manager.CreateScope("worker");
                try
                {
                    for (int i = 0; i < 64; i++)
                    {
                        using IMemoryReservation reservation = scope.ReserveAsync(new MemoryRequest { Pool = _pool, Bytes = 4096, Purpose = MemoryPurpose.Build }).GetAwaiter().GetResult();
                        using IMemoryCharge charge = reservation.Commit(new MemoryUsage(4096, MemoryAccuracy.Exact));
                    }
                }
                finally
                {
                    scope.DisposeAsync().GetAwaiter().GetResult();
                }
            }
            catch (Exception error)
            {
                Interlocked.CompareExchange(ref _failure, error, null);
            }
            finally
            {
                _completed.Signal();
            }
        }
    }
}
