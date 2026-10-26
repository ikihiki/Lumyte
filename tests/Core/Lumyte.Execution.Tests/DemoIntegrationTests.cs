using System.Diagnostics;
using System.Runtime.CompilerServices;
using Lumyte.Execution.Sample;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Execution.Tests;

/// <summary>Exercises actual generated commands, one-reference voices, and factory-free DI collection.</summary>
public sealed class DemoIntegrationTests
{
    /// <summary>DI resolves modules, startup chooses the owner, and voice copies forward to that owner.</summary>
    [Fact]
    public void ParameterlessDiRegistrationDrivesGeneratedVoiceOnDedicatedOwner()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionModule, DemoModule>();
        services.AddSingleton<ExecutionRuntime>();
        using ServiceProvider provider = services.BuildServiceProvider();
        ExecutionRuntime runtime = provider.GetRequiredService<ExecutionRuntime>();
        DemoModule audio = provider.GetServices<IExecutionModule>().OfType<DemoModule>().Single();
        Assert.Equal(0, audio.State.ThreadId);
        Assert.Equal(ExecutionStatus.Unprepared, runtime.Status);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Dedicated, CpuExecutionMode.ThreadPool, 2, new ExecutionOptions());
        void PumpUntil(Func<bool> done)
        {
            long start = Stopwatch.GetTimestamp();
            while (!done())
            {
                runtime.PumpHostLifecycle(16, TimeSpan.FromMilliseconds(2));
                runtime.PumpJobs(16, TimeSpan.FromMilliseconds(2));
                if (Stopwatch.GetElapsedTime(start) > TimeSpan.FromSeconds(10))
                {
                    throw new TimeoutException();
                }

                Thread.Sleep(1);
            }
        }

        Task start = runtime.StartAsync().AsTask();
        PumpUntil(() => start.IsCompleted);
        CompletedResult.Read(start);
        Assert.NotEqual(Environment.CurrentManagedThreadId, audio.State.ThreadId);
        ValueTask<DemoVoiceData> creation = audio.State.CreateVoice(1);
        PumpUntil(() => creation.IsCompleted);
        DemoVoiceData original = CompletedResult.Read(creation);
        var voice = new DemoVoice(original);
        DemoVoice copy = voice;
        Assert.Equal(IntPtr.Size, Unsafe.SizeOf<DemoVoice>());
        Assert.Throws<InvalidOperationException>(() => default(DemoVoice).Play());
        ValueTask<Unit> play = voice.Play();
        PumpUntil(() => play.IsCompleted);
        CompletedResult.Read(play);
        Assert.True(original.Playing);
        ValueTask<Unit> stop = copy.Stop();
        PumpUntil(() => stop.IsCompleted);
        CompletedResult.Read(stop);
        Assert.False(original.Playing);
        using var batch = new CommandBatch(2);
        voice.AddPlayTo(batch);
        voice.AddDestroyTo(batch);
        Assert.True(runtime.TrySubmit(batch, out CommandRun? run));
        PumpUntil(() => run!.IsCompleted);
        run!.ThrowIfFailed();
        run.Dispose();
        Assert.True(original.Destroyed);
        ValueTask<Unit> invalid = copy.Play();
        PumpUntil(() => invalid.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => CompletedResult.Read(invalid));
        Task shutdown = runtime.StopAsync().AsTask();
        PumpUntil(() => shutdown.IsCompleted);
        CompletedResult.Read(shutdown);
        ValueTask<DemoVoiceData> closed = audio.State.CreateVoice(2);
        Assert.Throws<InvalidOperationException>(() => CompletedResult.Read(closed));
        var replacement = new ExecutionRuntime([new DemoModule()]);
        Assert.Throws<InvalidOperationException>(() => replacement.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions()));
    }
}
