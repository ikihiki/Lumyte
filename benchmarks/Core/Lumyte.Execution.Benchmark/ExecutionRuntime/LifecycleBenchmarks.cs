using BenchmarkDotNet.Attributes;

namespace Lumyte.Execution.Benchmark;

/// <summary>Measures fresh lifecycle and bounded refusal, gate, and cancellation operations.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Core", "Execution", "Screening", "Comparison", "Diagnostic")]
public class LifecycleBenchmarks
{
    /// <summary>Holds one completed result and confirms capacity resumes after observation.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("Admission")]
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
    [BenchmarkCategory("Gates")]
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
    [BenchmarkCategory("Concurrency")]
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

    /// <summary>Checks pre-cancellation without enqueuing side effects.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("Cancellation")]
    public CommandEnqueueStatus PreCanceled()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        return fixture.Module.Port.Enqueue(1, out _, cancellation.Token);
    }
}
