namespace Lumyte.Execution.Benchmark;

internal sealed class Fixture : IDisposable
{
    internal Fixture(bool start = true, bool gated = false, int capacity = 1024, OwnerPlacement placement = OwnerPlacement.Host, bool generated = false)
    {
        Module = new WorkModule(gated, generated);
        Runtime = new ExecutionRuntime([Module]);
        Runtime.PrepareOnCurrentHost(placement, CpuExecutionMode.Cooperative, 1, new ExecutionOptions { MaxCommands = capacity, MaxInFlightRuns = 1024 });
        if (start)
        {
            Wait(Runtime.StartAsync().AsTask());
        }
    }

    internal WorkModule Module { get; }

    internal ExecutionRuntime Runtime { get; }

    public void Dispose() => Wait(Runtime.StopAsync().AsTask());

    internal void Pump()
    {
        Runtime.PumpHostLifecycle(1024, TimeSpan.FromSeconds(1));
        Runtime.PumpJobs(1024, TimeSpan.FromSeconds(1));
        if (Module.OnHost && Runtime.Status is ExecutionStatus.Running or ExecutionStatus.Quiescing)
        {
            Runtime.RunHostPhase(Module.Phase, 1024, TimeSpan.FromSeconds(1));
        }
    }

    internal void Until(Func<bool> completed)
    {
        for (int i = 0; i < 100000; i++)
        {
            if (completed())
            {
                return;
            }

            Pump();
            Thread.Yield();
        }

        throw new TimeoutException("Finite work did not complete.");
    }

    internal void Wait(Task task)
    {
        Until(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    internal int Read(ValueTask<int> result)
    {
        Until(() => result.IsCompleted);
        return result.GetAwaiter().GetResult();
    }
}
