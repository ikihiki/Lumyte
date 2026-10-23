using System.Diagnostics;

namespace Lumyte.Execution.Tests;

internal sealed class WorkflowFixture : IDisposable
{
    internal WorkflowFixture(CpuExecutionMode mode = CpuExecutionMode.Cooperative, OwnerPlacement placement = OwnerPlacement.Host, ExecutionOptions? options = null, int concurrency = 1)
    {
        Runtime = new ExecutionRuntime([Module]);
        Runtime.PrepareOnCurrentHost(placement, mode, concurrency, options ?? new ExecutionOptions());
        Until(() => Runtime.StartAsync().IsCompleted);
    }

    internal WorkflowModule Module { get; } = new();

    internal ExecutionRuntime Runtime { get; }

    public void Dispose()
    {
        Task stop = Runtime.StopAsync().AsTask();
        Until(() => stop.IsCompleted);
        CompletedResult.Read(stop);
    }

    internal void Pump()
    {
        Runtime.PumpJobs(16, TimeSpan.FromMilliseconds(2));
        Runtime.PumpHostLifecycle(16, TimeSpan.FromMilliseconds(2));
        if (Module.OnHost)
        {
            Runtime.RunHostPhase(Module.Phase, 16, TimeSpan.FromMilliseconds(2));
        }
    }

    internal void Until(Func<bool> done)
    {
        long start = Stopwatch.GetTimestamp();
        while (!done())
        {
            Pump();
            if (Stopwatch.GetElapsedTime(start) > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException("Workflow did not complete.");
            }

            Thread.Sleep(1);
        }
    }

    internal int Wait(ValueTask<int> result)
    {
        Until(() => result.IsCompleted);
        return CompletedResult.Read(result);
    }
}
