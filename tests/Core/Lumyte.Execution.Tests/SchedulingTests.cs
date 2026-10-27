using Xunit;

namespace Lumyte.Execution.Tests;

/// <summary>Checks worker admission and completion accounting under concurrent execution.</summary>
public sealed class SchedulingTests
{
    /// <summary>A new CPU command uses an available worker while earlier work is still executing.</summary>
    /// <returns>The completed concurrency check.</returns>
    [Fact]
    public async Task NewCpuCommandUsesAvailableConcurrencyAsync()
    {
        using var release = new ManualResetEventSlim();
        var module = new CpuModule(release);
        var runtime = new ExecutionRuntime([module]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.ThreadPool, 2, new ExecutionOptions());
        CompletedResult.Read(runtime.StartAsync());
        CommandPort<int, int> port = runtime.Bind(module.Work);
        Task<int> first = port.ExecuteAsync(1).AsTask();
        try
        {
            await module.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(2, await port.ExecuteAsync(2).AsTask().WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.False(first.IsCompleted);
        }
        finally
        {
            release.Set();
            await first.WaitAsync(TimeSpan.FromSeconds(30));
            await runtime.StopAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    /// <summary>Repeated range requeues finish exactly once and leave shutdown able to drain.</summary>
    /// <returns>The completed shutdown check.</returns>
    [Fact]
    public async Task RequeuedRangesCompleteBeforeShutdownAsync()
    {
        var module = new GraphModule();
        var runtime = new ExecutionRuntime([module]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.ThreadPool, 16, new ExecutionOptions());
        CompletedResult.Read(runtime.StartAsync());
        var builder = new JobGraphBuilder<GraphInput>();
        builder.AddRange("Range", module.Range, 1);
        JobGraph<GraphInput> graph = builder.Build();
        for (int iteration = 0; iteration < 512; iteration++)
        {
            var data = new GraphData(128);
            var input = new GraphInput(data);
            Assert.True(runtime.Schedule(graph, in input, out JobRun? run) == CommandEnqueueStatus.Accepted);
            using (run)
            {
                await run!.Completion.WaitAsync(TimeSpan.FromSeconds(30));
                Assert.Equal(Enumerable.Range(1, 128), data.Values);
                Assert.Equal(128, data.Events.Count);
            }
        }

        await runtime.StopAsync(ShutdownMode.Drain).AsTask().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(ExecutionStatus.Stopped, runtime.Status);
    }

    private sealed class CpuModule(ManualResetEventSlim release) : IExecutionModule, ICommandHandler<int, int>
    {
        public string Name => "Cpu";

        internal TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal WorkKey<int, int> Work { get; private set; }

        public void Declare(ModuleBuilder builder)
            => Work = builder.Command<int, int, CpuModule>("Execute", this, new WorkOptions { Target = WorkTarget.Cpu });

        public void Configure(ExecutionPlanBuilder builder, OwnerPlacement placement)
        {
        }

        public ValueTask StartAsync(ModuleContext context, CancellationToken token) => ValueTask.CompletedTask;

        public ValueTask StopAsync(ModuleContext context) => ValueTask.CompletedTask;

        public int Execute(in int command, in WorkContext context)
        {
            if (command == 1)
            {
                FirstStarted.SetResult();
                if (!release.Wait(TimeSpan.FromSeconds(60)))
                {
                    throw new TimeoutException("The running CPU command was not released.");
                }
            }

            return command;
        }
    }
}
