using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.Benchmark.Integration;

/// <summary>Compares maintained BCL hashing over the defined fifteen uint lanes, not struct padding.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison")]
public class PipelineHashBenchmarks
{
    private readonly uint[] _words = Enumerable.Range(1, 15).Select(i => (uint)i).ToArray();

    /// <summary>Hashes each defined lane using the BCL accumulator.</summary>
    /// <returns>Process-local hash.</returns>
    [Benchmark]
    public int AddWords()
    {
        HashCode hash = default;
        foreach (uint word in _words)
        {
            hash.Add(word);
        }

        return hash.ToHashCode();
    }

    /// <summary>Hashes only the defined lanes using the BCL byte-span implementation.</summary>
    /// <returns>Process-local hash.</returns>
    [Benchmark]
    public int AddBytes()
    {
        HashCode hash = default;
        hash.AddBytes(MemoryMarshal.AsBytes(_words.AsSpan()));
        return hash.ToHashCode();
    }
}
