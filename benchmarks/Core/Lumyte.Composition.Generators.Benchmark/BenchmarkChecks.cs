using Microsoft.CodeAnalysis;

namespace Lumyte.Composition.Generators.Benchmark;

/// <summary>Deterministic compiler, output, and warm-rerun oracles.</summary>
public static class BenchmarkChecks
{
    /// <summary>Checks every change kind against a fresh driver and validates compilation.</summary>
    public static void Verify()
    {
        foreach (string change in new[] { "Unchanged", "Unrelated", "Target" })
        {
            var benchmark = new GeneratorBenchmarks { Count = 8, Change = change };
            benchmark.Setup();
            if (benchmark.ConsumerCompile() != 0 || benchmark.Cold().GeneratedTrees.Length == 0)
            {
                throw new InvalidOperationException("Generated consumer did not compile or emit.");
            }

            if (benchmark.ChangedConsumerCompile() != 0 || (change == "Target" && !benchmark.TargetOutputChanges()))
            {
                throw new InvalidOperationException("Target edit must compile and change emitted source before the incremental oracle is meaningful.");
            }

            for (int i = 0; i < 4; i++)
            {
                if (GeneratorBenchmarks.Fingerprint(benchmark.Warm()) != benchmark.FreshFingerprint(i % 2 == 0))
                {
                    throw new InvalidOperationException("Incremental output differs from fresh output.");
                }
            }

            if (!benchmark.DiagnoseInvalid().Diagnostics.Any(diagnostic => diagnostic.Id == "LYC001"))
            {
                throw new InvalidOperationException("Expected invalid declaration diagnostic.");
            }
        }
    }
}
