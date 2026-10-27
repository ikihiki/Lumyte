using System.Collections.Concurrent;
using System.Threading.Tasks.Sources;
using Xunit;

namespace Lumyte.Execution.Tests;

/// <summary>Checks actual admission, ownership, lifecycle, cancellation, and retained capacity.</summary>
public sealed class RuntimeTests
{
    /// <summary>Definitions are collected once and devices start only after explicit startup.</summary>
    [Fact]
    public void CollectionAndPreparationDoNotStartOwners()
    {
        using var fixture = new ExecutionFixture();
        Assert.Equal(1, fixture.Module.DeclareCalls);
        Assert.Equal(1, fixture.Module.ConfigureCalls);
        Assert.Equal(0, fixture.Module.OwnerThread);
        Assert.Equal(ExecutionStatus.Created, fixture.Runtime.Status);
        Assert.Equal(CommandEnqueueStatus.NotRunning, fixture.Module.Port.Enqueue(1, out _, default));
        fixture.Start();
        Assert.Equal(Environment.CurrentManagedThreadId, fixture.Module.OwnerThread);
    }

    /// <summary>Unprepared and wrong-host operations are rejected.</summary>
    [Fact]
    public void PreparationAndFixedHostAreRequired()
    {
        var unprepared = new ExecutionRuntime([new RecordingModule()]);
        Assert.Throws<InvalidOperationException>(() => unprepared.StartAsync());
        using var fixture = new ExecutionFixture();
        Assert.Throws<InvalidOperationException>(() => fixture.Runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions()));
        Task worker = Task.Run(() => Assert.Throws<InvalidOperationException>(() => fixture.Runtime.PumpJobs(1, TimeSpan.FromMilliseconds(1))));
        fixture.Wait(worker);
    }

    /// <summary>Commands always enqueue, even when caller and owner are the same thread.</summary>
    [Fact]
    public void SameThreadCommandsAreDeferredAndResultsAreSingleUse()
    {
        using var fixture = new ExecutionFixture();
        fixture.Start();
        int value = 4;
        Assert.Equal(CommandEnqueueStatus.Accepted, fixture.Module.Port.Enqueue(in value, out IValueTaskSource<int>? source, default));
        Assert.Empty(fixture.Module.Executed);
        Assert.Throws<InvalidOperationException>(() => CompletedResult.ReadSource(source!));
        fixture.Until(() => source!.GetStatus(0) != ValueTaskSourceStatus.Pending);
        Assert.Equal(8, CompletedResult.ReadSource(source!));
        Assert.Throws<InvalidOperationException>(() => CompletedResult.ReadSource(source!));
    }

    /// <summary>Different typed endpoints share owner FIFO ordering.</summary>
    [Fact]
    public void DifferentCommandTypesPreserveOneGroupOrder()
    {
        using var fixture = new ExecutionFixture();
        fixture.Start();
        ValueTask<int> first = fixture.Module.Port.ExecuteAsync(1);
        ValueTask<long> second = fixture.Module.OtherPort.ExecuteAsync(2);
        ValueTask<int> third = fixture.Module.Port.ExecuteAsync(3);
        Assert.Equal(2, fixture.Wait(first));
        Assert.Equal(6, fixture.Wait(second));
        Assert.Equal(6, fixture.Wait(third));
        Assert.Equal(new[] { 1, 2, 3 }, fixture.Module.Executed.ToArray());
    }

    /// <summary>Unobserved results consume normal capacity but cannot block reserved cleanup.</summary>
    [Fact]
    public void RetainedResultDoesNotBlockShutdownCleanup()
    {
        using var fixture = new ExecutionFixture(options: new ExecutionOptions { MaxInFlightRuns = 1, MaxCommands = 1 });
        fixture.Start();
        ValueTask<int> result = fixture.Module.Port.ExecuteAsync(2);
        fixture.Until(() => result.IsCompleted);
        Assert.Equal(CommandEnqueueStatus.QueueFull, fixture.Module.Port.Enqueue(3, out _, default));
        fixture.Wait(fixture.Runtime.StopAsync().AsTask());
        Assert.Equal(1, fixture.Module.CleanupCalls);
        Assert.Equal(4, CompletedResult.Read(result));
        Assert.Equal(CommandEnqueueStatus.NotRunning, fixture.Module.Port.Enqueue(0, out _, default));
    }

    /// <summary>A failed cleanup is retried on the original owner without restarting resources.</summary>
    [Fact]
    public void FailedCleanupKeepsOwnerForRetry()
    {
        using var fixture = new ExecutionFixture(OwnerPlacement.Dedicated);
        fixture.Module.FailCleanupOnce = true;
        fixture.Start();
        int owner = fixture.Module.OwnerThread;
        Assert.Throws<InvalidOperationException>(() => fixture.Wait(fixture.Runtime.StopAsync().AsTask()));
        fixture.Wait(fixture.Runtime.StopAsync().AsTask());
        Assert.Equal(2, fixture.Module.CleanupCalls);
        Assert.Equal(owner, fixture.Module.CleanupThread);
        Assert.Equal(ExecutionStatus.Stopped, fixture.Runtime.Status);
    }

    /// <summary>Gate waits do not execute a command, and cancellation before start has no side effect.</summary>
    [Fact]
    public void GateWaitAndCancellationSuppressUnstartedWork()
    {
        using var fixture = new ExecutionFixture(gated: true);
        fixture.Start();
        using var cancellation = new CancellationTokenSource();
        ValueTask<int> canceled = fixture.Module.Port.ExecuteAsync(1, cancellation.Token);
        fixture.Pump();
        Assert.False(canceled.IsCompleted);
        cancellation.Cancel();
        fixture.Until(() => canceled.IsCompleted);
        Assert.Throws<OperationCanceledException>(() => CompletedResult.Read(canceled));
        Assert.Empty(fixture.Module.Executed);
        fixture.Module.Gate!.Open();
        Assert.Equal(4, fixture.Wait(fixture.Module.Port.ExecuteAsync(2)));
    }

    /// <summary>Permanent gate failure reaches waiting results and cannot be reopened.</summary>
    [Fact]
    public void GateFailureIsReportedToWaitingCommands()
    {
        using var fixture = new ExecutionFixture(gated: true);
        fixture.Start();
        ValueTask<int> result = fixture.Module.Port.ExecuteAsync(1);
        var error = new InvalidOperationException("Device unavailable.");
        fixture.Module.Gate!.Fail(error);
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => fixture.Wait(result)));
        Assert.Throws<InvalidOperationException>(() => fixture.Module.Gate.Open());
    }

    /// <summary>A cancellation after start preserves the actual result and does not complete early.</summary>
    [Fact]
    public void CancellationAfterStartRetainsActualResult()
    {
        using var fixture = new ExecutionFixture(OwnerPlacement.Dedicated);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        fixture.Module.BeforeExecute = _ =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException();
            }
        };
        fixture.Start();
        using var cancellation = new CancellationTokenSource();
        ValueTask<int> result = fixture.Module.Port.ExecuteAsync(5, cancellation.Token);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        cancellation.Cancel();
        Assert.False(result.IsCompleted);
        release.Set();
        Assert.Equal(10, fixture.Wait(result));
    }

    /// <summary>Lifecycle authority expires and static startup entry access cannot bypass it.</summary>
    [Fact]
    public void StartupPermissionCannotEscapeContext()
    {
        using var fixture = new ExecutionFixture();
        fixture.Start();
        Assert.Throws<InvalidOperationException>(() => fixture.Module.StartupPort.Enqueue(0, out _, default));
        Assert.Throws<InvalidOperationException>(() => fixture.Module.SavedContext!.GetGate(default));
        Assert.Throws<InvalidOperationException>(() => fixture.Module.SavedContext!.ExecuteAsync(fixture.Module.InitializeKey, 0));
    }

    /// <summary>Handler submissions are rejected and original exceptions stay local to the run.</summary>
    [Fact]
    public void HandlerCannotRecursivelySubmit()
    {
        using var fixture = new ExecutionFixture();
        fixture.Start();
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => fixture.Wait(fixture.Module.Port.ExecuteAsync(1000)));
        Assert.Contains("Handlers cannot", error.Message, StringComparison.Ordinal);
        Assert.Equal(ExecutionStatus.Running, fixture.Runtime.Status);
        Assert.Equal(2, fixture.Wait(fixture.Module.Port.ExecuteAsync(1)));
    }

    /// <summary>Handler OperationCanceledException is a domain fault rather than pre-start cancellation.</summary>
    [Fact]
    public void HandlerCancellationExceptionIsFaulted()
    {
        using var fixture = new ExecutionFixture();
        fixture.Start();
        Assert.Equal(CommandEnqueueStatus.Accepted, fixture.Module.Port.Enqueue(-999, out IValueTaskSource<int>? source, default));
        fixture.Until(() => source!.GetStatus(0) != ValueTaskSourceStatus.Pending);
        Assert.Equal(ValueTaskSourceStatus.Faulted, source!.GetStatus(0));
        Assert.Throws<OperationCanceledException>(() => CompletedResult.ReadSource(source!));
    }

    /// <summary>Concurrent producers preserve each producer's order on one physical owner.</summary>
    [Fact]
    public void MultipleProducersDoNotLoseCommands()
    {
        using var fixture = new ExecutionFixture(OwnerPlacement.Dedicated, new ExecutionOptions { MaxInFlightRuns = 512 });
        fixture.Start();
        var results = new ConcurrentBag<ValueTask<int>>();
        var producers = Task.WhenAll(Enumerable.Range(0, 4).Select(producer => Task.Run(() =>
        {
            for (int i = 0; i < 50; i++)
            {
                results.Add(fixture.Module.Port.ExecuteAsync((producer * 50) + i));
            }
        })));
        fixture.Wait(producers);
        fixture.Until(() => results.All(result => result.IsCompleted));
        foreach (ValueTask<int> result in results)
        {
            CompletedResult.Read(result);
        }

        int[] executed = fixture.Module.Executed.ToArray();
        Assert.Equal(200, executed.Length);
        Assert.Equal(200, executed.Distinct().Count());
        for (int producer = 0; producer < 4; producer++)
        {
            Assert.Equal(Enumerable.Range(producer * 50, 50), executed.Where(value => value / 50 == producer));
        }
    }

    /// <summary>A blocked FIFO does not freeze later arrivals in another group on the same phase.</summary>
    [Fact]
    public void BlockedGroupDoesNotFreezeHostPhaseSequence()
    {
        using var fixture = new ExecutionFixture(gated: true);
        fixture.Start();
        ValueTask<int> blocked = fixture.Module.Port.ExecuteAsync(1);
        fixture.Pump();
        ValueTask<int> independent = fixture.Module.IndependentPort.ExecuteAsync(2);
        Assert.Equal(4, fixture.Wait(independent));
        Assert.False(blocked.IsCompleted);
        fixture.Module.Gate!.Open();
        Assert.Equal(2, fixture.Wait(blocked));
        Assert.Equal(new[] { 2, 1 }, fixture.Module.Executed.ToArray());
    }

    /// <summary>Stop during startup cannot reopen normal admission or updates.</summary>
    [Fact]
    public void StopDuringStartupWaitsForCleanupWithoutRunning()
    {
        using var fixture = new ExecutionFixture();
        Task startup = fixture.Runtime.StartAsync().AsTask();
        Task shutdown = fixture.Runtime.StopAsync().AsTask();
        fixture.Wait(shutdown);
        Assert.Throws<OperationCanceledException>(() => CompletedResult.Read(startup));
        Assert.Equal(0, fixture.Module.OwnerThread);
        Assert.Equal(0, fixture.Module.Updates);
        Assert.Equal(ExecutionStatus.Stopped, fixture.Runtime.Status);
    }

    /// <summary>Drain can be strengthened to cancel without replacing the shared shutdown operation.</summary>
    [Fact]
    public void DrainCanBeStrengthenedToCancelPending()
    {
        using var fixture = new ExecutionFixture(gated: true);
        fixture.Start();
        ValueTask<int> pending = fixture.Module.Port.ExecuteAsync(1);
        Task drain = fixture.Runtime.StopAsync(ShutdownMode.Drain).AsTask();
        fixture.Pump();
        Assert.False(drain.IsCompleted);
        Task cancel = fixture.Runtime.StopAsync(ShutdownMode.CancelPending).AsTask();
        Assert.Same(drain, cancel);
        fixture.Wait(cancel);
        Assert.Throws<OperationCanceledException>(() => CompletedResult.Read(pending));
    }

    /// <summary>Periodic updates run directly without an external producer loop.</summary>
    [Fact]
    public void DedicatedOwnerDrivesUpdatesIndependently()
    {
        using var fixture = new ExecutionFixture(OwnerPlacement.Dedicated);
        fixture.Start();
        Assert.NotEqual(Environment.CurrentManagedThreadId, fixture.Module.OwnerThread);
        fixture.Until(() => fixture.Module.Updates >= 3);
        Assert.Empty(fixture.Module.Executed);
    }
}
