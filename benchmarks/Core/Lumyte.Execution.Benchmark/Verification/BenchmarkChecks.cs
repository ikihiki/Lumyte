namespace Lumyte.Execution.Benchmark;

/// <summary>Checks actual finite execution results and refusal boundaries.</summary>
public static class BenchmarkChecks
{
    private static readonly object _senderLock = new();
    private static bool _senderVerified;

    /// <summary>Verifies all runtime benchmark operations without measuring time.</summary>
    public static void Verify()
    {
        var controls = new SenderControlBenchmarks();
        controls.Setup();
        if (controls.Direct() != 14 || controls.CachedDelegate() != 14 || controls.CapturedDelegate() != 14 || controls.ObservedValue != 7)
        {
            throw new InvalidOperationException("Sender comparison controls differ.");
        }

        lock (_senderLock)
        {
            if (!_senderVerified)
            {
                var generated = new GeneratedSenderBenchmarks();
                generated.Setup();
                try
                {
                    if (generated.GeneratedSender() != 14 || generated.ObservedValue != 7)
                    {
                        throw new InvalidOperationException("Generated sender failed.");
                    }
                }
                finally
                {
                    generated.Cleanup();
                }

                _senderVerified = true;
            }
        }

        foreach (int count in new[] { 1, 16, 256 })
        {
            var benchmark = new ExecutionBenchmarks { Count = count };
            benchmark.Setup();
            try
            {
                if (benchmark.Single() != 14 || benchmark.Batch() != (count - 1) * 2 || benchmark.Graph() != count || benchmark.Range() != count || benchmark.Workflow() != 7 || benchmark.SuspendedWorkflow() != 42 || benchmark.ChildWorkflow() != 14 || benchmark.HostTurn() != count * (count - 1))
                {
                    throw new InvalidOperationException("Execution result oracle failed.");
                }
            }
            finally
            {
                benchmark.Cleanup();
            }
        }

        if (new DependencyInjectionBenchmarks().ResolveAndPrepare() != 1)
        {
            throw new InvalidOperationException("DI module collection failed.");
        }

        var lifecycle = new LifecycleBenchmarks();
        var runtime = new RuntimeLifecycleBenchmarks();
        runtime.Prepare();
        runtime.Cleanup();
        runtime.PrepareStart();
        if (runtime.Start() != ExecutionStatus.Running || runtime.Stop() != ExecutionStatus.Stopped)
        {
            throw new InvalidOperationException("Lifecycle state failed.");
        }

        runtime.Cleanup();
        if (lifecycle.RetainedCapacity() != CommandEnqueueStatus.QueueFull || lifecycle.GateRelease() != 14 || lifecycle.PreCanceled() != CommandEnqueueStatus.Canceled || lifecycle.DedicatedProducers() != 240)
        {
            throw new InvalidOperationException("Admission/lifecycle oracle failed.");
        }
    }
}
