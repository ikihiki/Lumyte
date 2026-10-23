using System.Diagnostics;

namespace Lumyte.Execution.Tests;

internal sealed class ExecutionFixture : IDisposable
{
    internal ExecutionFixture(OwnerPlacement placement = OwnerPlacement.Host, ExecutionOptions? options = null, bool gated = false, CpuExecutionMode cpuMode = CpuExecutionMode.Cooperative, int concurrency = 1)
    {
        Module = new RecordingModule { Gated = gated };
        Runtime = new ExecutionRuntime([Module]);
        Runtime.PrepareOnCurrentHost(placement, cpuMode, concurrency, options ?? new ExecutionOptions());
    }

    internal RecordingModule Module { get; }

    internal ExecutionRuntime Runtime { get; }

    public void Dispose()
    {
        if (Runtime.Status != ExecutionStatus.Stopped)
        {
            Wait(Runtime.StopAsync().AsTask());
        }
    }

    internal void Start() => Wait(Runtime.StartAsync().AsTask());

    internal void Pump()
    {
        Runtime.PumpHostLifecycle(16, TimeSpan.FromMilliseconds(2));
        Runtime.PumpJobs(16, TimeSpan.FromMilliseconds(2));
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
                throw new TimeoutException("Host work did not complete.");
            }

            Thread.Sleep(1);
        }
    }

    internal void Wait(Task task)
    {
        Until(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    internal TResult Wait<TResult>(ValueTask<TResult> result)
    {
        Until(() => result.IsCompleted);
        return result.GetAwaiter().GetResult();
    }
}
