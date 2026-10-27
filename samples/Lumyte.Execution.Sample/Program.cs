using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Execution.Sample;

internal static class Program
{
    private static void Main(string[] args)
    {
        bool host = args.Contains("--host", StringComparer.Ordinal);
        bool useDi = args.Contains("--di", StringComparer.Ordinal);
        ServiceProvider? provider = null;
        DemoModule audio;
        ExecutionRuntime runtime;
        if (useDi)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IExecutionModule, DemoModule>();
            services.AddSingleton<ExecutionRuntime>();
            provider = services.BuildServiceProvider();
            runtime = provider.GetRequiredService<ExecutionRuntime>();
            audio = provider.GetServices<IExecutionModule>().OfType<DemoModule>().Single();
        }
        else
        {
            audio = new DemoModule();
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
            async Task UseVoiceAsync()
            {
                DemoVoiceData original = await audio.State.CreateVoiceAsync(7);
                var voice = new DemoVoice(original);
                await voice.PlayAsync();
                await voice.StopAsync();
                await voice.DestroyAsync();
                if (!original.Destroyed)
                {
                    throw new InvalidOperationException("Destruction was not applied.");
                }
            }

            Wait(UseVoiceAsync());
            if (args.Contains("--benchmark", StringComparer.Ordinal))
            {
                if (!host)
                {
                    throw new ArgumentException("The allocation benchmark requires --host.");
                }

                ValueTask<DemoVoiceData> creation = audio.State.CreateVoiceAsync(9);
                Task<DemoVoiceData> created = creation.AsTask();
                Wait(created);
                ExecutionBenchmarks.Run(runtime, audio, created.GetAwaiter().GetResult());
            }

            Console.WriteLine($"Host={Environment.CurrentManagedThreadId}, Owner={audio.State.ThreadId}, Updates={audio.State.UpdateCount}, DI={useDi}");
        }
        finally
        {
            Wait(runtime.StopAsync().AsTask());
            provider?.Dispose();
        }
    }
}
