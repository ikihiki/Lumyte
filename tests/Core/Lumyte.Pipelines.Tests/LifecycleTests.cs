using Lumyte.Execution;
using Xunit;

namespace Lumyte.Pipelines.Tests;

/// <summary>Exercises overlapping runs, real terminal cancellation, and cleanup recovery.</summary>
public sealed class LifecycleTests
{
    /// <summary>Two overlapping invocations retain independent values and bounded admission.</summary>
    [Fact]
    public void ConcurrentRunsAreIndependent()
    {
        var gate = new TaskCompletionSource();
        var stage = new Stage(async (context, input, token) =>
        {
            int value = context.Read(input);
            await gate.Task;
            return context.Publish(value * 2);
        });
        With(stage, (runtime, pipeline) =>
        {
            Task<PipelineResult<int>> first = pipeline.PrepareAsync(3).AsTask();
            Task<PipelineResult<int>> second = pipeline.PrepareAsync(7).AsTask();
            PumpUntil(runtime, () => stage.Calls == 2);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => pipeline.PrepareAsync(9).GetAwaiter().GetResult());
            gate.SetResult();
            Pump(runtime, Task.WhenAll(first, second));
            pipeline.Dispose();
            Assert.Equal(6, first.Result.Value);
            Assert.Equal(14, second.Result.Value);
            first.Result.Dispose();
            Assert.Equal(0, stage.Disposals);
            second.Result.Dispose();
            Assert.Equal(1, stage.Disposals);
        });
    }

    /// <summary>Cancellation never returns published ownership while an async processor still runs.</summary>
    [Fact]
    public void CancellationAwaitsActualTerminalState()
    {
        var gate = new TaskCompletionSource();
        var owner = new Owner();
        var stage = new Stage(async (context, input, token) =>
        {
            ValueLease<int> value = context.PublishOwned(context.Read(input), owner);
            await gate.Task;
            return value;
        });
        With(stage, (runtime, pipeline) =>
        {
            using var cancellation = new CancellationTokenSource();
            Task<PipelineResult<int>> pending = pipeline.PrepareAsync(3, cancellation.Token).AsTask();
            PumpUntil(runtime, () => stage.Calls == 1);
            cancellation.Cancel();
            for (int i = 0; i < 4; i++)
            {
                runtime.PumpJobs(64, TimeSpan.FromMilliseconds(2));
            }

            Assert.False(pending.IsCompleted);
            Assert.Equal(0, owner.Calls);
            gate.SetResult();
            Assert.ThrowsAny<OperationCanceledException>(() => Pump(runtime, pending));
            Assert.Equal(1, owner.Calls);
        });
    }

    /// <summary>Queued cancellation requires pumping and never enters the processor.</summary>
    [Fact]
    public void QueuedCancellationDoesNotCompleteWithoutPump()
    {
        var stage = new Stage((context, input, token) => ValueTask.FromResult(context.Publish(context.Read(input))));
        With(stage, (runtime, pipeline) =>
        {
            using var cancellation = new CancellationTokenSource();
            Task<PipelineResult<int>> pending = pipeline.PrepareAsync(1, cancellation.Token).AsTask();
            cancellation.Cancel();
            Assert.False(pending.IsCompleted);
            Assert.ThrowsAny<OperationCanceledException>(() => Pump(runtime, pending));
            Assert.Equal(0, stage.Calls);
        });
    }

    /// <summary>Primary and cleanup failures retain the failed owner for retry.</summary>
    [Fact]
    public void FailureAndCleanupFailureRemainRetryable()
    {
        var first = new Owner { FailFirst = true };
        var second = new Owner();
        var a = new Stage((context, input, token) => ValueTask.FromResult(context.PublishOwned(context.Read(input), first)));
        var b = new Stage((context, input, token) =>
        {
            context.PublishOwned(context.Read(input), second);
            throw new InvalidOperationException("primary");
        });
        With(
            a,
            (runtime, pipeline) =>
        {
            Task<PipelineResult<int>> pending = pipeline.PrepareAsync(1).AsTask();
            AggregateException error = Assert.Throws<AggregateException>(() => Pump(runtime, pending));
            Assert.Contains(error.InnerExceptions, exception => exception.Message == "primary");
            Assert.Equal(1, second.Calls);
            Assert.Equal(1, first.Calls);
            pipeline.Dispose();
            pipeline.Dispose();
            Assert.Equal(1, second.Calls);
            Assert.Equal(2, first.Calls);
        },
            b);
    }

    /// <summary>A later reservation failure cleans earlier owners before returning failure.</summary>
    [Fact]
    public void LaterBudgetFailureCleansEarlierOutput()
    {
        var owner = new Owner();
        var a = new Stage((context, input, token) => ValueTask.FromResult(context.PublishOwned(context.Read(input), owner)), 100);
        var b = new Stage((context, input, token) => ValueTask.FromResult(context.Publish(context.Read(input))), 100, minimum: 0);
        With(
            a,
            (runtime, pipeline) =>
        {
            Task<PipelineResult<int>> pending = pipeline.PrepareAsync(1).AsTask();
            Assert.Throws<InvalidOperationException>(() => Pump(runtime, pending));
            Assert.Equal(1, owner.Calls);
            Assert.Equal(0, b.Calls);
        },
            b,
            bytes: 150);
    }

    /// <summary>Overflow and under-declared lease metadata fail before leaking ownership.</summary>
    [Fact]
    public void OverflowAndUnderDeclarationAreRejected()
    {
        var runtime = new ExecutionRuntime([]);
        var a = new Stage((context, input, token) => ValueTask.FromResult(context.Publish(0)), long.MaxValue);
        var graph = new PipelineGraph<int>();
        using var host = new PipelineBuildHost(runtime, new PipelineBudget(8, long.MaxValue, 2));
        Assert.Throws<OverflowException>(() => host.Build(graph.Complete(graph.Input.Then(a).Then(a))));
        var small = new Stage((context, input, token) => ValueTask.FromResult(context.Publish(0)), 0);
        With(small, (execution, pipeline) =>
        {
            Task<PipelineResult<int>> pending = pipeline.PrepareAsync(1).AsTask();
            Assert.Throws<InvalidOperationException>(() => Pump(execution, pending));
        });
    }

    /// <summary>Saved leases and contexts cannot escape their invocation.</summary>
    [Fact]
    public void ForeignLeaseAndExpiredContextAreRejected()
    {
        ValueLease<int>? saved = null;
        StagePreparationContext? previous = null;
        var stage = new Stage((context, input, token) =>
        {
            if (saved is not null)
            {
                Assert.Throws<InvalidOperationException>(() => context.Read(saved));
                Assert.Throws<ObjectDisposedException>(() => previous!.Read(saved));
                return ValueTask.FromResult(saved);
            }

            previous = context;
            saved = context.Publish(context.Read(input));
            return ValueTask.FromResult(saved);
        });
        With(stage, (runtime, pipeline) =>
        {
            Task<PipelineResult<int>> first = pipeline.PrepareAsync(1).AsTask();
            Pump(runtime, first);
            using PipelineResult<int> result = first.Result;
            Task<PipelineResult<int>> second = pipeline.PrepareAsync(2).AsTask();
            Assert.Throws<InvalidOperationException>(() => Pump(runtime, second));
            Assert.Equal(1, result.Value);
        });
    }

    /// <summary>Public CPU bridge rejects self cycles and forward edges.</summary>
    [Fact]
    public void LoweredCyclesAreRejected()
    {
        var runtime = new ExecutionRuntime([]);
        Func<int, CancellationToken, ValueTask<Unit>> action = (input, token) => ValueTask.FromResult(default(Unit));
        Assert.Throws<ArgumentException>(() => PipelineJobBridge.Create(runtime, [action], [[0]]));
        Assert.Throws<ArgumentException>(() => PipelineJobBridge.Create(runtime, [action, action], [[1], []]));
    }

    /// <summary>Bridge workflows retain graph capacity until disposal and drain running work before shutdown.</summary>
    [Fact]
    public void BridgeAccountingAndShutdownDrain()
    {
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions { MaxCommands = 1 });
        Pump(runtime, runtime.StartAsync().AsTask());
        var gate = new TaskCompletionSource();
        bool started = false;
        Func<int, CancellationToken, ValueTask<Unit>> action = async (input, token) =>
        {
            started = true;
            await gate.Task;
            return default;
        };
        JobGraph<int> graph = PipelineJobBridge.Create(runtime, [action], [[]]);
        int input = 1;
        Assert.Equal(CommandEnqueueStatus.Accepted, runtime.Schedule(graph, in input, out JobRun? first));
        PumpUntil(runtime, () => started);
        Assert.Equal(CommandEnqueueStatus.QueueFull, runtime.Schedule(graph, in input, out _));
        gate.SetResult();
        Pump(runtime, first!.Completion);
        Assert.Equal(CommandEnqueueStatus.QueueFull, runtime.Schedule(graph, in input, out _));
        first.Dispose();
        first.Dispose();
        gate = new TaskCompletionSource();
        started = false;
        Assert.Equal(CommandEnqueueStatus.Accepted, runtime.Schedule(graph, in input, out JobRun? second));
        PumpUntil(runtime, () => started);
        Task stop = runtime.StopAsync(ShutdownMode.CancelPending).AsTask();
        Assert.False(second!.IsCompleted);
        Assert.False(stop.IsCompleted);
        gate.SetResult();
        Pump(runtime, stop);
        Assert.True(second.IsCompleted);
        Assert.Equal(ExecutionStatus.Stopped, runtime.Status);
        second.Dispose();
        second.Dispose();
    }

    private static void With(Stage stage, Action<ExecutionRuntime, IPipeline<int, int>> action, Stage? next = null, long bytes = 4096)
    {
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        Pump(runtime, runtime.StartAsync().AsTask());
        var graph = new PipelineGraph<int>();
        Port<int> output = graph.Input.Then(stage);
        if (next is not null)
        {
            output = output.Then(next);
        }

        using var host = new PipelineBuildHost(runtime, new PipelineBudget(8, bytes, 2));
        using IPipeline<int, int> pipeline = host.Build(graph.Complete(output));
        try
        {
            action(runtime, pipeline);
        }
        finally
        {
            Pump(runtime, runtime.StopAsync().AsTask());
        }
    }

    private static void Pump(ExecutionRuntime runtime, Task task)
    {
        PumpUntil(runtime, () => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    private static void PumpUntil(ExecutionRuntime runtime, Func<bool> done)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!done())
        {
            runtime.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
            runtime.PumpJobs(64, TimeSpan.FromMilliseconds(2));
            if (timer.Elapsed > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException();
            }

            Thread.Yield();
        }
    }

    private sealed class Stage(Func<StagePreparationContext, ValueLease<int>, CancellationToken, ValueTask<ValueLease<int>>> invoke, long bytes = 100, long? minimum = null) : IStage<int, int>, IStageFactory<int, int>, IStageProcessor<int, int>
    {
        public IStageFactory<int, int> Factory => this;

        internal int Calls { get; private set; }

        internal int Disposals { get; private set; }

        public StageRequirements Describe(StageInputDescriptions inputs)
            => new(inputs.Extents[0].Metadata.ContainsKey("LowerBound") ? minimum ?? bytes : bytes, []);

        public IStageProcessor<int, int> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<int>> PrepareAsync(StagePreparationContext context, ValueLease<int> a, CancellationToken cancellationToken)
        {
            Calls++;
            return invoke(context, a, cancellationToken);
        }

        public void Dispose() => Disposals++;
    }

    private sealed class Owner : IDisposable
    {
        internal bool FailFirst { get; init; }

        internal int Calls { get; private set; }

        public void Dispose()
        {
            Calls++;
            if (FailFirst && Calls == 1)
            {
                throw new InvalidOperationException("cleanup");
            }
        }
    }
}
