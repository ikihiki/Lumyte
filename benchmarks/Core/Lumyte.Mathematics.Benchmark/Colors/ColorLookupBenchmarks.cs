using BenchmarkDotNet.Attributes;
using Lumyte.Mathematics.Colors;

namespace Lumyte.Mathematics.Benchmark;

/// <summary>Warm exact, case-insensitive and missing color lookup profiles.</summary>
[BenchmarkCategory("Core", "Mathematics", "Colors", "ColorLookup")]
public class ColorLookupBenchmarks
{
    /// <summary>Gets or sets lookup input.</summary>
    [Params("red", "ReD", "not-a-color", "")]
    public string Name { get; set; } = "red";

    /// <summary>Executes the NamedLookup color operation.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-COLOR-07")]
    public bool NamedLookup() => CssColors.TryGet(Name, out _);
}
