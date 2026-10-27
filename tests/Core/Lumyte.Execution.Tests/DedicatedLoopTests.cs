using System.Collections.Concurrent;
using Xunit;

namespace Lumyte.Execution.Tests;

/// <summary>Checks control progress and update fairness on physical owner threads.</summary>
public sealed class DedicatedLoopTests
{
    /// <summary>A completed control turn does not reserve the rest of the update period.</summary>
    [Fact]
    public void PendingControlsContinueBeforeTheNextPeriodicUpdate()
    {
        using var fixture = new Fixture(new ThreadLoopOptions(TimeSpan.FromSeconds(5), 1, TimeSpan.FromMilliseconds(10)));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        fixture.Module.BeforeExecute = command =>
        {
            if (command == 1)
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            }
        };
        fixture.Start();
        Assert.True(fixture.Module.Updated.Wait(TimeSpan.FromSeconds(5)));
        Task<int> first = fixture.Module.Port.ExecuteAsync(1).AsTask();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Task<int> second = fixture.Module.Port.ExecuteAsync(2).AsTask();
            release.Set();
            Assert.Equal(new[] { 1, 2 }, fixture.Wait(Task.WhenAll(first, second), TimeSpan.FromSeconds(2)));
            Assert.Equal(new[] { 1, 2 }, fixture.Module.Executed.Select(static item => item.Command));
        }
        finally
        {
            release.Set();
        }
    }

    /// <summary>Once a handler crosses an update deadline, queued controls yield to that update.</summary>
    [Fact]
    public void DueUpdatesRunWhileControlsRemainQueued()
    {
        using var fixture = new Fixture(new ThreadLoopOptions(TimeSpan.FromMilliseconds(10), 64, TimeSpan.FromMilliseconds(2)));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        fixture.Module.BeforeExecute = command =>
        {
            if (command == 1)
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            }
        };
        fixture.Start();
        Assert.True(fixture.Module.Updated.Wait(TimeSpan.FromSeconds(5)));
        Task<int> first = fixture.Module.Port.ExecuteAsync(1).AsTask();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Task<int>[] remaining = Enumerable.Range(2, 8).Select(command => fixture.Module.Port.ExecuteAsync(command).AsTask()).ToArray();
            Thread.Sleep(40);
            release.Set();
            fixture.Wait(first);
            fixture.Wait(Task.WhenAll(remaining));
            (int Command, int Updates)[] executed = fixture.Module.Executed.ToArray();
            Assert.Equal(Enumerable.Range(1, 9), executed.Select(static item => item.Command));
            Assert.True(executed[1].Updates > executed[0].Updates);
        }
        finally
        {
            release.Set();
        }
    }

    /// <summary>Gate notifications wake an owner whose commands and periodic update are both blocked.</summary>
    [Fact]
    public void OpeningAGateWakesAnIdleOwner()
    {
        using var fixture = new Fixture(new ThreadLoopOptions(TimeSpan.FromSeconds(5), 1, TimeSpan.FromMilliseconds(10)), gated: true);
        fixture.Start();
        Task<int> result = fixture.Module.Port.ExecuteAsync(1).AsTask();
        Assert.False(result.IsCompleted);
        fixture.Module.Gate!.Open();
        Assert.Equal(1, fixture.Wait(result, TimeSpan.FromSeconds(2)));
        Assert.True(fixture.Module.Updated.Wait(TimeSpan.FromSeconds(2)));
    }

    /// <summary>Token cancellation wakes gate-blocked single commands and batches without another notification.</summary>
    /// <param name="batched">Whether cancellation applies to a submitted batch.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationWakesAnOwnerWhileItsGateRemainsClosed(bool batched)
    {
        using var fixture = new Fixture(new ThreadLoopOptions(TimeSpan.FromSeconds(5), 1, TimeSpan.FromMilliseconds(10)), gated: true);
        using var cancellation = new CancellationTokenSource();
        using var batch = new CommandBatch(1);
        fixture.Start();
        CommandRun? run = null;
        Task completion;
        if (batched)
        {
            batch.Add(fixture.Module.Port, 1);
            Assert.True(fixture.Runtime.Submit(batch, out run, cancellation.Token) == CommandEnqueueStatus.Accepted);
            completion = run!.Completion;
        }
        else
        {
            completion = fixture.Module.Port.ExecuteAsync(1, cancellation.Token).AsTask();
        }

        try
        {
            fixture.Wait(fixture.Module.Probe.ExecuteAsync(0).AsTask());

            // Let the owner consume the admission signal and park with the gate still closed.
            Thread.Sleep(20);
            Assert.False(completion.IsCompleted);
            cancellation.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => fixture.Wait(completion, TimeSpan.FromSeconds(2)));
            Assert.Empty(fixture.Module.Executed);
        }
        finally
        {
            if (run?.IsCompleted == true)
            {
                run.Dispose();
            }
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal Fixture(ThreadLoopOptions options, bool gated = false)
        {
            Module = new(options, gated);
            Runtime = new([Module]);
            Runtime.PrepareOnCurrentHost(OwnerPlacement.Dedicated, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        }

        internal LoopModule Module { get; }

        internal ExecutionRuntime Runtime { get; }

        public void Dispose()
        {
            try
            {
                Runtime.StopAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            }
            finally
            {
                Module.Updated.Dispose();
            }
        }

        internal void Start() => Wait(Runtime.StartAsync().AsTask());

        internal void Wait(Task task, TimeSpan? timeout = null)
            => task.WaitAsync(timeout ?? TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

        internal TResult Wait<TResult>(Task<TResult> task, TimeSpan? timeout = null)
            => task.WaitAsync(timeout ?? TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    }

    private sealed class LoopModule(ThreadLoopOptions options, bool gated) : IExecutionModule
    {
        private OwnerKey _owner;
        private UpdateKey _update;
        private GateKey _gate;
        private int _updates;

        public string Name => "Loop";

        internal CommandPort<int, int> Port { get; } = new();

        internal CommandPort<int, int> Probe { get; } = new();

        internal ConcurrentQueue<(int Command, int Updates)> Executed { get; } = new();

        internal ManualResetEventSlim Updated { get; } = new();

        internal GateControl? Gate { get; private set; }

        internal Action<int>? BeforeExecute { get; set; }

        public void Declare(ModuleBuilder builder)
        {
            _owner = builder.Owner("Device", OwnerRequirement.FixedThread);
            OrderGroupKey group = builder.OrderGroup("Control", _owner);
            OrderGroupKey probe = builder.OrderGroup("ProbeGroup", _owner);
            builder.Command<int, int, ProbeHandler>("Probe", default, new WorkOptions { Target = WorkTarget.On(_owner), OrderGroup = probe }, Probe);
            _gate = builder.Gate("Ready");
            builder.Command<int, int, Handler>("Command", new Handler(this), new WorkOptions { Target = WorkTarget.On(_owner), OrderGroup = group, RequiredGates = new GateKey[] { _gate } }, Port);
            _update = builder.Update("Update", _owner, new UpdateHandler(this), new GateKey[] { _gate });
        }

        public void Configure(ExecutionPlanBuilder builder, OwnerPlacement placement)
        {
            builder.BindDedicated(_owner, builder.DedicatedThread("Owner"));
            builder.DrivePeriodic(_update, options);
        }

        public ValueTask StartAsync(ModuleContext context, CancellationToken token)
        {
            Gate = context.GetGate(_gate);
            if (!gated)
            {
                Gate.Open();
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(ModuleContext context) => ValueTask.CompletedTask;

        private readonly struct Handler(LoopModule module) : ICommandHandler<int, int>
        {
            public int Execute(in int command, in WorkContext context)
            {
                context.VerifyAccess();
                module.BeforeExecute?.Invoke(command);
                module.Executed.Enqueue((command, Volatile.Read(ref module._updates)));
                return command;
            }
        }

        private readonly struct ProbeHandler : ICommandHandler<int, int>
        {
            public int Execute(in int command, in WorkContext context)
            {
                context.VerifyAccess();
                return command;
            }
        }

        private readonly struct UpdateHandler(LoopModule module) : IUpdateHandler
        {
            public void Update(in UpdateTick tick, in WorkContext context)
            {
                context.VerifyAccess();
                Interlocked.Increment(ref module._updates);
                module.Updated.Set();
            }
        }
    }
}
