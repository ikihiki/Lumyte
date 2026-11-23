using Lumyte.Execution;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Guards owner-affine pumping against delayed cooperative and external continuations.</summary>
public sealed class CooperativeTestPumpTests
{
    /// <summary>A queued owner continuation may require more turns than the old fixed iteration cap.</summary>
    [Fact]
    public void DelayedOwnerContinuationOutlivesLegacyIterationBudget()
    {
        int owner = Environment.CurrentManagedThreadId;
        int turns = 0;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Progress()
        {
            Assert.Equal(owner, Environment.CurrentManagedThreadId);
            if (++turns == 100001)
            {
                completion.SetResult();
            }
        }

        CooperativeTestPump.Complete(Progress, completion.Task, TimeSpan.FromSeconds(15), "delayed owner continuation");
        Assert.Equal(100001, turns);
    }

    /// <summary>An external delayed completion progresses without moving the runtime's owner thread.</summary>
    [Fact]
    [Trait("Execution", "Integration")]
    public void DelayedExternalCompletionPreservesRuntimeOwner()
    {
        int owner = Environment.CurrentManagedThreadId;
        using var release = new ManualResetEventSlim();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var producer = new Thread(() =>
        {
            release.Wait();
            Thread.Sleep(100);
            completion.SetResult();
        });
        producer.Start();
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        try
        {
            CooperativeTestPump.Complete(runtime, runtime.StartAsync().AsTask());
            try
            {
                void Progress()
                {
                    Assert.Equal(owner, Environment.CurrentManagedThreadId);
                    release.Set();
                    runtime.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
                    runtime.PumpJobs(64, TimeSpan.FromMilliseconds(2));
                }

                CooperativeTestPump.Complete(Progress, completion.Task, TimeSpan.FromSeconds(15), "delayed external completion");
                Assert.Equal(owner, Environment.CurrentManagedThreadId);
            }
            finally
            {
                CooperativeTestPump.Complete(runtime, runtime.StopAsync().AsTask());
            }
        }
        finally
        {
            release.Set();
            Assert.True(producer.Join(TimeSpan.FromSeconds(5)), "Owned delayed producer did not finish.");
        }
    }

    /// <summary>A genuinely stalled task fails with a bounded diagnostic rather than a completion assertion.</summary>
    [Fact]
    public void StalledTaskReportsItsOperationAndStatus()
    {
        var completion = new TaskCompletionSource();
        TimeoutException error = Assert.Throws<TimeoutException>(() => CooperativeTestPump.Complete(() => { }, completion.Task, TimeSpan.FromMilliseconds(20), "stalled probe"));
        Assert.Contains("stalled probe", error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(TaskStatus.WaitingForActivation), error.Message, StringComparison.Ordinal);
        Assert.Contains("owner thread", error.Message, StringComparison.Ordinal);
    }
}
