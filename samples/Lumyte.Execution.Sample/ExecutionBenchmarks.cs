using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Lumyte.Execution.Sample;

internal static class ExecutionBenchmarks
{
    private const int Iterations = 20000;
    private const int BatchSize = 16;
    private static Action? _escaped;

    internal static void Run(ExecutionRuntime runtime, AudioModule audio, AudioVoice original)
    {
        var voice = AudioState.Voice.For(original);
        Action cached = () => audio.State.PlayDirect(original);
        Print("direct", MeasureDirect(audio.State, original, Iterations));
        Print("cached_delegate", Measure(cached, Iterations));
        Print("captured_delegate", Measure(() => CreateCaptured(audio.State, original)(), Iterations));
        Action single = () =>
        {
            ValueTask<Unit> result = voice.Play();
            runtime.RunHostPhase(audio.HostPhase, 64, TimeSpan.FromMilliseconds(2));
            result.GetAwaiter().GetResult();
        };
        Print("typed_single", Measure(single, Iterations));
        using var batch = new CommandBatch(BatchSize);
        Action executeBatch = () =>
        {
            for (int i = 0; i < BatchSize; i++)
            {
                voice.AddPlayTo(batch);
            }

            if (!runtime.TrySubmit(batch, out CommandRun? run))
            {
                throw new InvalidOperationException("Unexpected benchmark capacity shortage.");
            }

            runtime.RunHostPhase(audio.HostPhase, 64, TimeSpan.FromMilliseconds(2));
            run!.ThrowIfFailed();
            run.Dispose();
            batch.Reset();
        };
        (double Nanoseconds, double Bytes) measured = Measure(executeBatch, Iterations / BatchSize);
        Print("typed_batch_16", (measured.Nanoseconds / BatchSize, measured.Bytes / BatchSize));
        Console.WriteLine($"sender_bytes={Unsafe.SizeOf<AudioState.Voice>()}, runtime={Environment.Version}, os={Environment.OSVersion}, cpu_count={Environment.ProcessorCount}");
        _escaped = null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Action CreateCaptured(AudioState state, AudioVoice original)
    {
        // The escape prevents stack allocation or immediate-invocation delegate elimination.
        _escaped = () => state.PlayDirect(original);
        return _escaped;
    }

    private static (double Nanoseconds, double Bytes) MeasureDirect(AudioState state, AudioVoice voice, int iterations)
    {
        for (int i = 0; i < 1000; i++)
        {
            state.PlayDirect(voice);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
        {
            state.PlayDirect(voice);
        }

        long elapsed = Stopwatch.GetTimestamp() - started;
        return ((double)elapsed * 1000000000 / Stopwatch.Frequency / iterations, (double)(GC.GetAllocatedBytesForCurrentThread() - allocated) / iterations);
    }

    private static (double Nanoseconds, double Bytes) Measure(Action action, int iterations)
    {
        for (int i = 0; i < 1000; i++)
        {
            action();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
        {
            action();
        }

        long elapsed = Stopwatch.GetTimestamp() - started;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        return ((double)elapsed * 1000000000 / Stopwatch.Frequency / iterations, (double)bytes / iterations);
    }

    private static void Print(string name, (double Nanoseconds, double Bytes) metrics) => Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name},{metrics.Nanoseconds:F2},{metrics.Bytes:F2}"));
}
