using BenchmarkDotNet.Attributes;

namespace Lumyte.Diagnostics.Benchmarks;

/// <summary>Measures generated dispatch including schema and authorization validation.</summary>
[MemoryDiagnoser]
public class OperationBenchmarks
{
    private readonly OperationFixture _fixture = new();
    private readonly Dictionary<string, DiagnosticValue> _values = new() { ["value"] = DiagnosticValue.From(42L) };
    private readonly HashSet<DiagnosticPermission> _permissions = [DiagnosticPermission.Observe];
    private readonly DiagnosticOperationContext _context = new(Guid.NewGuid(), Guid.NewGuid(), default, "benchmark", null, default);
    private DiagnosticOperationSet _set = null!;

    /// <summary>Constructs the generated catalog outside measured code.</summary>
    [GlobalSetup]
    public void Setup() => _set = new(_fixture);

    /// <summary>Calls the ordinary typed method.</summary>
    /// <returns>The typed result.</returns>
    [Benchmark(Baseline = true)]
    public DiagnosticResult<OperationReceipt> Direct() => _fixture.Echo(42);

    /// <summary>Invokes through generated code and runtime validation.</summary>
    /// <returns>The detached result.</returns>
    [Benchmark]
    public DiagnosticOperationResult Generated() => _set.Invoke("echo", _values, _context, _permissions);
}
