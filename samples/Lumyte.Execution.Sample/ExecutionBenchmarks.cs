namespace Lumyte.Execution.Sample;

internal static class ExecutionBenchmarks
{
    internal static void Run(ExecutionRuntime runtime, DemoModule audio, DemoVoiceData original)
    {
        var voice = new DemoVoice(original);
        ValueTask<Unit> result = voice.PlayAsync();
        runtime.RunHostPhase(audio.HostPhase, 64, TimeSpan.FromSeconds(1));
        result.GetAwaiter().GetResult();
        Console.WriteLine("Benchmark timing moved to benchmarks/Core/Lumyte.Execution.Benchmark; use --mode screen or --mode compare there.");
    }
}
