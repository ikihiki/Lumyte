using Xunit;

namespace Lumyte.Execution.Tests;

/// <summary>Checks module identities, enumeration, dependency ordering, and atomic endpoint preparation.</summary>
public sealed class ModuleCollectionTests
{
    /// <summary>Dependency lifecycle ordering is independent of DI enumeration order.</summary>
    [Fact]
    public void DependenciesStartFirstAndStopLast()
    {
        var events = new List<string>();
        var runtime = new ExecutionRuntime([new Dependent(events), new Dependency(events)]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        CompletedResult.Read(runtime.StartAsync());
        CompletedResult.Read(runtime.StopAsync());
        Assert.Equal(new[] { "Dependency", "Dependent", "-Dependent", "-Dependency" }, events);
    }

    /// <summary>Missing, cyclic, and duplicate identities are rejected before thread creation.</summary>
    [Fact]
    public void InvalidModuleSetsAreRejectedDuringCollection()
    {
        var events = new List<string>();
        Assert.Throws<ArgumentException>(() => new ExecutionRuntime([new Dependent(events)]));
        Assert.Throws<ArgumentException>(() => new ExecutionRuntime([new Dependency(events), new Dependency(events)]));
        Assert.Throws<ArgumentException>(() => new ExecutionRuntime([new CyclicA(events), new CyclicB(events)]));
    }

    /// <summary>A later conflicting static endpoint cannot partially consume earlier fresh endpoints.</summary>
    [Fact]
    public void PreparationConflictDoesNotPublishOrConsumeFreshEndpoint()
    {
        using var fixture = new ExecutionFixture();
        var fresh = new CommandPort<int, int>();
        var failed = new ExecutionRuntime([new PortModule(fresh, fixture.Module.Port)]);
        Assert.Throws<InvalidOperationException>(() => failed.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions()));
        Assert.Equal(CommandEnqueueStatus.NotRunning, fresh.TryEnqueue(1, default, out _));
        var recovered = new ExecutionRuntime([new PortModule(fresh, null)]);
        recovered.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        CompletedResult.Read(recovered.StartAsync());
        ValueTask<int> result = fresh.ExecuteAsync(3);
        recovered.PumpJobs(1, TimeSpan.FromSeconds(1));
        Assert.Equal(3, CompletedResult.Read(result));
        CompletedResult.Read(recovered.StopAsync());
    }

    /// <summary>Stopping cannot strand cleanup after an uncancelable external startup operation finishes.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task QuiescingStartupContextCanFinishCleanupAsync()
    {
        // Keep the fixed Host off the pool while awaiting pool-scheduled startup/stop continuations.
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new Thread(() =>
        {
            try
            {
                RunQuiescingStartupContext();
                completion.SetResult();
            }
            catch (Exception error)
            {
                completion.SetException(error);
            }
        })
        { IsBackground = true };
        host.Start();
        await completion.Task;
    }

    private static void RunQuiescingStartupContext()
    {
        var module = new PendingStartupModule();
        var runtime = new ExecutionRuntime([module]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        Task startup = runtime.StartAsync().AsTask();
        Task stop = runtime.StopAsync().AsTask();
        Assert.False(stop.IsCompleted);
        module.External.TrySetResult();
        bool PumpWhileAwaitingStop()
        {
            runtime.PumpHostLifecycle(8, TimeSpan.FromMilliseconds(2));
            runtime.PumpJobs(8, TimeSpan.FromMilliseconds(2));
            return stop.IsCompleted;
        }

        // Shared CI runners may delay asynchronous continuations while other test assemblies compile.
        // This is a lifecycle correctness test, not a five-second shutdown latency requirement.
        Assert.True(
            SpinWait.SpinUntil(PumpWhileAwaitingStop, TimeSpan.FromSeconds(30)),
            $"Shutdown timed out: runtime={runtime.Status}, startup={startup.Status}, stop={stop.Status}, cleaned={module.Cleaned}.");
        CompletedResult.Read(stop);
        Assert.True(module.Cleaned);
        Assert.Throws<OperationCanceledException>(() => CompletedResult.Read(startup));
    }

    private readonly struct IdentityHandler : ICommandHandler<int, int>
    {
        public int Execute(in int command, in WorkContext context) => command;
    }

    private sealed class PendingStartupModule : IExecutionModule, ICommandHandler<int, int>
    {
        private WorkKey<int, int> _cleanup;

        public string Name => "Pending";

        internal TaskCompletionSource External { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool Cleaned { get; private set; }

        public void Declare(ModuleBuilder builder) => _cleanup = builder.Command<int, int, PendingStartupModule>("Cleanup", this, new WorkOptions { Target = WorkTarget.Cpu, Admission = WorkAdmission.Cleanup });

        public void Configure(ExecutionPlanBuilder builder, OwnerPlacement placement)
        {
        }

        public async ValueTask StartAsync(ModuleContext context, CancellationToken token)
        {
            await External.Task.ConfigureAwait(false);
            await context.ExecuteAsync(_cleanup, 1).ConfigureAwait(false);
        }

        public ValueTask StopAsync(ModuleContext context) => ValueTask.CompletedTask;

        public int Execute(in int command, in WorkContext context)
        {
            context.VerifyAccess();
            Cleaned = true;
            return command;
        }
    }

    private abstract class ProbeModule(List<string> events) : IExecutionModule
    {
        public string Name => GetType().Name;

        public virtual void Declare(ModuleBuilder builder)
        {
        }

        public void Configure(ExecutionPlanBuilder builder, OwnerPlacement placement)
        {
        }

        public ValueTask StartAsync(ModuleContext context, CancellationToken token)
        {
            events.Add(Name);
            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(ModuleContext context)
        {
            events.Add("-" + Name);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Dependency(List<string> events) : ProbeModule(events);

    private sealed class Dependent(List<string> events) : ProbeModule(events)
    {
        public override void Declare(ModuleBuilder builder) => builder.DependsOn<Dependency>();
    }

    private sealed class CyclicA(List<string> events) : ProbeModule(events)
    {
        public override void Declare(ModuleBuilder builder) => builder.DependsOn<CyclicB>();
    }

    private sealed class CyclicB(List<string> events) : ProbeModule(events)
    {
        public override void Declare(ModuleBuilder builder) => builder.DependsOn<CyclicA>();
    }

    private sealed class PortModule(CommandPort<int, int> first, CommandPort<int, int>? second) : IExecutionModule
    {
        public string Name => "Ports";

        public void Declare(ModuleBuilder builder)
        {
            builder.Command<int, int, IdentityHandler>("First", default, new WorkOptions { Target = WorkTarget.Cpu }, first);
            if (second is not null)
            {
                builder.Command<int, int, IdentityHandler>("Second", default, new WorkOptions { Target = WorkTarget.Cpu }, second);
            }
        }

        public void Configure(ExecutionPlanBuilder builder, OwnerPlacement placement)
        {
        }

        public ValueTask StartAsync(ModuleContext context, CancellationToken token) => ValueTask.CompletedTask;

        public ValueTask StopAsync(ModuleContext context) => ValueTask.CompletedTask;
    }
}
