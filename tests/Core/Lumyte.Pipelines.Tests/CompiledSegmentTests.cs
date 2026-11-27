using Lumyte.Execution;
using Xunit;

namespace Lumyte.Pipelines.Tests;

/// <summary>Exercises compiled logical stages through the public processor and runtime boundaries.</summary>
public sealed class CompiledSegmentTests
{
    /// <summary>Every external processor executes, and captured contexts and leases stay immutable.</summary>
    [Fact]
    public void All512StagesExecuteWithDistinctExpiredContexts()
    {
        var contexts = new List<StagePreparationContext>();
        var leases = new List<ValueLease<int>>();
        var stage = new Stage((context, input, token) =>
        {
            if (contexts.Count != 0)
            {
                Assert.Throws<ObjectDisposedException>(() => contexts[^1].Read(leases[^1]));
                if (contexts.Count > 1)
                {
                    Assert.Throws<InvalidOperationException>(() => context.Read(leases[0]));
                }
            }

            contexts.Add(context);
            ValueLease<int> published = context.Publish(context.Read(input) + 1);
            leases.Add(published);
            return ValueTask.FromResult(published);
        });
        var graph = new PipelineGraph<int>();
        Port<int> output = graph.Input;
        for (int i = 0; i < 512; i++)
        {
            output = output.Then(stage);
        }

        With(graph.Complete(output), (runtime, pipeline) =>
        {
            Task<PipelineResult<int>> pending = pipeline.PrepareAsync(3).AsTask();
            Assert.Equal(0, stage.Calls);
            Pump(runtime, pending);
            using PipelineResult<int> result = pending.Result;
            Assert.Equal(515, result.Value);
            Assert.Equal(512, stage.Calls);
            Assert.Equal(1024, stage.Descriptions);
            Assert.Equal(512, contexts.Distinct().Count());
            foreach (StagePreparationContext context in contexts)
            {
                Assert.Throws<ObjectDisposedException>(() => context.Publish(0));
            }
        });
    }

    /// <summary>Suspended independent segments can release one another without sequential-await deadlock.</summary>
    [Fact]
    public void IndependentAsyncBranchesReleaseEachOther()
    {
        var leftEntered = new TaskCompletionSource();
        var rightEntered = new TaskCompletionSource();
        var left = new Stage(async (context, input, token) =>
        {
            leftEntered.SetResult();
            await rightEntered.Task;
            return context.Publish(context.Read(input) + 1);
        });
        var right = new Stage(async (context, input, token) =>
        {
            rightEntered.SetResult();
            await leftEntered.Task;
            return context.Publish(context.Read(input) + 2);
        });
        Stage increment = Increment();
        var graph = new PipelineGraph<int>();
        Port<int> a = graph.Input.Then(left).Then(increment);
        Port<int> b = graph.Input.Then(right).Then(increment);
        With(graph.Complete(graph.Stage(new Sum(), a, b)), (runtime, pipeline) =>
        {
            Task<PipelineResult<int>> pending = pipeline.PrepareAsync(10).AsTask();
            Pump(runtime, pending);
            using PipelineResult<int> result = pending.Result;
            Assert.Equal(25, result.Value);
            Assert.Equal(2, increment.Calls);
        });
    }

    /// <summary>Cancellation inside one logical stage suppresses remaining descriptions and processors.</summary>
    [Fact]
    public void UserCancellationSuppressesUnstartedSegmentTail()
    {
        using var cancellation = new CancellationTokenSource();
        var first = new Stage((context, input, token) =>
        {
            cancellation.Cancel();
            return ValueTask.FromResult(context.Publish(context.Read(input)));
        });
        Stage tail = Increment();
        var graph = new PipelineGraph<int>();
        With(graph.Complete(graph.Input.Then(first).Then(tail)), (runtime, pipeline) =>
        {
            Task<PipelineResult<int>> pending = pipeline.PrepareAsync(1, cancellation.Token).AsTask();
            Assert.ThrowsAny<OperationCanceledException>(() => Pump(runtime, pending));
            Assert.Equal(1, first.Calls);
            Assert.Equal(0, tail.Calls);
            Assert.Equal(1, tail.Descriptions);
        });
    }

    /// <summary>Shutdown drains a started async processor but suppresses the remaining logical stages.</summary>
    [Fact]
    public void ShutdownDrainsAsyncStageAndSuppressesTail()
    {
        var gate = new TaskCompletionSource();
        var owner = new Owner();
        var first = new Stage(async (context, input, token) =>
        {
            ValueLease<int> output = context.PublishOwned(context.Read(input), owner);
            await gate.Task;
            return output;
        });
        Stage tail = Increment();
        var graph = new PipelineGraph<int>();
        With(graph.Complete(graph.Input.Then(first).Then(tail)), (runtime, pipeline) =>
        {
            Task<PipelineResult<int>> pending = pipeline.PrepareAsync(1).AsTask();
            PumpUntil(runtime, () => first.Calls == 1);
            Task stop = runtime.StopAsync(ShutdownMode.CancelPending).AsTask();
            Assert.False(pending.IsCompleted);
            Assert.Equal(0, owner.Disposals);
            gate.SetResult();
            Assert.ThrowsAny<OperationCanceledException>(() => Pump(runtime, pending));
            Pump(runtime, stop);
            Assert.Equal(1, owner.Disposals);
            Assert.Equal(0, tail.Calls);
            Assert.Equal(1, tail.Descriptions);
        });
    }

    /// <summary>Sibling failure suppresses a suspended segment's unstarted tail without abandoning its owner.</summary>
    [Fact]
    public void SiblingFailureDrainsStartedBranchBeforeCleanup()
    {
        var gate = new TaskCompletionSource();
        var owner = new Owner();
        var started = new Stage(async (context, input, token) =>
        {
            ValueLease<int> output = context.PublishOwned(context.Read(input), owner);
            await gate.Task;
            return output;
        });
        var failure = new Stage((context, input, token) => throw new InvalidOperationException("sibling"));
        Stage tail = Increment();
        var graph = new PipelineGraph<int>();
        Port<int> a = graph.Input.Then(started).Then(tail);
        Port<int> b = graph.Input.Then(failure);
        With(graph.Complete(graph.Stage(new Sum(), a, b)), (runtime, pipeline) =>
        {
            Task<PipelineResult<int>> pending = pipeline.PrepareAsync(1).AsTask();
            PumpUntil(runtime, () => failure.Calls == 1);
            Assert.False(pending.IsCompleted);
            Assert.Equal(0, owner.Disposals);
            gate.SetResult();
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => Pump(runtime, pending));
            Assert.Equal("sibling", error.Message);
            Assert.Equal(1, owner.Disposals);
            Assert.Equal(0, tail.Calls);
            Assert.Equal(1, tail.Descriptions);
        });
    }

    /// <summary>Cached scalar descriptions do not cache stateful factory answers or bypass actual validation.</summary>
    [Fact]
    public void ActualRequirementsAreStillDescribedAndValidatedEveryRun()
    {
        Stage stage = Increment();
        var graph = new PipelineGraph<int>();
        With(graph.Complete(graph.Input.Then(stage)), (runtime, pipeline) =>
        {
            Task<PipelineResult<int>> first = pipeline.PrepareAsync(1).AsTask();
            Pump(runtime, first);
            first.Result.Dispose();
            stage.Bytes = 101;
            Task<PipelineResult<int>> second = pipeline.PrepareAsync(1).AsTask();
            Assert.Throws<InvalidOperationException>(() => Pump(runtime, second));
            Assert.Equal(3, stage.Descriptions);
            Assert.Equal(1, stage.Calls);
        });
    }

    /// <summary>Both deep parallel chains and a wide reduction execute all 512 logical stages.</summary>
    /// <param name="branches">The number of independent branches.</param>
    /// <param name="length">The logical stages per branch.</param>
    [Theory]
    [InlineData(4, 127)]
    [InlineData(256, 1)]
    public void Branched512StagesExecute(int branches, int length)
    {
        Stage stage = Increment();
        var sum = new Sum();
        var graph = new PipelineGraph<int>();
        var ports = new List<Port<int>>();
        for (int i = 0; i < branches; i++)
        {
            Port<int> port = graph.Input;
            for (int j = 0; j < length; j++)
            {
                port = port.Then(stage);
            }

            ports.Add(port);
        }

        while (ports.Count > 1)
        {
            var next = new List<Port<int>>();
            for (int i = 0; i < ports.Count; i += 2)
            {
                next.Add(graph.Stage(sum, ports[i], ports[i + 1]));
            }

            ports = next;
        }

        With(graph.Complete(ports[0].Then(stage)), (runtime, pipeline) =>
        {
            Task<PipelineResult<int>> pending = pipeline.PrepareAsync(2).AsTask();
            Pump(runtime, pending);
            using PipelineResult<int> result = pending.Result;
            Assert.Equal((branches * (2 + length)) + 1, result.Value);
            Assert.Equal((branches * length) + 1, stage.Calls);
            Assert.Equal(512, stage.Calls + sum.Calls);
        });
    }

    /// <summary>Application metadata is read afresh and checked against the build reservation.</summary>
    /// <returns>The completed metadata validation checks.</returns>
    [Fact]
    public async Task DynamicInputMetadataRemainsLiveAsync()
    {
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        Pump(runtime, runtime.StartAsync().AsTask());
        var graph = new PipelineGraph<DynamicInput>();
        var stage = new DynamicStage();
        using var host = new PipelineBuildHost(runtime, new PipelineBudget(8, 4096, 2));
        using IPipeline<DynamicInput, int> pipeline = host.Build(graph.Complete(graph.Input.Then(stage)));
        foreach (int elements in new[] { 3, 7 })
        {
            Task<PipelineResult<int>> pending = pipeline.PrepareAsync(new DynamicInput(elements)).AsTask();
            Pump(runtime, pending);
            using PipelineResult<int> result = await pending;
            Assert.Equal(elements, result.Value);
        }

        Task<PipelineResult<int>> tooLarge = pipeline.PrepareAsync(new DynamicInput(101)).AsTask();
        Assert.Throws<InvalidOperationException>(() => Pump(runtime, tooLarge));
        Assert.Equal(new long[] { 3, 7, 101 }, stage.ActualElements);
        Pump(runtime, runtime.StopAsync().AsTask());
    }

    /// <summary>Mixed immediate and concurrently completed async roots are observed once before fan-in.</summary>
    [Fact]
    public void MixedAsyncRootsDrainConcurrentCompletions()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int entered = 0;
        var graph = new PipelineGraph<int>();
        var ports = new List<Port<int>>();
        for (int i = 0; i < 64; i++)
        {
            int ordinal = i;
            var stage = new Stage(async (context, input, token) =>
            {
                entered++;
                if ((ordinal & 1) != 0)
                {
                    await gate.Task.ConfigureAwait(false);
                }

                return context.Publish(context.Read(input) + ordinal);
            });
            ports.Add(graph.Input.Then(stage).Then(Increment()));
        }

        while (ports.Count > 1)
        {
            var next = new List<Port<int>>();
            for (int i = 0; i < ports.Count; i += 2)
            {
                next.Add(graph.Stage(new Sum(), ports[i], ports[i + 1]));
            }

            ports = next;
        }

        With(graph.Complete(ports[0]), (runtime, pipeline) =>
        {
            Task<PipelineResult<int>> pending = pipeline.PrepareAsync(2).AsTask();
            PumpUntil(runtime, () => entered == 64);
            Assert.False(pending.IsCompleted);
            gate.SetResult();
            Pump(runtime, pending);
            using PipelineResult<int> result = pending.Result;
            Assert.Equal((64 * 3) + 2016, result.Value);
        });
    }

    /// <summary>A long cooperative slice actually yields to another admitted runtime workflow.</summary>
    [Fact]
    public void CooperativeProgramYieldsToOtherRun() => VerifyYieldToOtherRun(CpuExecutionMode.Cooperative);

    /// <summary>A ThreadPool segment yields at logical stage boundaries even with only one CPU worker.</summary>
    [Fact]
    [Trait("Execution", "Integration")]
    public void ThreadPoolSegmentYieldsToOtherRun() => VerifyYieldToOtherRun(CpuExecutionMode.ThreadPool);

    /// <summary>Cooperative backend selection is deferred until actual admission.</summary>
    [Fact]
    public void BuildBeforePreparationSelectsCooperativeMode() => BuildBeforeRuntimePreparation(CpuExecutionMode.Cooperative);

    /// <summary>ThreadPool backend selection is deferred until actual admission.</summary>
    [Fact]
    [Trait("Execution", "Integration")]
    public void BuildBeforePreparationSelectsThreadPoolMode() => BuildBeforeRuntimePreparation(CpuExecutionMode.ThreadPool);

    /// <summary>Independent synchronous CPU branches still run concurrently on the runtime's ThreadPool backend.</summary>
    [Fact]
    [Trait("Execution", "Integration")]
    public void ThreadPoolBranchesRetainCpuParallelism()
    {
        using var entered = new CountdownEvent(2);
        var left = new Stage((context, input, token) =>
        {
            entered.Signal();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            return ValueTask.FromResult(context.Publish(context.Read(input) + 1));
        });
        var right = new Stage((context, input, token) =>
        {
            entered.Signal();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            return ValueTask.FromResult(context.Publish(context.Read(input) + 2));
        });
        var graph = new PipelineGraph<int>();
        Port<int> a = graph.Input.Then(left).Then(Increment());
        Port<int> b = graph.Input.Then(right).Then(Increment());
        With(
            graph.Complete(graph.Stage(new Sum(), a, b)),
            (runtime, pipeline) =>
        {
            Task<PipelineResult<int>> pending = pipeline.PrepareAsync(3).AsTask();
            Pump(runtime, pending);
            using PipelineResult<int> result = pending.Result;
            Assert.Equal(11, result.Value);
        },
            CpuExecutionMode.ThreadPool);
    }

    /// <summary>Explicit graph cancellation suppresses unstarted logical work and expires its checkpoint scope.</summary>
    [Fact]
    public void CheckpointScopeClosesAfterRequestCancelDrain()
    {
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        Pump(runtime, runtime.StartAsync().AsTask());
        var gate = new TaskCompletionSource();
        PipelineJobScope? saved = null;
        bool tail = false;
        Func<int, PipelineJobScope, CancellationToken, ValueTask<Unit>> action = async (input, scope, token) =>
        {
            saved = scope;
            Assert.True(scope.TryStartLogicalStage());
            await gate.Task;
            tail = scope.TryStartLogicalStage();
            return default;
        };
        JobGraph<int> graph = PipelineJobBridge.CreateCooperative(runtime, [action], [[]], 1024);
        int input = 1;
        Assert.Equal(CommandEnqueueStatus.Accepted, runtime.Schedule(graph, in input, out JobRun? run));
        PumpUntil(runtime, () => saved is not null);
        run!.RequestCancel();
        gate.SetResult();
        Assert.ThrowsAny<OperationCanceledException>(() => Pump(runtime, run.Completion));
        Assert.False(tail);
        Assert.Throws<ObjectDisposedException>(() => saved!.TryStartLogicalStage());
        run.Dispose();
        Pump(runtime, runtime.StopAsync().AsTask());
    }

    /// <summary>Logical metadata is reserved by the graph and remains charged through completed-run disposal.</summary>
    [Fact]
    public void CooperativeMetadataReservationIsRetainedUntilGraphDisposal()
    {
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions { MaxPayloadBytes = 4096 });
        Pump(runtime, runtime.StartAsync().AsTask());
        Func<int, PipelineJobScope, CancellationToken, ValueTask<Unit>> action = (input, scope, token) => default;
        JobGraph<int> graph = PipelineJobBridge.CreateCooperative(runtime, [action], [[]], 3072);
        int input = 1;
        Assert.Equal(CommandEnqueueStatus.Accepted, runtime.Schedule(graph, in input, out JobRun? first));
        Pump(runtime, first!.Completion);
        Assert.Equal(CommandEnqueueStatus.QueueFull, runtime.Schedule(graph, in input, out _));
        first.Dispose();
        Assert.Equal(CommandEnqueueStatus.Accepted, runtime.Schedule(graph, in input, out JobRun? second));
        Pump(runtime, second!.Completion);
        second.Dispose();
        Pump(runtime, runtime.StopAsync().AsTask());
    }

    /// <summary>Simultaneous async faults drain before cleanup and retain an original fault through cleanup retry.</summary>
    [Fact]
    public void SimultaneousAsyncFailuresAndCleanupRemainRetryable()
    {
        var gate = new TaskCompletionSource();
        var firstError = new OperationCanceledException("handler cancellation");
        var secondError = new InvalidOperationException("async primary");
        var firstOwner = new Owner { FailFirst = true };
        var secondOwner = new Owner();
        var canceled = new Stage(async (context, input, token) =>
        {
            context.PublishOwned(context.Read(input), firstOwner);
            await gate.Task;
            throw firstError;
        });
        var failed = new Stage(async (context, input, token) =>
        {
            context.PublishOwned(context.Read(input), secondOwner);
            await gate.Task;
            throw secondError;
        });
        Stage tail = Increment();
        var graph = new PipelineGraph<int>();
        Port<int> a = graph.Input.Then(canceled).Then(tail);
        Port<int> b = graph.Input.Then(failed).Then(tail);
        With(graph.Complete(graph.Stage(new Sum(), a, b)), (runtime, pipeline) =>
        {
            Task<PipelineResult<int>> pending = pipeline.PrepareAsync(1).AsTask();
            PumpUntil(runtime, () => canceled.Calls == 1 && failed.Calls == 1);
            Assert.Equal(0, firstOwner.Disposals);
            Assert.Equal(0, secondOwner.Disposals);
            gate.SetResult();
            AggregateException error = Assert.Throws<AggregateException>(() => Pump(runtime, pending));

            // Releasing the same gate does not order notification delivery.
            Assert.True(ReferenceEquals(firstError, error.InnerExceptions[0]) || ReferenceEquals(secondError, error.InnerExceptions[0]));
            Assert.Equal(0, tail.Calls);
            Assert.Equal(1, firstOwner.Disposals);
            Assert.Equal(1, secondOwner.Disposals);
            pipeline.Dispose();
            Assert.Equal(2, firstOwner.Disposals);
            Assert.Equal(1, secondOwner.Disposals);
        });
    }

    /// <summary>A 512-stage program reserves logical metadata before any processor or actual Describe invocation.</summary>
    [Fact]
    public void Logical512MetadataIsAdmittedBeforeExecution()
    {
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions { MaxPayloadBytes = 4096 });
        Pump(runtime, runtime.StartAsync().AsTask());
        Stage stage = Increment();
        var graph = new PipelineGraph<int>();
        Port<int> output = graph.Input;
        for (int i = 0; i < 512; i++)
        {
            output = output.Then(stage);
        }

        using var host = new PipelineBuildHost(runtime, new PipelineBudget(512, 100_000, 1));
        using IPipeline<int, int> pipeline = host.Build(graph.Complete(output));
        for (int i = 0; i < 2; i++)
        {
            Task<PipelineResult<int>> pending = pipeline.PrepareAsync(1).AsTask();
            Assert.Throws<InvalidOperationException>(() => Pump(runtime, pending));
            Assert.Equal(0, stage.Calls);
            Assert.Equal(512, stage.Descriptions);
        }

        Pump(runtime, runtime.StopAsync().AsTask());
    }

    /// <summary>Cooperative interpretation preserves the first handler exception, including a handler OCE.</summary>
    [Fact]
    public void CooperativeKeepsFirstHandlerCancellationFault() => VerifyFirstHandlerFault(CpuExecutionMode.Cooperative);

    /// <summary>ThreadPool graph execution preserves the identical first handler OCE instance.</summary>
    [Fact]
    [Trait("Execution", "Integration")]
    public void ThreadPoolKeepsFirstHandlerCancellationFault() => VerifyFirstHandlerFault(CpuExecutionMode.ThreadPool);

    private static void VerifyFirstHandlerFault(CpuExecutionMode mode)
    {
        var secondGate = new TaskCompletionSource();
        var firstError = new OperationCanceledException("first handler fault, not token cancellation");
        var secondError = new InvalidOperationException("second handler fault");
        var second = new Stage(async (context, input, token) =>
        {
            await secondGate.Task;
            throw secondError;
        });
        var first = new Stage((context, input, token) =>
        {
            Assert.Equal(1, second.Calls);
            return ValueTask.FromException<ValueLease<int>>(firstError);
        });
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, mode, 1, new ExecutionOptions());
        Pump(runtime, runtime.StartAsync().AsTask());
        var graph = new PipelineGraph<int>();

        // B suspends first. A then faults synchronously on the sole CPU worker.
        // B's captured continuation cannot dispatch until A's fault is observed.
        Port<int> b = graph.Input.Then(second);
        Port<int> a = graph.Input.Then(first);
        using var host = new PipelineBuildHost(runtime, new PipelineBudget(8, 4096, 2));
        using IPipeline<int, int> pipeline = host.Build(graph.Complete(graph.Stage(new Sum(), a, b)));
        Task<PipelineResult<int>> pending = pipeline.PrepareAsync(1).AsTask();
        PumpUntil(runtime, () => first.Calls == 1 && second.Calls == 1);
        Assert.False(pending.IsCompleted);
        secondGate.SetResult();
        try
        {
            OperationCanceledException actual = Assert.ThrowsAny<OperationCanceledException>(() => Pump(runtime, pending));
            Assert.Same(firstError, actual);
        }
        finally
        {
            Pump(runtime, runtime.StopAsync().AsTask());
        }
    }

    private static void VerifyYieldToOtherRun(CpuExecutionMode mode)
    {
        int calls = 0;
        Stage other = Increment();
        var stage = new Stage((context, input, token) =>
        {
            if (++calls == 1)
            {
                var deadline = System.Diagnostics.Stopwatch.StartNew();
                while (!ExecutionRuntime.ShouldYield(TimeSpan.FromMicroseconds(250)))
                {
                    if (deadline.Elapsed > TimeSpan.FromSeconds(10))
                    {
                        throw new TimeoutException("The handler did not enter a runtime workflow slice.");
                    }

                    Thread.SpinWait(16);
                }
            }
            else
            {
                Assert.Equal(1, other.Calls);
            }

            return ValueTask.FromResult(context.Publish(context.Read(input) + 1));
        });
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, mode, 1, new ExecutionOptions());
        Pump(runtime, runtime.StartAsync().AsTask());
        var graph = new PipelineGraph<int>();
        var secondGraph = new PipelineGraph<int>();
        using var host = new PipelineBuildHost(runtime, new PipelineBudget(8, 4096, 2));
        using IPipeline<int, int> pipeline = host.Build(graph.Complete(graph.Input.Then(stage).Then(stage)));
        using IPipeline<int, int> second = host.Build(secondGraph.Complete(secondGraph.Input.Then(other)));
        Task<PipelineResult<int>> first = pipeline.PrepareAsync(1).AsTask();
        Task<PipelineResult<int>> next = second.PrepareAsync(1).AsTask();
        Pump(runtime, Task.WhenAll(first, next));
        first.Result.Dispose();
        next.Result.Dispose();
        Assert.Equal(2, calls);
        Pump(runtime, runtime.StopAsync().AsTask());
    }

    private static void BuildBeforeRuntimePreparation(CpuExecutionMode mode)
    {
        var runtime = new ExecutionRuntime([]);
        var graph = new PipelineGraph<int>();
        using var host = new PipelineBuildHost(runtime, new PipelineBudget(8, 4096, 2));
        using IPipeline<int, int> pipeline = host.Build(graph.Complete(graph.Input.Then(Increment())));
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, mode, mode == CpuExecutionMode.Cooperative ? 1 : 2, new ExecutionOptions());
        Pump(runtime, runtime.StartAsync().AsTask());
        Task<PipelineResult<int>> pending = pipeline.PrepareAsync(4).AsTask();
        Pump(runtime, pending);
        using (PipelineResult<int> result = CompletedResult(pending))
        {
            Assert.Equal(5, result.Value);
        }

        Pump(runtime, runtime.StopAsync().AsTask());
    }

    private static PipelineResult<int> CompletedResult(Task<PipelineResult<int>> pending) => pending.GetAwaiter().GetResult();

    private static Stage Increment() => new((context, input, token) => ValueTask.FromResult(context.Publish(context.Read(input) + 1)));

    private static void With(PipelineDefinition<int, int> definition, Action<ExecutionRuntime, IPipeline<int, int>> action, CpuExecutionMode mode = CpuExecutionMode.Cooperative)
    {
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, mode, mode == CpuExecutionMode.Cooperative ? 1 : 2, new ExecutionOptions());
        Pump(runtime, runtime.StartAsync().AsTask());
        using var host = new PipelineBuildHost(runtime, new PipelineBudget(512, 100_000, 2));
        using IPipeline<int, int> pipeline = host.Build(definition);
        try
        {
            action(runtime, pipeline);
        }
        finally
        {
            if (runtime.Status != ExecutionStatus.Stopped)
            {
                Pump(runtime, runtime.StopAsync().AsTask());
            }
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

    private sealed class Stage(Func<StagePreparationContext, ValueLease<int>, CancellationToken, ValueTask<ValueLease<int>>> invoke) : IStage<int, int>, IStageFactory<int, int>, IStageProcessor<int, int>
    {
        public IStageFactory<int, int> Factory => this;

        internal int Calls { get; private set; }

        internal int Descriptions { get; private set; }

        internal long Bytes { get; set; } = 100;

        public StageRequirements Describe(StageInputDescriptions inputs)
        {
            Descriptions++;
            return new StageRequirements(Bytes, []);
        }

        public IStageProcessor<int, int> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<int>> PrepareAsync(StagePreparationContext context, ValueLease<int> a, CancellationToken cancellationToken)
        {
            Calls++;
            return invoke(context, a, cancellationToken);
        }

        public void Dispose()
        {
        }
    }

    private sealed class Sum : IStage<int, int, int>, IStageFactory<int, int, int>, IStageProcessor<int, int, int>
    {
        private int _calls;

        public IStageFactory<int, int, int> Factory => this;

        internal int Calls => _calls;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(100, []);

        public IStageProcessor<int, int, int> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<int>> PrepareAsync(StagePreparationContext context, ValueLease<int> a, ValueLease<int> b, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return ValueTask.FromResult(context.Publish(context.Read(a) + context.Read(b)));
        }

        public void Dispose()
        {
        }
    }

    private sealed class DynamicInput(int elements) : IImmutablePipelineInput, ICpuPipelineValue
    {
        public SchemaExtent Description { get; } = new(elements, new Dictionary<string, object>());
    }

    private sealed class DynamicStage : IStage<DynamicInput, int>, IStageFactory<DynamicInput, int>, IStageProcessor<DynamicInput, int>
    {
        public IStageFactory<DynamicInput, int> Factory => this;

        internal List<long> ActualElements { get; } = [];

        public StageRequirements Describe(StageInputDescriptions inputs)
        {
            SchemaExtent extent = inputs.Extents[0];
            if (extent.Metadata.ContainsKey("UpperBound"))
            {
                return new StageRequirements(100, []);
            }

            ActualElements.Add(extent.ElementCount);
            return new StageRequirements(68 + extent.ElementCount, []);
        }

        public IStageProcessor<DynamicInput, int> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<int>> PrepareAsync(StagePreparationContext context, ValueLease<DynamicInput> a, CancellationToken cancellationToken)
            => ValueTask.FromResult(context.Publish((int)context.Read(a).Description.ElementCount));

        public void Dispose()
        {
        }
    }

    private sealed class Owner : IDisposable
    {
        internal int Disposals { get; private set; }

        internal bool FailFirst { get; init; }

        public void Dispose()
        {
            Disposals++;
            if (FailFirst && Disposals == 1)
            {
                throw new InvalidOperationException("cleanup");
            }
        }
    }
}
