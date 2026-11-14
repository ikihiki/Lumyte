using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;

namespace Lumyte.Execution.Generators.Benchmark;

/// <summary>Measures only warm driver reruns with distinct change profiles.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Standalone", "CompileTime", "Core", "Execution.Generators", "SourceGeneration", "IncrementalGeneration", "Screening", "Comparison", "Diagnostic")]
public class IncrementalBenchmarks
{
    private GeneratorBenchmarks _driver = null!;

    /// <summary>Gets or sets the finite declaration count.</summary>
    [Params(1, 8, 32)]
    public int Count { get; set; } = 8;

    /// <summary>Gets or sets the precise input change profile.</summary>
    [Params("Unchanged", "Unrelated", "Target")]
    public string Change { get; set; } = "Unchanged";

    /// <summary>Primes the driver outside measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _driver = new GeneratorBenchmarks { Count = Count, Change = Change };
        _driver.Setup();
    }

    /// <summary>Alternates at most two compilations and retains only the current driver.</summary>
    /// <returns>The actual generated trees and diagnostics.</returns>
    [Benchmark]
    public GeneratorDriverRunResult Rerun() => _driver.Warm();
}
