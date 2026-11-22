using Lumyte.Execution;
using Xunit;

namespace Lumyte.Pipelines.Tests;

/// <summary>Checks CPU publication ownership, retry and bounded lease metadata.</summary>
public sealed class OwnedOutputTests
{
    /// <summary>Owned output cleanup happens exactly once after the result returns.</summary>
    [Fact]
    public void ResultOwnsCpuOutputLease()
    {
        var owner = new Counter();
        Run(new Owned(owner), (runtime, pipeline) =>
        {
            PipelineResult<int> result = Prepare(runtime, pipeline);
            Assert.Equal(0, owner.Returns);
            Assert.Equal(12, result.Value);
            result.Dispose();
            result.Dispose();
            Assert.Equal(1, owner.Returns);
            Assert.Throws<ObjectDisposedException>(() => result.Value);
        });
    }

    /// <summary>Cleanup failure closes reads but leaves the same result available for retry.</summary>
    [Fact]
    public void FailedCleanupIsRetriedWithoutLosingOwnership()
    {
        var owner = new Counter { FailFirst = true };
        Run(new Owned(owner), (runtime, pipeline) =>
        {
            PipelineResult<int> result = Prepare(runtime, pipeline);
            Assert.Throws<InvalidOperationException>(result.Dispose);
            Assert.Throws<ObjectDisposedException>(() => result.Value);
            Assert.Equal(0, owner.Returns);
            result.Dispose();
            Assert.Equal(1, owner.Returns);
        });
    }

    /// <summary>One invocation cannot create an unbounded number of passive output leases.</summary>
    [Fact]
    public void SecondPublicationRejected()
        => Run(new Owned(new Counter(), twice: true), (runtime, pipeline) =>
        {
            Task<PipelineResult<int>> pending = pipeline.PrepareAsync(6).AsTask();
            Assert.Throws<InvalidOperationException>(() => Pump(runtime, pending));
        });

    private static PipelineResult<int> Prepare(ExecutionRuntime runtime, IPipeline<int, int> pipeline)
    {
        Task<PipelineResult<int>> pending = pipeline.PrepareAsync(6).AsTask();
        Pump(runtime, pending);
        return pending.GetAwaiter().GetResult();
    }

    private static void Run(Owned stage, Action<ExecutionRuntime, IPipeline<int, int>> action)
    {
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        Pump(runtime, runtime.StartAsync().AsTask());
        var graph = new PipelineGraph<int>();
        using var build = new PipelineBuildHost(runtime, new PipelineBudget(8, 4096, 1));
        using IPipeline<int, int> pipeline = build.Build(graph.Complete(graph.Stage(stage, graph.Input)));
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
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            runtime.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
            runtime.PumpJobs(64, TimeSpan.FromMilliseconds(2));
            if (timeout.Elapsed > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException();
            }

            Thread.Yield();
        }

        task.GetAwaiter().GetResult();
    }

    private sealed class Owned(Counter owner, bool twice = false) : IStage<int, int>, IStageFactory<int, int>, IStageProcessor<int, int>
    {
        public IStageFactory<int, int> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(1024, []);

        public IStageProcessor<int, int> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<int>> PrepareAsync(StagePreparationContext context, ValueLease<int> a, CancellationToken cancellationToken)
        {
            ValueLease<int> value = context.PublishOwned(context.Read(a) * 2, owner);
            return ValueTask.FromResult(twice ? context.Publish(0) : value);
        }

        public void Dispose()
        {
        }
    }

    private sealed class Counter : IDisposable
    {
        private bool _failed;
        private bool _returned;

        internal bool FailFirst { get; init; }

        internal int Returns { get; private set; }

        public void Dispose()
        {
            if (FailFirst && !_failed)
            {
                _failed = true;
                throw new InvalidOperationException("Injected output cleanup failure.");
            }

            if (!_returned)
            {
                Returns++;
                _returned = true;
            }
        }
    }
}
