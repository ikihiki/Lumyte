using System.Diagnostics;
using Lumyte.Audio.Abstractions;
using Lumyte.Audio.Execution;
using Lumyte.Execution;
using Lumyte.Memory;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.Audio.DependencyInjection;
using Lumyte.Resources.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Resources.Audio.IntegrationTests;

internal static class Program
{
    private static void Main(string[] args)
    {
        bool host = args.Contains("--host", StringComparer.Ordinal);
        bool di = args.Contains("--di", StringComparer.Ordinal);
        bool recover = args.Contains("--recover", StringComparer.Ordinal);
        bool recoverStop = args.Contains("--recover-stop", StringComparer.Ordinal);
        bool startCancel = args.Contains("--recover-start-cancel", StringComparer.Ordinal);
        bool native = args.Contains("--openal", StringComparer.Ordinal);
        if ((recover || recoverStop || startCancel) && (native || args.Contains("--reload", StringComparer.Ordinal)))
        {
            throw new ArgumentException("Recovery checks use injected device loss; run them separately from --openal and --reload.");
        }

        if (startCancel && !host)
        {
            throw new ArgumentException("--recover-start-cancel requires --host to cancel before the first owner pump.");
        }

        var factory = new TestFactory();
        var audio = new AudioModule(native ? new OpenALDeviceFactory() : factory);
        var source = new TestSource();
        ServiceProvider? provider = null;
        AsyncServiceScope? scope = null;
        ExecutionRuntime execution;
        ResourceManager manager;
        if (di)
        {
            var services = new ServiceCollection();
            services.AddSingleton(audio);
            services.AddSingleton(source);
            services.AddMemoryManagement();
            services.AddAudioResources<SharedTestSource>("audio");
            provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            execution = provider.GetRequiredService<ExecutionRuntime>();
            scope = provider.CreateAsyncScope();
            manager = scope.Value.ServiceProvider.GetRequiredKeyedService<ResourceManager>("audio");
        }
        else
        {
            var runtime = new ResourceRuntime();
            var bridge = new AudioResourceRuntime(audio, runtime);
            execution = new ExecutionRuntime([audio, bridge, runtime]);
            manager = new ResourceManager(
                "audio", source, [new AudioDecodeStage(), new AudioClipStage(), new AudioVoiceStage()], new MemoryManager(), runtime,
                stageRuntimes: [new(typeof(IAudioClip), bridge), new(typeof(IAudioVoice), bridge)]);
        }

        execution.PrepareOnCurrentHost(host ? OwnerPlacement.Host : OwnerPlacement.Dedicated, host ? CpuExecutionMode.Cooperative : CpuExecutionMode.ThreadPool, host ? 1 : 2, recover ? new ExecutionOptions { MaxCommands = 16 } : new ExecutionOptions());
        void PumpUntil(Func<bool> completed)
        {
            long start = Stopwatch.GetTimestamp();
            while (!completed())
            {
                execution.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
                execution.PumpJobs(64, TimeSpan.FromMilliseconds(2));
                if (host && execution.Status is ExecutionStatus.Running or ExecutionStatus.Quiescing)
                {
                    execution.RunHostPhase(audio.HostPhase, 64, TimeSpan.FromMilliseconds(2));
                }

                if (Stopwatch.GetElapsedTime(start) > TimeSpan.FromSeconds(30))
                {
                    throw new TimeoutException($"Resource runtime stalled: {execution.Status}.");
                }

                Thread.Sleep(1);
            }
        }

        void Wait(Task task)
        {
            PumpUntil(() => task.IsCompleted);
            task.GetAwaiter().GetResult();
        }

        try
        {
            if (startCancel)
            {
                RecoveryScenario.CancelBeforeOwnerStart(audio, execution, factory, Wait);
                return;
            }

            Wait(execution.StartAsync().AsTask());
            Task<ResourceReference<IAudioVoice>> load = manager.AcquireAsync(new ResourceKey<IAudioVoice>("tone")).AsTask();
            Wait(load);
            using (ResourceReference<IAudioVoice> reference = load.GetAwaiter().GetResult())
            using (ResourceLease<IAudioVoice> lease = reference.Acquire())
            {
                Wait(new AudioVoice(lease.Value).Play().AsTask());
                Wait(audio.State.Refresh().AsTask());
                if (lease.Value.State != AudioPlaybackState.Playing || (audio.State.OwnerThreadId == Environment.CurrentManagedThreadId) != host)
                {
                    throw new InvalidOperationException("Audio execution ownership or playback is incorrect.");
                }

                if (args.Contains("--reload", StringComparer.Ordinal))
                {
                    var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    manager.HotReload!.Reloaded += (_, result) =>
                    {
                        if (result.Result.Status == ResourceReloadStatus.Published)
                        {
                            published.TrySetResult();
                        }
                    };
                    source.Set(0.5f);
                    Wait(published.Task);
                    using ResourceLease<IAudioVoice> next = reference.Acquire();
                    if (next.Revision == lease.Revision || ReferenceEquals(next.Value, lease.Value) || next.Value.State != AudioPlaybackState.Stopped)
                    {
                        throw new InvalidOperationException("Reload did not publish a new stopped voice.");
                    }

                    Wait(new AudioVoice(lease.Value).Stop().AsTask());
                    Wait(new AudioVoice(next.Value).Play().AsTask());
                    var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    manager.HotReload.ReloadFailed += (_, _) => failed.TrySetResult();
                    source.Set(float.NaN);
                    Wait(failed.Task);
                    using ResourceLease<IAudioVoice> afterFailure = reference.Acquire();
                    if (!ReferenceEquals(next.Value, afterFailure.Value) || !lease.IsUsable)
                    {
                        throw new InvalidOperationException("Failed reload changed the publication or released an old lease.");
                    }

                    Console.WriteLine("Resource audio HotReload passed: replaced voice, retained old lease, rejected invalid decode.");
                }
            }

            if (recover)
            {
                RecoveryScenario.Run(audio, execution, manager, factory, Wait);
                RecoveryScenario.LossDuringBuild(audio, execution, manager, factory, Wait);
                RecoveryScenario.RetryCapacityFailure(audio, execution, factory, Wait, PumpUntil);
            }

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            try
            {
                Wait(manager.AcquireAsync(new ResourceKey<AudioPcmData>("canceled"), canceled.Token).AsTask());
                throw new InvalidOperationException("Canceled resource loading executed.");
            }
            catch (OperationCanceledException)
            {
            }

            try
            {
                Wait(manager.AcquireAsync(new ResourceKey<IAudioVoice>("invalid")).AsTask());
                throw new InvalidOperationException("Invalid input was published.");
            }
            catch (InvalidDataException)
            {
            }

            Wait(manager.DisposeAsync().AsTask());
            if (recoverStop)
            {
                RecoveryScenario.StopDuringRecovery(audio, execution, factory, Wait);
            }

            Wait(execution.StopAsync().AsTask());
            Console.WriteLine($"Resource audio runtime passed: host={host}, di={di}, owner={audio.State.OwnerThreadId}");
        }
        finally
        {
            // Resource cleanup must be pumped while Audio still accepts cleanup commands.
            Wait(manager.DisposeAsync().AsTask());
            Wait(execution.StopAsync().AsTask());
            if (scope is { } ownedScope)
            {
                Wait(ownedScope.DisposeAsync().AsTask());
            }

            if (provider is not null)
            {
                Wait(provider.DisposeAsync().AsTask());
            }
        }
    }
}
