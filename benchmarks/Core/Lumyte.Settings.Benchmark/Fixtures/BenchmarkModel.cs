namespace Lumyte.Settings.Benchmark;

/// <summary>A module-owned model with generated JSON metadata.</summary>
public sealed class BenchmarkModel
{
    /// <summary>Gets or sets the validated scalar.</summary>
    public int Number { get; set; } = 1;

    /// <summary>Gets or sets the detached collection.</summary>
    public List<int> Values { get; set; } = [];
}
