using System.Diagnostics;
using Lumyte.Audio.Abstractions;
using Lumyte.Audio.Execution;
using Lumyte.Execution;
using Lumyte.Memory;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Lumyte.Resources.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Resources.Audio.Sample;

internal static class Program
{
    private static void Main(string[] args)
    {
        bool host = args.Contains("--host", StringComparer.Ordinal);
        bool di = args.Contains("--di", StringComparer.Ordinal);
        var audio = new AudioModule(args.Contains("--openal", StringComparer.Ordinal) ? new OpenALDeviceFactory() : new TestFactory());
        var bridge = new AudioResourceRuntime(audio);
        var jobs = new ResourceJob<DecodeRequest, AudioPcmData, DecodeHandler>();
        var source = new TestSource();
        ServiceProvider? provider = null;
        AsyncServiceScope? scope = null;
        ExecutionRuntime execution;
        ResourceManager manager;
        if (di)
        {
            var services = new ServiceCollection();
            services.AddSingleton(audio);
            services.AddSingleton(bridge);
            services.AddSingleton(jobs);
            services.AddSingleton<IExecutionModule>(provider => provider.GetRequiredService<AudioModule>());
            services.AddSingleton<IExecutionModule>(provider => provider.GetRequiredService<AudioResourceRuntime>());
            services.AddSingleton<IExecutionModule>(provider => provider.GetRequiredService<ResourceJob<DecodeRequest, AudioPcmData, DecodeHandler>>());
            services.AddSingleton<ExecutionRuntime>();
            services.AddSingleton(source);
            services.AddMemoryManagement();
            services.AddResources("audio", builder => builder.UseSource<TestSource>()
                .AddSourcePipeline<AudioPcmData, DecodeStage>()
                .AddTransformPipeline<AudioPcmData, IAudioClip, AudioClipStage>()
                .AddTransformPipeline<IAudioClip, IAudioVoice, AudioVoiceStage>());
            services.AddResourceRuntimeDependency("audio", "Audio", provider => provider.GetRequiredService<AudioResourceRuntime>());
            provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            execution = provider.GetRequiredService<ExecutionRuntime>();
            scope = provider.CreateAsyncScope();
            manager = scope.Value.ServiceProvider.GetRequiredKeyedService<ResourceManager>("audio");
        }
        else
        {
            execution = new ExecutionRuntime([audio, bridge, jobs]);
            manager = new ResourceManager("audio", source, [new DecodeStage(jobs), new AudioClipStage(bridge), new AudioVoiceStage(bridge)], new MemoryManager(), [bridge]);
        }

        execution.PrepareOnCurrentHost(host ? OwnerPlacement.Host : OwnerPlacement.Dedicated, host ? CpuExecutionMode.Cooperative : CpuExecutionMode.ThreadPool, host ? 1 : 2, new ExecutionOptions());
        void Wait(Task task)
        {
            long start = Stopwatch.GetTimestamp();
            while (!task.IsCompleted)
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

            task.GetAwaiter().GetResult();
        }

        try
        {
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
            }

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            try
            {
                Wait(jobs.Execute(new DecodeRequest([1]), canceled.Token).AsTask());
                throw new InvalidOperationException("Canceled CPU work executed.");
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
            Wait(execution.StopAsync().AsTask());
            Console.WriteLine($"Resource audio runtime passed: host={host}, di={di}, owner={audio.State.OwnerThreadId}, decode={DecodeHandler.LastThreadId}");
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
