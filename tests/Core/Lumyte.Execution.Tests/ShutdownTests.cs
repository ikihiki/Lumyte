using Xunit;

namespace Lumyte.Execution.Tests;

/// <summary>Checks notifications racing with dedicated thread shutdown.</summary>
public sealed class ShutdownTests
{
    /// <summary>Gate notifications stay valid while owner threads are being released.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task GateNotificationsCanRaceWithShutdownAsync()
    {
        for (int iteration = 0; iteration < 32; iteration++)
        {
            var module = new GateModule();
            var runtime = new ExecutionRuntime([module]);
            runtime.PrepareOnCurrentHost(OwnerPlacement.Dedicated, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
            CompletedResult.Read(runtime.StartAsync());
            using var started = new ManualResetEventSlim();
            using var finish = new ManualResetEventSlim();
            var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var producer = new Thread(() =>
            {
                try
                {
                    module.Control!.Open();
                    started.Set();
                    while (!finish.IsSet)
                    {
                        try
                        {
                            module.Control.Open();
                        }
                        catch (InvalidOperationException error) when (error is not ObjectDisposedException && runtime.Status == ExecutionStatus.Stopped)
                        {
                            break;
                        }
                    }

                    notified.SetResult();
                }
                catch (Exception error)
                {
                    started.Set();
                    notified.SetException(error);
                }
            })
            { IsBackground = true };
            producer.Start();
            try
            {
                Assert.True(started.Wait(TimeSpan.FromSeconds(30)));
                await runtime.StopAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
            }
            finally
            {
                finish.Set();
                await notified.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }

            Assert.Equal(ExecutionStatus.Stopped, runtime.Status);
        }
    }

    private sealed class GateModule : IExecutionModule
    {
        private readonly OwnerKey[] _owners = new OwnerKey[8];
        private GateKey _gate;

        public string Name => "Gate";

        internal GateControl? Control { get; private set; }

        public void Declare(ModuleBuilder builder)
        {
            _gate = builder.Gate("Ready");
            for (int i = 0; i < _owners.Length; i++)
            {
                _owners[i] = builder.Owner($"Owner{i}", OwnerRequirement.FixedThread);
            }
        }

        public void Configure(ExecutionPlanBuilder builder, OwnerPlacement placement)
        {
            for (int i = 0; i < _owners.Length; i++)
            {
                builder.BindDedicated(_owners[i], builder.DedicatedThread($"Thread{i}"));
            }
        }

        public ValueTask StartAsync(ModuleContext context, CancellationToken token)
        {
            Control = context.GetGate(_gate);
            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(ModuleContext context) => ValueTask.CompletedTask;
    }
}
