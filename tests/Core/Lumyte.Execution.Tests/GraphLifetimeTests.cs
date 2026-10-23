using System.Diagnostics;
using Xunit;

namespace Lumyte.Execution.Tests;

/// <summary>Checks graph lifetime accounting independently of workers and retained results.</summary>
public sealed class GraphLifetimeTests
{
    /// <summary>Canceling queued and dormant work cannot finish shutdown while a range handler is running.</summary>
    /// <param name="cancelRun">Whether cancellation is requested before shutdown.</param>
    /// <returns>The completed lifetime check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShutdownWaitsForRunningRangeAfterCancellationAsync(bool cancelRun)
    {
        using var release = new ManualResetEventSlim();
        var module = new BlockingRangeModule(release);
        var runtime = new ExecutionRuntime([module]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Dedicated, CpuExecutionMode.ThreadPool, 2, new ExecutionOptions());
        CompletedResult.Read(runtime.StartAsync());
        var builder = new JobGraphBuilder<int>();
        builder.Add("WaitingOwner", module.OwnerWork);
        JobNodeId range = builder.AddRange("Range", module.Range, 1);
        JobNodeId dependent = builder.Add("Dependent", module.CpuWork);
        builder.DependsOn(dependent, range);
        int input = 0;
        Assert.True(runtime.TrySchedule(builder.Build(), in input, out JobRun? run));
        Task? stop = null;
        try
        {
            await module.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await module.OtherChunkFinished.Task.WaitAsync(TimeSpan.FromSeconds(30));
            if (cancelRun)
            {
                run!.RequestCancel();
            }

            await runtime.Bind(module.CpuWork).ExecuteAsync(input).AsTask().WaitAsync(TimeSpan.FromSeconds(30));
            stop = runtime.StopAsync(ShutdownMode.CancelPending).AsTask();
            Assert.False(run!.IsCompleted);
            Assert.False(stop.IsCompleted);
            Assert.False(module.Stopped.Task.IsCompleted);
        }
        finally
        {
            release.Set();
            stop ??= runtime.StopAsync(ShutdownMode.CancelPending).AsTask();
            await stop.WaitAsync(TimeSpan.FromSeconds(30));
        }

        Assert.True(run!.IsCompleted);
        Assert.Throws<OperationCanceledException>(() => run.ThrowIfFailed());
        Assert.Equal(1, module.CpuExecutions);
        Assert.Equal(0, module.OwnerExecutions);
        Assert.True(module.Stopped.Task.IsCompletedSuccessfully);
        Assert.Equal(ExecutionStatus.Stopped, runtime.Status);
        run.Dispose();
    }

    /// <summary>Graph capacity remains reserved until disposal, but shutdown only waits for execution.</summary>
    [Fact]
    public void CompletedRunRetainsCapacityWithoutBlockingShutdown()
    {
        var module = new GraphModule();
        var runtime = new ExecutionRuntime([module]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions { MaxCommands = 1 });
        CompletedResult.Read(runtime.StartAsync());
        var builder = new JobGraphBuilder<GraphInput>();
        builder.AddRange("Range", module.Range, 1);
        JobGraph<GraphInput> graph = builder.Build();
        var input = new GraphInput(new GraphData(3));
        CommandPort<GraphInput, Unit> port = runtime.Bind(module.Prepare);
        Assert.True(runtime.TrySchedule(graph, in input, out JobRun? first));
        PumpUntil(runtime, module, () => first!.IsCompleted);
        first!.ThrowIfFailed();
        Assert.Equal(CommandEnqueueStatus.QueueFull, port.TryEnqueue(in input, default, out _));
        Assert.False(runtime.TrySchedule(graph, in input, out _));

        first.Dispose();
        ValueTask<Unit> command = port.ExecuteAsync(input);
        PumpUntil(runtime, module, () => command.IsCompleted);
        CompletedResult.Read(command);
        Assert.True(runtime.TrySchedule(graph, in input, out JobRun? retained));
        PumpUntil(runtime, module, () => retained!.IsCompleted);
        Task stop = runtime.StopAsync(ShutdownMode.Drain).AsTask();
        PumpUntil(runtime, module, () => stop.IsCompleted);
        CompletedResult.Read(stop);
        Assert.Equal(ExecutionStatus.Stopped, runtime.Status);
        retained!.ThrowIfFailed();
        retained.Dispose();
    }

    /// <summary>Bounded host pumping drains mixed graph outcomes and ordinary commands exactly once.</summary>
    [Fact]
    public void BoundedHostPumpDrainsMixedGraphsAndCommands()
    {
        var module = new GraphModule();
        var runtime = new ExecutionRuntime([module]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        CompletedResult.Read(runtime.StartAsync());
        module.Gate!.Open();
        var builder = new JobGraphBuilder<GraphInput>();
        JobNodeId prepare = builder.Add("Prepare", module.Prepare);
        JobNodeId range = builder.AddRange("Range", module.Range, 1);
        JobNodeId apply = builder.Add("Apply", module.Apply);
        builder.DependsOn(range, prepare);
        builder.DependsOn(apply, range);
        JobGraph<GraphInput> graph = builder.Build();
        GraphData[] data = [new(5), new(0), new(3) { FailPrepare = true }, new(3)];
        var runs = new JobRun[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            var input = new GraphInput(data[i]);
            Assert.True(runtime.TrySchedule(graph, in input, out JobRun? run));
            runs[i] = run!;
        }

        runs[3].RequestCancel();
        var independent = new GraphData(0);
        ValueTask<Unit> command = runtime.Bind(module.Prepare).ExecuteAsync(new GraphInput(independent));
        Task stop = runtime.StopAsync(ShutdownMode.Drain).AsTask();
        Assert.False(stop.IsCompleted);
        PumpUntil(runtime, module, () => stop.IsCompleted);
        CompletedResult.Read(stop);
        CompletedResult.Read(command);
        Assert.All(runs, run => Assert.True(run.IsCompleted));
        runs[0].ThrowIfFailed();
        runs[1].ThrowIfFailed();
        Assert.Throws<InvalidOperationException>(() => runs[2].ThrowIfFailed());
        Assert.Throws<OperationCanceledException>(() => runs[3].ThrowIfFailed());
        Assert.Equal(15, data[0].Total);
        Assert.Equal(["prepare", "apply"], data[1].Events);
        Assert.Empty(data[2].Events);
        Assert.Empty(data[3].Events);
        Assert.Equal(["prepare"], independent.Events);
        Assert.Equal(ExecutionStatus.Stopped, runtime.Status);
        foreach (JobRun run in runs)
        {
            run.Dispose();
        }
    }

    private static void PumpUntil(ExecutionRuntime runtime, GraphModule module, Func<bool> done)
    {
        long start = Stopwatch.GetTimestamp();
        while (!done())
        {
            Assert.InRange(runtime.PumpHostLifecycle(1, TimeSpan.FromSeconds(1)), 0, 1);
            Assert.InRange(runtime.PumpJobs(1, TimeSpan.FromSeconds(1)), 0, 1);
            Assert.InRange(runtime.RunHostPhase(module.Phase, 1, TimeSpan.FromSeconds(1)), 0, 1);
            if (Stopwatch.GetElapsedTime(start) > TimeSpan.FromSeconds(30))
            {
                throw new TimeoutException("Graph lifetime did not complete within the host pump budget.");
            }

            Thread.Yield();
        }
    }

    private sealed class BlockingRangeModule(ManualResetEventSlim release) : IExecutionModule, IRangeWorkHandler<int>
    {
        private OwnerKey _owner;
        private int _cpuExecutions;
        private int _ownerExecutions;

        public string Name => "BlockingRange";

        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource OtherChunkFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal WorkKey<int, Unit> OwnerWork { get; private set; }

        internal WorkKey<int, Unit> CpuWork { get; private set; }

        internal RangeWorkKey<int> Range { get; private set; }

        internal int CpuExecutions => Volatile.Read(ref _cpuExecutions);

        internal int OwnerExecutions => Volatile.Read(ref _ownerExecutions);

        public void Declare(ModuleBuilder builder)
        {
            _owner = builder.Owner("Owner", OwnerRequirement.FixedThread);
            OrderGroupKey group = builder.OrderGroup("OwnerGroup", _owner);
            GateKey gate = builder.Gate("Closed");
            OwnerWork = builder.Command<int, Unit, Handler>("OwnerCommand", new Handler(this, true), new WorkOptions { Target = WorkTarget.On(_owner), OrderGroup = group, RequiredGates = new GateKey[] { gate } });
            CpuWork = builder.Command<int, Unit, Handler>("Cpu", new Handler(this, false), new WorkOptions { Target = WorkTarget.Cpu });
            Range = builder.Range<int, BlockingRangeModule>("Range", this);
        }

        public void Configure(ExecutionPlanBuilder builder, OwnerPlacement placement)
            => builder.BindDedicated(_owner, builder.DedicatedThread("Owner"));

        public ValueTask StartAsync(ModuleContext context, CancellationToken token) => ValueTask.CompletedTask;

        public ValueTask StopAsync(ModuleContext context)
        {
            Stopped.SetResult();
            return ValueTask.CompletedTask;
        }

        public int GetLength(in int context) => 2;

        public void Execute(in int context, int first, int endExclusive)
        {
            if (first == 0)
            {
                Started.SetResult();
                if (!release.Wait(TimeSpan.FromSeconds(60)))
                {
                    throw new TimeoutException("The running range handler was not released.");
                }
            }
            else
            {
                OtherChunkFinished.SetResult();
            }
        }

        private readonly struct Handler(BlockingRangeModule module, bool owner) : ICommandHandler<int, Unit>
        {
            public Unit Execute(in int command, in WorkContext context)
            {
                context.VerifyAccess();
                if (owner)
                {
                    Interlocked.Increment(ref module._ownerExecutions);
                }
                else
                {
                    Interlocked.Increment(ref module._cpuExecutions);
                }

                return default;
            }
        }
    }
}
