using BenchmarkDotNet.Attributes;

namespace Lumyte.Execution.Benchmark;

/// <summary>Measures a generated static endpoint in its isolated benchmark process.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Core", "Execution", "Commands", "Screening", "Comparison", "Diagnostic")]
public class GeneratedSenderBenchmarks
{
    private readonly SenderState _state = new();
    private Fixture _fixture = null!;
    private Sender _sender;

    internal int ObservedValue => _state.Value;

    /// <summary>Connects the generated static endpoint once per benchmark process.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _fixture = new Fixture(generated: true);
        _sender = new Sender(_state);
    }

    /// <summary>Stops the static endpoint after all measurements.</summary>
    [GlobalCleanup]
    public void Cleanup() => _fixture.Dispose();

    /// <summary>Sends through the actual generated expanded wrapper.</summary>
    /// <returns>The receiver result.</returns>
    [Benchmark]
    [BenchmarkCategory("Commands")]
    public int GeneratedSender() => _fixture.Read(_sender.PlayAsync(7));
}
