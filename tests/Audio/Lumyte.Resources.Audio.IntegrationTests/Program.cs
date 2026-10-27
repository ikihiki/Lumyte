using System.Diagnostics;
using Lumyte.Audio.Execution;
using Lumyte.Execution;
using Lumyte.Memory;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.Audio.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
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
        ExecutionRuntime execution;
        ResourceManager manager;
        ResourceRuntime runtime;
        AudioResourceRuntime bridge;
        if (di)
        {
            var services = new ServiceCollection();
            services.AddSingleton(audio);
            services.AddSingleton(source);
            services.AddMemoryManagement();
            services.AddAudioResources();
            services.AddResources(builder => builder.UseSource<SharedTestSource>().AddAudioPipeline());
            provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            execution = provider.GetRequiredService<ExecutionRuntime>();
            runtime = provider.GetRequiredService<ResourceRuntime>();
            bridge = provider.GetRequiredService<AudioResourceRuntime>();
            manager = provider.GetRequiredService<ResourceManager>();
        }
        else
        {
            runtime = new ResourceRuntime();
            bridge = new AudioResourceRuntime(audio, runtime);
            execution = new ExecutionRuntime([audio, bridge, runtime]);
            manager = new ResourceManager(
                "audio",
                source,
                [new AudioDecodeStage(), new AudioClipStage()],
                new MemoryManager(),
                runtime,
                stageRuntimes: [new(typeof(AudioClip), bridge)]);
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
            if (!native)
            {
                CreationScenario.Run(audio, runtime, bridge, factory, Wait);
            }

            VoiceScenario.Run(audio, manager, source, host, args.Contains("--reload", StringComparer.Ordinal), Wait);
            SeekScenario.Run(audio, manager, Wait);

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
                Wait(manager.RetainAsync(new ResourceKey<AudioPcmData>("canceled"), canceled.Token).AsTask());
                throw new InvalidOperationException("Canceled resource loading executed.");
            }
            catch (OperationCanceledException)
            {
            }

            try
            {
                Wait(manager.RetainAsync(new ResourceKey<AudioClip>("invalid")).AsTask());
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
            if (provider is not null)
            {
                Wait(provider.DisposeAsync().AsTask());
            }
        }
    }
}
