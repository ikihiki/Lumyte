using System.Threading.Tasks.Sources;
using Xunit;

namespace Lumyte.Execution.Tests;

/// <summary>Checks async workflow scheduling, completion ownership, and shutdown.</summary>
public sealed class WorkflowTests
{
    /// <summary>Both entry and I/O continuations require cooperative pumping on the fixed host.</summary>
    [Fact]
    public void CooperativeEntryAndContinuationRequirePumping()
    {
        using var fixture = new WorkflowFixture();
        var ready = new TaskCompletionSource();
        int entered = 0;
        int resumed = 0;
        int host = Environment.CurrentManagedThreadId;
        SynchronizationContext? outer = SynchronizationContext.Current;
        fixture.Module.Handler = async (value, _) =>
        {
            entered++;
            Assert.Equal(host, Environment.CurrentManagedThreadId);
            Assert.NotSame(outer, SynchronizationContext.Current);
            await ready.Task;
            resumed++;
            Assert.Equal(host, Environment.CurrentManagedThreadId);
            return value;
        };

        ValueTask<int> result = fixture.Module.Port.ExecuteAsync(12);
        Assert.Equal(0, entered);
        fixture.Runtime.PumpJobs(1, TimeSpan.FromSeconds(1));
        Assert.Equal(1, entered);
        Assert.False(result.IsCompleted);
        Assert.Same(outer, SynchronizationContext.Current);
        ready.SetResult();
        Assert.Equal(0, resumed);
        Assert.Equal(12, fixture.Wait(result));
        Assert.Equal(1, resumed);
        Assert.Same(outer, SynchronizationContext.Current);
    }

    /// <summary>Each resumed synchronous dispatch gets a fresh budget, excluding time spent waiting for I/O.</summary>
    [Fact]
    public void DispatchBudgetExcludesSuspendedTime()
    {
        using var fixture = new WorkflowFixture();
        var ready = new TaskCompletionSource();
        var budget = TimeSpan.FromMilliseconds(50);
        bool entered = false;
        bool freshAfterResume = false;
        bool expiredAfterWork = false;
        Assert.False(ExecutionRuntime.ShouldYield(budget));
        fixture.Module.Handler = async (value, _) =>
        {
            ExecutionRuntime.ShouldYield(budget);
            entered = true;
            await ready.Task;
            freshAfterResume = !ExecutionRuntime.ShouldYield(budget);
            Thread.Sleep(75);
            expiredAfterWork = ExecutionRuntime.ShouldYield(budget);
            return value;
        };

        ValueTask<int> result = fixture.Module.Port.ExecuteAsync(12);
        fixture.Runtime.PumpJobs(1, TimeSpan.FromSeconds(1));
        Assert.True(entered);
        Thread.Sleep(75);
        Assert.False(ExecutionRuntime.ShouldYield(budget));
        ready.SetResult();
        Assert.Equal(12, fixture.Wait(result));
        Assert.True(freshAfterResume);
        Assert.True(expiredAfterWork);
        Assert.False(ExecutionRuntime.ShouldYield(budget));
    }

    /// <summary>A suspended operation leaves the only CPU worker available for independent work.</summary>
    [Fact]
    public void WaitingWorkflowDoesNotHoldCpuSlot()
    {
        using var fixture = new WorkflowFixture(CpuExecutionMode.ThreadPool);
        using var entered = new ManualResetEventSlim();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Module.Handler = async (value, _) =>
        {
            entered.Set();
            await ready.Task;
            return await fixture.Module.Cpu.ExecuteAsync(value);
        };

        ValueTask<int> result = fixture.Module.Port.ExecuteAsync(9);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            Assert.Equal(6, fixture.Wait(fixture.Module.Cpu.ExecuteAsync(3)));
            Assert.False(result.IsCompleted);
        }
        finally
        {
            ready.TrySetResult();
        }

        Assert.Equal(18, fixture.Wait(result));
    }

    /// <summary>Nested workflows and owner commands can be awaited even with one CPU worker.</summary>
    /// <param name="mode">The CPU execution mode.</param>
    /// <param name="placement">The owner placement.</param>
    [Theory]
    [InlineData(CpuExecutionMode.Cooperative, OwnerPlacement.Host)]
    [InlineData(CpuExecutionMode.ThreadPool, OwnerPlacement.Dedicated)]
    public void NestedWorkflowAndOwnerCommandsDoNotDeadlock(CpuExecutionMode mode, OwnerPlacement placement)
    {
        using var fixture = new WorkflowFixture(mode, placement);
        fixture.Module.Handler = async (value, _) =>
        {
            if (value == 1)
            {
                return await fixture.Module.Port.ExecuteAsync(2);
            }

            int cpu = await fixture.Module.Cpu.ExecuteAsync(value);
            return await fixture.Module.Owner.ExecuteAsync(cpu);
        };

        Assert.Equal(8, fixture.Wait(fixture.Module.Port.ExecuteAsync(1)));
        Assert.Equal(2, fixture.Module.Commands);
        if (placement == OwnerPlacement.Dedicated)
        {
            Assert.NotEqual(Environment.CurrentManagedThreadId, fixture.Module.OwnerThread);
        }
    }

    /// <summary>Shutdown preserves started operations and their subsequent owner commands.</summary>
    [Fact]
    public void ShutdownWaitsForStartedWorkflowAndOwnerWork()
    {
        using var fixture = new WorkflowFixture();
        var ready = new TaskCompletionSource();
        int entered = 0;
        fixture.Module.Handler = async (value, _) =>
        {
            entered++;
            await ready.Task;
            return await fixture.Module.Owner.ExecuteAsync(value);
        };

        ValueTask<int> result = fixture.Module.Port.ExecuteAsync(7);
        fixture.Runtime.PumpJobs(1, TimeSpan.FromSeconds(1));
        ValueTask<int> unstarted = fixture.Module.Port.ExecuteAsync(99);
        Task stop = fixture.Runtime.StopAsync().AsTask();
        fixture.Pump();
        Assert.False(stop.IsCompleted);
        Assert.Throws<OperationCanceledException>(() => CompletedResult.Read(unstarted));
        Assert.Equal(1, entered);
        Assert.Equal(CommandEnqueueStatus.NotRunning, fixture.Module.Port.Enqueue(0, out _, default));
        ready.SetResult();
        fixture.Until(() => stop.IsCompleted);
        CompletedResult.Read(stop);
        Assert.Equal(14, CompletedResult.Read(result));
        Assert.Equal(1, fixture.Module.Commands);
    }

    /// <summary>Cancellation suppresses unstarted handlers but cannot discard an already started result.</summary>
    [Fact]
    public void CancellationAfterStartPreservesActualResult()
    {
        using var fixture = new WorkflowFixture();
        using var cancellation = new CancellationTokenSource();
        var ready = new TaskCompletionSource();
        int entered = 0;
        fixture.Module.Handler = async (value, token) =>
        {
            entered++;
            await ready.Task;
            Assert.True(token.IsCancellationRequested);
            return value;
        };

        ValueTask<int> result = fixture.Module.Port.ExecuteAsync(5, cancellation.Token);
        fixture.Runtime.PumpJobs(1, TimeSpan.FromSeconds(1));
        ValueTask<int> unstarted = fixture.Module.Port.ExecuteAsync(99, cancellation.Token);
        cancellation.Cancel();
        fixture.Pump();
        Assert.False(result.IsCompleted);
        Assert.Throws<OperationCanceledException>(() => CompletedResult.Read(unstarted));
        ready.SetResult();
        Assert.Equal(5, fixture.Wait(result));
        Assert.Equal(1, entered);
    }

    /// <summary>A domain cancellation exception is faulted and observation releases admission capacity.</summary>
    [Fact]
    public void FaultsRemainLocalAndCapacityIsRetainedUntilObservation()
    {
        using var fixture = new WorkflowFixture(options: new ExecutionOptions { MaxInFlightRuns = 1, MaxCommands = 1 });
        fixture.Module.Handler = async (_, _) =>
        {
            await Task.Yield();
            throw new OperationCanceledException("Domain failure.");
        };

        Assert.Equal(CommandEnqueueStatus.Accepted, fixture.Module.Port.Enqueue(0, out IValueTaskSource<int>? source, default));
        fixture.Until(() => source!.GetStatus(0) != ValueTaskSourceStatus.Pending);
        Assert.Equal(ValueTaskSourceStatus.Faulted, source!.GetStatus(0));
        Assert.Equal(CommandEnqueueStatus.QueueFull, fixture.Module.Port.Enqueue(0, out _, default));
        Assert.Throws<OperationCanceledException>(() => CompletedResult.ReadSource(source));
        Assert.Throws<InvalidOperationException>(() => CompletedResult.ReadSource(source));
        fixture.Module.Handler = (value, _) => ValueTask.FromResult(value);
        ValueTask<int> result = fixture.Module.Port.ExecuteAsync(4);
        fixture.Until(() => result.IsCompleted);
        Task stop = fixture.Runtime.StopAsync().AsTask();
        fixture.Until(() => stop.IsCompleted);
        CompletedResult.Read(stop);
        Assert.Equal(4, CompletedResult.Read(result));
    }

    /// <summary>The root operation is observed once even when its result source invokes completion inline.</summary>
    [Fact]
    public void ValueTaskSourceIsObservedExactlyOnce()
    {
        using var fixture = new WorkflowFixture();
        var operation = new SingleResultSource();
        fixture.Module.Handler = (_, _) => new ValueTask<int>(operation, 0);
        ValueTask<int> result = fixture.Module.Port.ExecuteAsync(0);
        fixture.Runtime.PumpJobs(1, TimeSpan.FromSeconds(1));
        Assert.Equal(0, operation.Observations);
        operation.Complete(17);
        Assert.False(result.IsCompleted);
        Assert.Equal(17, fixture.Wait(result));
        Assert.Equal(1, operation.Observations);
    }

    /// <summary>Root completion drains accepted continuations and preserves the first failure without rereading its result.</summary>
    /// <param name="rootFault">Whether the root result source fails.</param>
    /// <param name="callbackFault">Whether a trailing callback fails.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RootCompletionDrainsAcceptedCallbacks(bool rootFault, bool callbackFault)
    {
        using var fixture = new WorkflowFixture();
        var operation = new SingleResultSource();
        var rootError = new InvalidOperationException("Root failure.");
        var callbackError = new IOException("Callback failure.");
        SynchronizationContext? workflowContext = null;
        int callbacks = 0;
        fixture.Module.Handler = (_, _) =>
        {
            workflowContext = SynchronizationContext.Current;
            return new ValueTask<int>(operation, 0);
        };

        ValueTask<int> result = fixture.Module.Port.ExecuteAsync(0);
        fixture.Runtime.PumpJobs(1, TimeSpan.FromSeconds(1));
        Assert.NotNull(workflowContext);
        workflowContext.Post(
            _ =>
            {
                callbacks++;
                workflowContext.Post(
                    _ =>
                    {
                        callbacks++;
                        if (callbackFault)
                        {
                            throw callbackError;
                        }
                    },
                    null);
            },
            null);
        if (rootFault)
        {
            operation.Fail(rootError);
        }
        else
        {
            operation.Complete(17);
        }

        fixture.Runtime.PumpJobs(1, TimeSpan.FromSeconds(1));
        Assert.Equal(1, callbacks);
        Assert.Equal(1, operation.Observations);
        Assert.False(result.IsCompleted);
        fixture.Until(() => result.IsCompleted);
        Assert.Equal(2, callbacks);
        Assert.Equal(1, operation.Observations);
        if (rootFault)
        {
            Assert.Same(rootError, Assert.Throws<InvalidOperationException>(() => CompletedResult.Read(result)));
        }
        else if (callbackFault)
        {
            Assert.Same(callbackError, Assert.Throws<IOException>(() => CompletedResult.Read(result)));
        }
        else
        {
            Assert.Equal(17, CompletedResult.Read(result));
        }
    }

    /// <summary>Workflow authority does not permit pumps or lifecycle changes or escape to ordinary handlers.</summary>
    [Fact]
    public void WorkflowSubmissionAuthorityDoesNotEscape()
    {
        using var fixture = new WorkflowFixture();
        fixture.Module.Handler = async (_, _) =>
        {
            Assert.Throws<InvalidOperationException>(() => fixture.Runtime.StopAsync());
            Assert.Throws<InvalidOperationException>(() => fixture.Runtime.PumpJobs(1, TimeSpan.FromSeconds(1)));
            await Task.Yield();
            return await fixture.Module.Cpu.ExecuteAsync(2);
        };
        fixture.Module.BeforeCommand = () => Assert.Throws<InvalidOperationException>(() => fixture.Module.Port.Enqueue(1, out _, default));
        Assert.Equal(4, fixture.Wait(fixture.Module.Port.ExecuteAsync(0)));
        fixture.Module.BeforeCommand = null;
        Assert.Equal(6, fixture.Wait(fixture.Module.Cpu.ExecuteAsync(3)));
    }

    /// <summary>Cross-runtime child submissions fail before consuming the other runtime's capacity.</summary>
    [Fact]
    public void ChildSubmissionRequiresTheSameRuntime()
    {
        using var fixture = new WorkflowFixture();
        using var other = new WorkflowFixture(options: new ExecutionOptions { MaxInFlightRuns = 1, MaxCommands = 1 });
        fixture.Module.Handler = (_, _) =>
        {
            Assert.Throws<InvalidOperationException>(() => other.Module.Cpu.Enqueue(1, out _, default));
            Assert.Throws<InvalidOperationException>(() => other.Module.Port.Enqueue(1, out _, default));
            return ValueTask.FromResult(3);
        };

        Assert.Equal(3, fixture.Wait(fixture.Module.Port.ExecuteAsync(0)));
        Assert.Equal(4, other.Wait(other.Module.Cpu.ExecuteAsync(2)));
        Assert.Equal(5, other.Wait(other.Module.Port.ExecuteAsync(5)));
    }

    /// <summary>Previously accepted foreign results resume on the waiting workflow scheduler, including faults.</summary>
    /// <param name="workflow">Whether the foreign operation is a workflow.</param>
    /// <param name="fault">Whether the foreign operation fails.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ForeignResultReturnsToWorkflowScheduler(bool workflow, bool fault)
    {
        using var fixture = new WorkflowFixture();
        using var other = new WorkflowFixture();
        var error = new InvalidOperationException("Foreign failure.");
        other.Module.Handler = (value, _) => fault ? ValueTask.FromException<int>(error) : ValueTask.FromResult(value);
        other.Module.BeforeCommand = () =>
        {
            if (fault)
            {
                throw error;
            }
        };

        ValueTask<int> foreign = workflow ? other.Module.Port.ExecuteAsync(6) : other.Module.Cpu.ExecuteAsync(3);
        int resumed = 0;
        fixture.Module.Handler = async (_, _) =>
        {
            SynchronizationContext? context = SynchronizationContext.Current;
            try
            {
                return await foreign;
            }
            finally
            {
                Assert.Same(context, SynchronizationContext.Current);
                resumed++;
            }
        };

        ValueTask<int> result = fixture.Module.Port.ExecuteAsync(0);
        fixture.Runtime.PumpJobs(1, TimeSpan.FromSeconds(1));
        other.Pump();
        Assert.Equal(0, resumed);
        fixture.Until(() => result.IsCompleted);
        if (fault)
        {
            Assert.Same(error, Assert.Throws<InvalidOperationException>(() => CompletedResult.Read(result)));
        }
        else
        {
            Assert.Equal(6, CompletedResult.Read(result));
        }

        Assert.Equal(1, resumed);
    }

    private sealed class SingleResultSource : IValueTaskSource<int>
    {
        private ManualResetValueTaskSourceCore<int> _source;

        internal int Observations { get; private set; }

        public int GetResult(short token)
        {
            Observations++;
            Assert.Equal(1, Observations);
            return _source.GetResult(token);
        }

        public ValueTaskSourceStatus GetStatus(short token) => _source.GetStatus(token);

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => _source.OnCompleted(continuation, state, token, flags);

        internal void Complete(int result) => _source.SetResult(result);

        internal void Fail(Exception error) => _source.SetException(error);
    }
}
