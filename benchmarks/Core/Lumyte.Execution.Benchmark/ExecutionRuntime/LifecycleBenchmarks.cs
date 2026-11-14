using BenchmarkDotNet.Attributes;

namespace Lumyte.Execution.Benchmark.Runtime;

/// <summary>Measures fresh lifecycle and bounded refusal, gate, and cancellation operations.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class LifecycleBenchmarks
{
    /// <summary>Holds one completed result and confirms capacity resumes after observation.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    public CommandEnqueueStatus RetainedCapacity()
    {
        using var fixture = new Fixture(capacity: 1);
        ValueTask<int> retained = fixture.Module.Port.ExecuteAsync(3);
        fixture.Until(() => retained.IsCompleted);
        CommandEnqueueStatus status = fixture.Module.Port.Enqueue(4, out _);
        if (fixture.Read(retained) != 6 || fixture.Read(fixture.Module.Port.ExecuteAsync(4)) != 8)
        {
            throw new InvalidOperationException("Result capacity did not release.");
        }

        return status;
    }

    /// <summary>Releases one actual gate waiter and observes its result.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    public int GateRelease()
    {
        using var fixture = new Fixture(gated: true);
        ValueTask<int> result = fixture.Module.Port.ExecuteAsync(7);
        fixture.Pump();
        if (result.IsCompleted || fixture.Module.Commands != 0)
        {
            throw new InvalidOperationException("Closed gate executed work.");
        }

        fixture.Module.Gate.Open();
        return fixture.Read(result);
    }

    /// <summary>Starts and joins two dedicated producer workers, then drains every result from a dedicated owner.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    public int DedicatedProducers()
    {
        using var fixture = new Fixture(placement: OwnerPlacement.Dedicated);
        var results = new ValueTask<int>[16];
        using var ready = new Barrier(2);
        Task first = Task.Factory.StartNew(() => Produce(0), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task second = Task.Factory.StartNew(() => Produce(1), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task.WaitAll(first, second);

        void Produce(int producer)
        {
            ready.SignalAndWait();
            for (int i = producer * 8; i < (producer + 1) * 8; i++)
            {
                results[i] = fixture.Module.Port.ExecuteAsync(i);
            }
        }

        int sum = 0;
        foreach (ValueTask<int> result in results)
        {
            sum += fixture.Read(result);
        }

        return sum;
    }

    /// <summary>Cancels an admitted command behind a closed gate and proves capacity recovers.</summary>
    /// <returns>The number of actual cancellation completions observed.</returns>
    [Benchmark]
    public int CancelAdmitted()
    {
        using var fixture = new Fixture(gated: true, capacity: 1);
        using var cancellation = new CancellationTokenSource();
        ValueTask<int> pending = fixture.Module.Port.ExecuteAsync(3, cancellation.Token);
        fixture.Pump();
        if (pending.IsCompleted || fixture.Module.Commands != 0)
        {
            throw new InvalidOperationException("Command did not wait behind its closed gate.");
        }

        cancellation.Cancel();
        int canceled = 0;
        try
        {
            _ = fixture.Read(pending);
        }
        catch (OperationCanceledException)
        {
            canceled++;
        }

        fixture.Module.Gate.Open();
        if (fixture.Read(fixture.Module.Port.ExecuteAsync(4)) != 8 || fixture.Module.Commands != 1)
        {
            throw new InvalidOperationException("Canceled command executed or retained admission capacity.");
        }

        return canceled;
    }

    /// <summary>Checks pre-cancellation without enqueuing side effects.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    public CommandEnqueueStatus PreCanceled()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        return fixture.Module.Port.Enqueue(1, out _, cancellation.Token);
    }
}
