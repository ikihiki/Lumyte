using System.Diagnostics;
using Lumyte.Audio.Abstractions;
using Lumyte.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Audio.Execution.Sample;

internal static class Program
{
    private static void Main(string[] args)
    {
        bool host = args.Contains("--host", StringComparer.Ordinal);
        bool useDi = args.Contains("--di", StringComparer.Ordinal);
        ServiceProvider? provider = null;
        AudioModule audio;
        ExecutionRuntime runtime;
        if (useDi)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IExecutionModule, AudioModule>();
            services.AddSingleton<ExecutionRuntime>();
            provider = services.BuildServiceProvider();
            runtime = provider.GetRequiredService<ExecutionRuntime>();
            audio = provider.GetServices<IExecutionModule>().OfType<AudioModule>().Single();
        }
        else
        {
            audio = new AudioModule();
            runtime = new ExecutionRuntime([audio]);
        }

        runtime.PrepareOnCurrentHost(host ? OwnerPlacement.Host : OwnerPlacement.Dedicated, host ? CpuExecutionMode.Cooperative : CpuExecutionMode.ThreadPool, host ? 1 : 4, new ExecutionOptions());
        void Pump()
        {
            runtime.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
            runtime.PumpJobs(64, TimeSpan.FromMilliseconds(2));
            if (host && runtime.Status is ExecutionStatus.Running or ExecutionStatus.Quiescing)
            {
                runtime.RunHostPhase(audio.HostPhase, 64, TimeSpan.FromMilliseconds(2));
            }
        }

        void Wait(Task task)
        {
            long start = Stopwatch.GetTimestamp();
            while (!task.IsCompleted)
            {
                Pump();
                if (Stopwatch.GetElapsedTime(start) > TimeSpan.FromSeconds(10))
                {
                    throw new TimeoutException("The host did not finish the operation.");
                }

                Thread.Sleep(1);
            }

            task.GetAwaiter().GetResult();
        }

        try
        {
            Wait(runtime.StartAsync().AsTask());
            if ((audio.State.OwnerThreadId == Environment.CurrentManagedThreadId) != host)
            {
                throw new InvalidOperationException("The audio owner placement is incorrect.");
            }

            async Task UseVoiceAsync()
            {
                float[] pcm = new float[4800];
                for (int i = 0; i < pcm.Length; i++)
                {
                    pcm[i] = (float)(0.1 * Math.Sin(i * 2 * Math.PI * 440 / 48000));
                }

                AudioClip clip = await AudioDevice.CreateClipPort.ExecuteAsync(new AudioFormat(48000, 1), pcm);
                AudioVoice original = await AudioClip.Commands.For(clip).CreateVoice(AudioBus.Effects);
                var voice = AudioVoice.Commands.For(original);
                try
                {
                    await AudioClip.Commands.For(clip).DestroyClip();
                    throw new InvalidOperationException("Borrowed clip destruction succeeded.");
                }
                catch (InvalidOperationException error) when (error.Message != "Borrowed clip destruction succeeded.")
                {
                }

                await voice.Play();
                await voice.Pause();
                await AudioDevice.RefreshPort.ExecuteAsync();
                if (original.State != AudioPlaybackState.Paused)
                {
                    throw new InvalidOperationException("Real backend is not paused.");
                }

                await voice.SetGain(0.5f);
                await voice.SetLoop(true);
                await voice.Play();
                await AudioDevice.RefreshPort.ExecuteAsync();
                if (original.State != AudioPlaybackState.Playing)
                {
                    throw new InvalidOperationException("Real backend is not playing.");
                }

                using (var batch = new CommandBatch(3))
                {
                    voice.AddSetGainTo(batch, 0.25f);
                    voice.AddPlayTo(batch);
                    voice.AddStopTo(batch);
                    if (!runtime.TrySubmit(batch, out CommandRun? run) || run is null)
                    {
                        throw new InvalidOperationException("Audio batch was rejected.");
                    }

                    using (run)
                    {
                        await run.Completion;
                        run.ThrowIfFailed();
                    }
                }

                await voice.Stop();
                await AudioDevice.RefreshPort.ExecuteAsync();
                if (original.State != AudioPlaybackState.Stopped)
                {
                    throw new InvalidOperationException("Real backend is not stopped.");
                }

                await voice.Destroy();
                await AudioClip.Commands.For(clip).DestroyClip();

                Console.WriteLine($"Real OpenAL controls passed; Host={host}, DI={useDi}");
            }

            Wait(UseVoiceAsync());
        }
        finally
        {
            Wait(runtime.StopAsync().AsTask());
            provider?.Dispose();
        }
    }
}
